using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace CelesFeature
{
    // ════════════════════════════════════════════════════════════════
    //  依赖型建筑未连接不可工作——bill 双闸拦截（值义反转修订 2026-09-27：1=依赖/0=可离网）
    //  · 断电同位同形（Building_WorkTable.cs :92/:104 两处同款条件），不自行造轮子
    //  · CurrentlyUsableForBills（bill 执行层）+ UsableForBillsAfterFueling（WorkGiver 派发层
    //    :141——缺此殖民者会接单空转）双 patch
    //  · 拦截时 JobFailReason.Is → 右键工作菜单自动灰显（FloatMenuOptionProvider_WorkGivers
    //    :127-130 灰显链：HasJobOnThing false + HaveReason → Disabled 项，原版模式零自造）
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
            if (consumer.CannotWorkUnconnected)
            {
                __result = false;
                JobFailReason.Is("CelesIM_Keyed_CannotWorkUnlinked".Translate());
            }
        }
    }

    [HarmonyPatch(typeof(Building_WorkTable), nameof(Building_WorkTable.UsableForBillsAfterFueling))]
    public static class CelesIM_Patch_WorkTableBillsUsableAfterFueling
    {
        public static void Postfix(Building_WorkTable __instance, ref bool __result)
        {
            if (!__result)
                return;
            CelesIM_CompThreadConsumer consumer = __instance.TryGetComp<CelesIM_CompThreadConsumer>();
            if (consumer == null)
                return;
            if (consumer.CannotWorkUnconnected)
            {
                __result = false;
                JobFailReason.Is("CelesIM_Keyed_CannotWorkUnlinked".Translate());
            }
        }
    }
}
