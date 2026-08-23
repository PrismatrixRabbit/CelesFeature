using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // 星铃轨道垃圾箱坠落（嵌套事件 worker——2026-08-15）
    // 内容生成三形态：物品/pawn（星铃空投舱砸开——G1 资产复用，faction 决定外观 DropPodUtility.cs:14 实证）、
    //   建筑（专属下降 def 落地 → r=10 生成容器并填战利品表）
    public class CelesFD_IncidentWorker_OrbitDrop : IncidentWorker
    {
        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            CelesFD_OrbitDropExtension ext = def.GetModExtension<CelesFD_OrbitDropExtension>();
            if (ext == null || ext.crates == null || ext.crates.Count == 0) return false;

            // 2026-08-15 用户裁决：集中坠落——一次确定中心（原版 DropThingGroupsNear dropCenter 附近语义），物品/pawn/建筑共用
            IntVec3 dropCenter = DropCellFinder.RandomDropSpot(map);
            var things = new List<Thing>();
            foreach (CelesFD_OrbitDropExtension.CelesFD_OrbitDropCrateEntry crateEntry in ext.crates)
            {
                if (crateEntry.crateDef == null || crateEntry.crateDef.contents.NullOrEmpty()) continue;
                int count = crateEntry.countRange.RandomInRange;
                for (int i = 0; i < count; i++)
                    GenerateCrate(map, dropCenter, crateEntry.crateDef, things);
            }

            if (things.Count > 0)
                DropPodUtility.DropThingsNear(dropCenter, map, things,
                    faction: CelesFD_BeaconUtility.BeaconFaction);   // 星铃空投舱（G1 资产）——同中心集中（DropThingGroupsNear 链）
            // 2026-08-15 用户裁决：letter 指向落点（"转至目标地点"+箭头——LookTargets 驱动，原版 ResourcePodCrash.cs:14 同款）
            SendStandardLetter(parms, new LookTargets(new TargetInfo(dropCenter, map)));
            return true;
        }

        // 单坠落物：内容按权重随机选一项（每坠落物独立抽取——用户裁决）→ 物品/pawn（装入舱）/ 建筑（下降 def → 落地 r=10 生成）
        private static void GenerateCrate(Map map, IntVec3 dropCenter, CelesFD_DropCrateDef crate, List<Thing> things)
        {
            CelesFD_DropContentEntry entry = crate.contents.RandomElementByWeight(e => e.weight);
            if (entry == null) return;
            switch (entry.type)
            {
                case CelesFD_DropContentType.Thing:
                {
                    ThingDef td = DefDatabase<ThingDef>.GetNamedSilentFail(entry.thingDefName);
                    if (td == null) return;
                    int left = entry.amountRange.RandomInRange;
                    while (left > 0)
                    {
                        Thing t = ThingMaker.MakeThing(td);
                        t.stackCount = Mathf.Min(left, td.stackLimit);
                        left -= t.stackCount;
                        things.Add(t);
                    }
                    break;
                }
                case CelesFD_DropContentType.PawnKind:
                {
                    PawnKindDef pk = DefDatabase<PawnKindDef>.GetNamedSilentFail(entry.pawnKindDefName);
                    if (pk == null) return;
                    int amount = Mathf.Max(1, entry.amountRange.RandomInRange);
                    for (int i = 0; i < amount; i++)
                        things.Add(GeneratePawn(pk, GetDefaultFaction(pk)));   // 默认派系（用户裁决：pawnKind.defaultFactionDef）
                    break;
                }
                case CelesFD_DropContentType.ManhunterAnimal:
                {
                    // 狂暴动物（原版 AggressiveAnimals.cs:47-51 模式：Scaria + ManhunterPermanent + exitMapAfterTick）
                    PawnKindDef pk = DefDatabase<PawnKindDef>.GetNamedSilentFail(entry.pawnKindDefName);
                    if (pk == null) return;
                    int amount = Mathf.Max(1, entry.amountRange.RandomInRange);
                    for (int i = 0; i < amount; i++)
                    {
                        Pawn animal = GeneratePawn(pk, GetDefaultFaction(pk));
                        animal.health.AddHediff(HediffDefOf.Scaria);
                        animal.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.ManhunterPermanent);
                        animal.mindState.exitMapAfterTick = Find.TickManager.TicksGame + Rand.Range(60000, 120000);
                        things.Add(animal);
                    }
                    break;
                }
                case CelesFD_DropContentType.RefugeeInjured:
                    // 逃生舱模式（原版实证：SpaceRefugee + 随机派系 60% + 无派系 40% + 重伤倒地）
                    things.Add(GenerateRefugeeInjured(map));
                    break;
                case CelesFD_DropContentType.Building:
                    SpawnBuildingDrop(map, dropCenter, crate);
                    break;
            }
        }

        // pawnKind 默认派系（PawnKindDef.defaultFactionDef——PawnKindDef.cs:13-14 实证；未配 → null 无派系）
        private static Faction GetDefaultFaction(PawnKindDef pk)
        {
            if (pk.defaultFactionDef == null) return null;
            return Find.FactionManager.AllFactions.FirstOrDefault(f => f.def == pk.defaultFactionDef);
        }

        // 逃生舱式：重伤倒地随机派系人类（ThingSetMaker_RefugeePod.cs:13-24 逐字复用）
        private static Pawn GenerateRefugeeInjured(Map map)
        {
            PawnGenerationRequest request = new PawnGenerationRequest(
                PawnKindDefOf.SpaceRefugee,
                DownedRefugeeQuestUtility.GetRandomFactionForRefugee(),
                PawnGenerationContext.NonPlayer, map?.Tile ?? 0,
                forceGenerateNewPawn: false, allowDead: false, allowDowned: false,
                canGeneratePawnRelations: true, mustBeCapableOfViolence: false, 20f,
                forceAddFreeWarmLayerIfNeeded: false, allowGay: true, allowPregnant: true);
            Pawn pawn = PawnGenerator.GeneratePawn(request);
            HealthUtility.DamageUntilDowned(pawn);   // ★ 重伤倒地（无法行动）
            return pawn;
        }

        private static Pawn GeneratePawn(PawnKindDef kind, Faction faction)
        {
            PawnGenerationRequest request = new PawnGenerationRequest(kind, faction, PawnGenerationContext.NonPlayer);
            return PawnGenerator.GeneratePawn(request);
        }

        // 建筑坠落：专属下降 def（fallerDef——贴图 = 建筑贴图）→ Skyfaller 落地（CelesFD_Skyfaller_OrbitCrate.Impact：r=10 生成）
        // 2026-08-15：集中但分散——每个坠落物在 dropCenter 附近 TryFindDropSpotNear（原版 DropThingGroupsNear 同款，DropPodUtility.cs:44 实证——
        //   修复：多个坠落物共用 dropCenter 致全部落同一坐标）
        private static void SpawnBuildingDrop(Map map, IntVec3 dropCenter, CelesFD_DropCrateDef crate)
        {
            if (crate.fallerDef == null)
            {
                Log.Warning($"[CelesFD] DropCrate {crate.defName} is building-type but has no fallerDef");
                return;
            }
            if (!DropCellFinder.TryFindDropSpotNear(dropCenter, map, out IntVec3 dropCell, allowFogged: true, canRoofPunch: true))
                dropCell = dropCenter;   // 兜底：找不到分散点用中心
            // inner 必须经 ThingMaker 生成（原版 MakeDropPodAt 同款 DropPodUtility.cs:14 实证——
            //   new ActiveTransporter() 未初始化 def/SpawnSetup → Skyfaller Tick 访问 inner 时 NRE）
            ActiveTransporter info = (ActiveTransporter)ThingMaker.MakeThing(ThingDefOf.ActiveDropPod);
            SkyfallerMaker.SpawnSkyfaller(crate.fallerDef, info, dropCell, map);
        }
    }
}
