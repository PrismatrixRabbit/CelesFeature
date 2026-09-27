using HarmonyLib;
using RimWorld;
using Verse;

namespace CelesFeature
{
    // ════════════════════════════════════════════════════════════════
    //  批 4 终版（2026-09-26）：依赖型建筑未连接不可工作——bill 拦截
    //  · 断电同位同形（Building_WorkTable.cs:92「!CanWorkWithoutPower && !PowerOn → false」形态），
    //    不自行造轮子；patch CurrentlyUsableForBills（bill 可用性唯一闸）
    //  · 条件：挂 Consumer + 依赖型（Celes_ThreadDependency < 0.5）+ 未连接 → false
    // ════════════════════════════════════════════════════════════════
    [HarmonyPatch(typeof(Building_WorkTable), nameof(Building_WorkTable.CurrentlyUsableForBills))]
    public static class CelesIM_Patch_WorkTableBillsUsable
    {
        public static void Postfix(Building_WorkTable __instance, ref bool __result)
        {
            if (!__result)
                return;
            CelesIM_CompThreadConsumer consumer = __instance.TryGetComp<CelesIM_CompThreadConsumer>();
            if (consumer == null)
                return;
            if (__instance.GetStatValue(CelesIM_DefOf.Celes_ThreadDependency) < 0.5f
                && !consumer.IsConnected && !consumer.devForcedActive)   // DEV 虚拟连接豁免（测试离网工作）
                __result = false;
        }
    }
}
