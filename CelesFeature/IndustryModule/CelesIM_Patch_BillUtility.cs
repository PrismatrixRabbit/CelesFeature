using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace CelesFeature
{
    // ════════════════════════════════════════════════════════════════
    //  批 5 二类生产：Bill 实例化布线（裁决 R1 · 2026-09-29）
    //  · 原版工厂 BillUtility.MakeNewBill（:36-55）= 硬编码决策树，无 XML/virtual/回调通道
    //    （全库 billRecipeClass 零命中）——本 postfix 是 CelesIM_Bill_AutoProducer 唯一实例化点
    //  · 注册表 = thingClass==我方建筑的 ThingDef 的 recipes 全集（建筑 def 单侧 <recipes>
    //    链接，MechGestator 惯例；配方侧禁写 recipeUsers——AllRecipes 两源合并会重复）
    //  · 粘贴走 Clone 保类型（Activator）、存档走类名反序列化——均不经此，无需另 patch
    //  · 已知耦合（裁决 R3）：补丁失效时 cycles>0 配方路由 Bill_ProductionMech（红字可见）；
    //    单周期 a 配方省略 gestationCycles → 降级裸 Bill_Autonomous（可用，仅失增强）
    // ════════════════════════════════════════════════════════════════
    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(BillUtility), nameof(BillUtility.MakeNewBill))]
    public static class CelesIM_Patch_BillUtility
    {
        internal static readonly HashSet<RecipeDef> AutoProducerRecipes = new HashSet<RecipeDef>();

        static CelesIM_Patch_BillUtility()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
            {
                if (def.thingClass == typeof(CelesIM_Building_AutoProducer) && def.recipes != null)
                {
                    foreach (RecipeDef recipe in def.recipes)
                    {
                        AutoProducerRecipes.Add(recipe);
                    }
                }
            }
        }

        // bill 创建唯一生产口（ITab_Bills.cs:114）；precept 透传（Ideo 风格变体加单兼容）
        public static void Postfix(RecipeDef recipe, Precept_ThingStyle precept, ref Bill __result)
        {
            if (AutoProducerRecipes.Contains(recipe))
            {
                __result = new CelesIM_Bill_AutoProducer(recipe, precept);
            }
        }
    }
}
