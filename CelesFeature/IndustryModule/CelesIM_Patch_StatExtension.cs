using HarmonyLib;
using RimWorld;
using Verse;

namespace CelesFeature
{
    // ════════════════════════════════════════════════════════════════
    //  批 4（E5）：一类生产效率因子接入——Harmony Postfix（P1 方案，裁决 2026-09-26）
    //  · 位置：StatExtension.GetStatValue 最外层——原版 parts 循环
    //    （StatWorker.FinalizeValue :793-799：无电/房间角色/温度/室外四项）之后，
    //    相乘不重叠（自建三项=offline×预热×produceEfic，见 Consumer.GetWorkSpeedFactor）
    //  · 性能（完整实证 2026-09-26）：bill 路径每 tick 全链重算零缓存=原版基线；
    //    本 postfix 增量=引用比较+is+TryGetComp（1.6 compsByType 类型字典 O(1)，
    //    ThingWithComps.cs:140-149）+一次乘法——<1% 原版 StatPart 查询成本
    //  · 显示（方案 A 裁决）：总值守恒分解缺口——原版行/Dialog 分解页总值含因子但解释
    //    不单列（postfix 无 ExplanationPart 机制）；动态当前值由 Consumer Inspect
    //    「工作系数」行承担；静态规格（预热时长/效率最小值）走 statBases 两 StatDef
    //  · 注册：PatchMain 自动扫描 [HarmonyPatch] 类（零登记）
    // ════════════════════════════════════════════════════════════════
    [HarmonyPatch(typeof(StatExtension), nameof(StatExtension.GetStatValue))]
    public static class CelesIM_Patch_StatExtension
    {
        public static void Postfix(Thing thing, StatDef stat, ref float __result)
        {
            if (stat != StatDefOf.WorkTableWorkSpeedFactor)
                return;
            CelesIM_CompThreadConsumer consumer = thing.TryGetComp<CelesIM_CompThreadConsumer>();
            if (consumer == null)
                return;
            __result *= consumer.GetWorkSpeedFactor();
        }
    }
}
