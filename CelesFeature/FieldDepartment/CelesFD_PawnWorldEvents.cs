using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace CelesFeature
{
    // ═══ R-2 P12：7 日窗口事件化——Harmony ×2 postfix ═══
    // PassedToWorld = pawn 离图（走边缘/传送）→ 检查是否在 PendingSettlement 中 → 翻案（回归）
    // Kill = pawn 死亡 → 检查是否在 PendingSettlement 中 → 标损
    // 两个 postfix 均只做标记（写入 PendingSettlement 内部状态），结算判定由 GameComponent 轮询/事件后调 DifferentialSettle

    [StaticConstructorOnStartup]
    public static class CelesFD_PawnWorldEvents
    {
        static CelesFD_PawnWorldEvents()
        {
            Harmony harmony = new Harmony("com.celesfeature.r2events");
            harmony.Patch(typeof(Pawn).GetMethod(nameof(Pawn.Notify_PassedToWorld)),
                postfix: new HarmonyMethod(typeof(CelesFD_PawnWorldEvents), nameof(PawnPassedToWorld)));
            harmony.Patch(typeof(Pawn).GetMethod(nameof(Pawn.Kill)),
                postfix: new HarmonyMethod(typeof(CelesFD_PawnWorldEvents), nameof(PawnKilled)));
        }

        // pawn 离图（PassedToWorld）→ 翻案（从 pending 列表移除 = 回归成功）
        static void PawnPassedToWorld(Pawn __instance)
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return;
            string id = __instance.thingIDNumber.ToString();
            foreach (var ps in gc.PendingSettlements)
            {
                if (!ps.pendingPawnIds.Contains(id)) continue;
                ps.pendingPawnIds.Remove(id);
                // 修7：计算回归前后双 completeness——Letter 3 精确阶段 label 数据源
                float oldCompleteness = ps.CurrentCompleteness(ps.returnedIds);
                ps.returnedIds.Add(id);
                float newCompleteness = ps.CurrentCompleteness(ps.returnedIds);
                CelesFD_Settlement.DifferentialSettleIfCrossed(gc, ps, __instance.LabelShort,
                    oldCompleteness, newCompleteness);
                break;
            }
        }

        // pawn 死亡 → 标损（从 pending 列表移除 = 损失确认——静默，不发 letter）
        static void PawnKilled(Pawn __instance)
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return;
            string id = __instance.thingIDNumber.ToString();
            foreach (var ps in gc.PendingSettlements)
            {
                if (!ps.pendingPawnIds.Contains(id)) continue;
                ps.pendingPawnIds.Remove(id);   // 死亡 = 损失（不加入 returnedIds）
                float oldCompleteness = ps.CurrentCompleteness(ps.returnedIds);
                // 死亡不增加 returnedIds——newCompleteness 不变（但可能因 loss 跨节点降档）
                CelesFD_Settlement.DifferentialSettleIfCrossed(gc, ps, null,
                    oldCompleteness, oldCompleteness);
                break;
            }
        }
    }
}
