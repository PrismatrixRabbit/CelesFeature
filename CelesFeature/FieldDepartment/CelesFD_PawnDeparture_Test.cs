using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace CelesFeature
{
    // G9 人员离场测试工具类。
    // 注意：类名带 Test 后缀——当前为里程碑 3 G9 验证用途；
    // 未来人员增援模块（策划案 §4）落地时调整（去 Test、接入正式离场/恢复流程）。
    public static class CelesFD_PawnDeparture_Test
    {
        private static PawnKindDef celesColonistKind;

        private static PawnKindDef CelesColonistKind
        {
            get
            {
                if (celesColonistKind == null)
                {
                    celesColonistKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Celes_Colonist");
                }
                return celesColonistKind;
            }
        }

        // 离场：校验 → 放下搬运物 → Farskip 出发特效 → 移出地图 → 世界化（KeepForever 防 GC）。
        // 失败一律 Messages 左上角小字提示（同空投仓货物丢失模式，TravellingTransporters.cs:324），不报 dev log；
        // 全部校验前置拦截，避免触发原版 PassToWorld / ExitMap 的 Log.Error / Log.Warning 路径。
        public static bool Departure(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed || !pawn.Spawned
                || pawn.kindDef != CelesColonistKind
                || Find.WorldPawns.Contains(pawn) || pawn.Discarded)
            {
                Fail("CelesFD_DepartureFailInvalid".Translate(), pawn);
                return false;
            }

            // 放下搬运物与被背者（原版 CommandDropPawn 同款 API，Pawn_CarryTracker.cs:132；"空手离场"裁决）
            if (pawn.carryTracker.CarriedThing != null
                && !pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _))
            {
                Fail("CelesFD_DepartureFailDrop".Translate(), pawn);
                return false;
            }

            // Farskip 出发特效——格子锚定（仿原版 CompAbilityEffect_Farskip.cs:25-26 绑定 item.Position 而非 Thing）；
            // 特效由 map.effecterMaintainer / map.flecks 驱动（Map tick），随游戏暂停而暂停
            IntVec3 pos = pawn.Position;
            Map map = pawn.Map;
            FleckCreationData data = FleckMaker.GetDataAttachedOverlay(pawn, FleckDefOf.PsycastSkipFlashEntry, new Vector3(-0.5f, 0f, -0.5f));
            data.link.detachAfterTicks = 5;
            map.flecks.CreateFleck(data);
            Effecter effecter = EffecterDefOf.Skip_Entry.Spawn(pawn, map);
            map.effecterMaintainer.AddEffecterToMaintain(effecter, pos, 60);
            SoundDefOf.Psycast_Skip_Pulse.PlayOneShot(new TargetInfo(pos, map));

            // 移出地图 + 世界化（ExitMap 内部完成 DeSpawn + PassToWorld(Decide)，Pawn.cs:2505-2597）。
            // teleporting 窗口保持到 KeepForever 之后：Notify_PassedToWorld（Pawn.cs:1851-1881）对玩家派系 pawn
            // 在 situation==Free 时随机重分配派系（TryGetRandomNonColonyHumanlikeFaction → SetFaction）；
            // teleporting=true 时 GetSituation 返回 Teleporting（WorldPawns.cs:309-312）≠ Free → 跳过重分配（原版内建解法）
            pawn.teleporting = true;
            pawn.ExitMap(allowedToJoinOrCreateCaravan: false, Rot4.Invalid);
            Find.WorldPawns.RemovePawn(pawn);
            Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever); // KeepForever 防 GC（WorldPawns.cs:226-229；Decide 模式可被 GC 回收）
            pawn.teleporting = false;
            return true;
        }

        // 恢复：世界取回 → 当前地图选点生成 → Farskip 抵达特效（数据保留验证：Hediff/装备/心情）。
        public static bool ReturnToMap(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed || pawn.Spawned
                || !Find.WorldPawns.Contains(pawn)
                || pawn.kindDef != CelesColonistKind)
            {
                Fail("CelesFD_DepartureFailReturn".Translate(), pawn);
                return false;
            }
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Fail("CelesFD_DepartureFailNoMap".Translate(), null);
                return false;
            }
            if (!CellFinder.TryFindRandomSpawnCellForPawnNear(map.Center, map, out IntVec3 cell, 4))
            {
                Fail("CelesFD_DepartureFailNoMap".Translate(), null);
                return false;
            }

            Find.WorldPawns.RemovePawn(pawn);
            GenSpawn.Spawn(pawn, cell, map);

            // 抵达特效——格子锚定（与离场端对称，避免锚定方式歧义；原版抵达端 CompAbilityEffect_Farskip.cs:65-66，def 无 maintainTicks → 手动维护 60 tick）
            Effecter effecter = EffecterDefOf.Skip_ExitNoDelay.Spawn(pawn, map);
            map.effecterMaintainer.AddEffecterToMaintain(effecter, cell, 60);
            SoundDefOf.Psycast_Skip_Exit.PlayOneShot(new TargetInfo(cell, map));
            pawn.Notify_Teleported();
            return true;
        }

        private static void Fail(string text, Pawn pawn)
        {
            if (pawn != null)
            {
                Messages.Message(text, pawn, MessageTypeDefOf.NegativeEvent);
            }
            else
            {
                Messages.Message(text, MessageTypeDefOf.NegativeEvent);
            }
        }
    }
}
