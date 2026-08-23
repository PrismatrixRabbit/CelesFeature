using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // 轨道垃圾箱容器（可打开搜刮——Building_Casket 基类：Open→EjectContents 释放内容（TryDropAll）；
    //   innerContainer = ThingOwner<Thing>（Building_Casket.cs:36 实证——可装 pawn——搜刮出 pawn）
    // 内容按战利品表生成（FillFromLootTable——每容器独立抽一项 + 含 PawnKind/狂暴动物/逃生舱模式）
    public class CelesFD_Building_OrbitCrate : Building_Casket
    {
        private CelesFD_LootTableDef lootTableDef;   // 战利品表引用（SpawnSetup 填内容用——[Unsaved] 无需存档：落地即填）

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            // 2026-08-15 用户裁决：showContents 控制内容显示（false = 未知内容——Building_Casket.cs:176 实证）；
            // 玩家派系原版默认 contentsKnown=true（:91-96）——覆写按表配置
            CelesFD_OrbitDropFallerExtension ext = def.GetModExtension<CelesFD_OrbitDropFallerExtension>();
            CelesFD_DropCrateDef crate = ext?.crateDef;
            if (crate?.lootTableDef != null)
            {
                lootTableDef = crate.lootTableDef;
                if (!lootTableDef.showContents)
                    contentsKnown = false;
            }
        }

        // 按战利品表填内容（2026-08-15 用户裁决：每容器独立抽一项——RandomElementByWeight，与坠落物语义一致）
        public void FillFromLootTable(CelesFD_LootTableDef table)
        {
            if (table == null || table.contents == null || table.contents.Count == 0) return;
            CelesFD_DropContentEntry entry = table.contents.RandomElementByWeight(e => e.weight);
            if (entry == null) return;
            int amount = Mathf.Max(1, entry.amountRange.RandomInRange);
            for (int i = 0; i < amount; i++)
            {
                Thing content = null;
                switch (entry.type)
                {
                    case CelesFD_DropContentType.Thing:
                        ThingDef td = DefDatabase<ThingDef>.GetNamedSilentFail(entry.thingDefName);
                        if (td != null) content = ThingMaker.MakeThing(td);
                        break;
                    case CelesFD_DropContentType.PawnKind:
                        PawnKindDef pk = DefDatabase<PawnKindDef>.GetNamedSilentFail(entry.pawnKindDefName);
                        if (pk != null)
                            content = PawnGenerator.GeneratePawn(new PawnGenerationRequest(pk, GetDefaultFaction(pk), PawnGenerationContext.NonPlayer));
                        break;
                    case CelesFD_DropContentType.ManhunterAnimal:
                        content = GenerateManhunterAnimal(entry.pawnKindDefName);
                        break;
                    case CelesFD_DropContentType.RefugeeInjured:
                        content = GenerateRefugeeInjured();
                        break;
                }
                if (content != null)
                    innerContainer.TryAdd(content);
            }
        }

        // pawnKind 默认派系（PawnKindDef.defaultFactionDef——PawnKindDef.cs:13-14 实证；未配 → null 无派系）
        private static Faction GetDefaultFaction(PawnKindDef pk)
        {
            if (pk.defaultFactionDef == null) return null;
            return Find.FactionManager.AllFactions.FirstOrDefault(f => f.def == pk.defaultFactionDef);
        }

        // 狂暴动物（原版 AggressiveAnimals.cs:47-51 完整模式：Scaria hediff + ManhunterPermanent + exitMapAfterTick）
        private static Pawn GenerateManhunterAnimal(string pawnKindDefName)
        {
            PawnKindDef pk = DefDatabase<PawnKindDef>.GetNamedSilentFail(pawnKindDefName);
            if (pk == null) return null;
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(pk, GetDefaultFaction(pk), PawnGenerationContext.NonPlayer));
            pawn.health.AddHediff(HediffDefOf.Scaria);
            pawn.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.ManhunterPermanent);
            pawn.mindState.exitMapAfterTick = Find.TickManager.TicksGame + Rand.Range(60000, 120000);
            return pawn;
        }

        // 逃生舱式（ThingSetMaker_RefugeePod.cs:13-24 同款：SpaceRefugee + 随机派系 + 重伤倒地）
        private static Pawn GenerateRefugeeInjured()
        {
            PawnGenerationRequest request = new PawnGenerationRequest(
                PawnKindDefOf.SpaceRefugee, DownedRefugeeQuestUtility.GetRandomFactionForRefugee(),
                PawnGenerationContext.NonPlayer, 0,
                forceGenerateNewPawn: false, allowDead: false, allowDowned: false,
                canGeneratePawnRelations: true, mustBeCapableOfViolence: false, 20f,
                forceAddFreeWarmLayerIfNeeded: false, allowGay: true, allowPregnant: true);
            Pawn pawn = PawnGenerator.GeneratePawn(request);
            HealthUtility.DamageUntilDowned(pawn);
            return pawn;
        }
    }
}
