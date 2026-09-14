using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace CelesFeature
{
    // ═══ R-2：G9 传送离场/回归产品化（从 PawnDeparture_Test 演进——泛化 pawnkind 限制）═══
    // 离场链路：校验 → 放下搬运物 → Farskip 出发特效 → ExitMap → teleporting 窗口 → KeepForever
    // 回归链路：世界取回 → 当前地图选点 → Farskip 抵达特效
    // 与 Test 版差异：去除 CelesColonistKind 限制（支援人员可为任意 pawnkind 含机械体）
    public static class CelesFD_PawnTeleport
    {
        // 传送离场（返回 true = 成功回家 / false = 不可传送——走边缘兜底）
        public static bool TryTeleportOut(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed || !pawn.Spawned || pawn.Discarded)
                return false;
            // 放下搬运物与被背者（原版 CommandDropPawn 同款——"空手离场"裁决）
            if (pawn.carryTracker != null && pawn.carryTracker.CarriedThing != null
                && !pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _))
                return false;
            IntVec3 pos = pawn.Position;
            Map map = pawn.Map;
            // Farskip 出发特效（格子锚定——仿 CompAbilityEffect_Farskip.cs:25-26）
            FleckCreationData data = FleckMaker.GetDataAttachedOverlay(pawn, FleckDefOf.PsycastSkipFlashEntry, new Vector3(-0.5f, 0f, -0.5f));
            data.link.detachAfterTicks = 5;
            map.flecks.CreateFleck(data);
            Effecter effecter = EffecterDefOf.Skip_Entry.Spawn(pawn, map);
            map.effecterMaintainer.AddEffecterToMaintain(effecter, pos, 60);
            SoundDefOf.Psycast_Skip_Pulse.PlayOneShot(new TargetInfo(pos, map));
            // 移出地图 + 世界化（teleporting 窗口防派系重分配——Pawn.cs:1851-1881 原版内建解法）
            pawn.teleporting = true;
            pawn.ExitMap(allowedToJoinOrCreateCaravan: false, Rot4.Invalid);
            Find.WorldPawns.RemovePawn(pawn);
            Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
            pawn.teleporting = false;
            Log.Message("[CelesFD] Teleport out: " + pawn.LabelShort + " (" + pawn.KindLabel + ")");
            return true;
        }
    }
}
