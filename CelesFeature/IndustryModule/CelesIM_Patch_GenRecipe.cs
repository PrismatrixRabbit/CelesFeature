using HarmonyLib;
using RimWorld;
using Verse;

namespace CelesFeature
{
    // ════════════════════════════════════════════════════════════════
    //  批 5 二类生产：品质闸（D1 · 裁决 R2 · 2026-09-29）
    //  · 位点：GenRecipe.PostProcessProduct（private static，全库唯一消费者 GenRecipe）——
    //    MakeRecipeProducts 是迭代器方法，postfix 拦不到体内红字（:84-89 workSkill==null
    //    → Log.Error + 品质 roll），prefix skip 是唯一「黄字替代红字」形态（零拷贝）
    //  · skip 同时跳过品质 roll/CompArt 署名/Ideo 风格/工艺通知——机器量产语义（R2 裁决）
    //  · 无品质产物条件不命中 = 原样通过（a 模型对正常产物「零 patch」承诺保持）
    //  · U2 生长点：同文件追加 MakeRecipeProducts prefix（category==Pawn 容器路由）
    // ════════════════════════════════════════════════════════════════
    [HarmonyPatch(typeof(GenRecipe), "PostProcessProduct")]
    public static class CelesIM_Patch_GenRecipe
    {
        public static bool Prefix(Thing product, RecipeDef recipeDef, ref Thing __result)
        {
            if (!CelesIM_Patch_BillUtility.AutoProducerRecipes.Contains(recipeDef))
            {
                return true;
            }
            CompQuality compQuality = product.TryGetComp<CompQuality>();
            if (compQuality == null)
            {
                return true;
            }
            compQuality.SetQuality(QualityCategory.Normal, ArtGenerationContext.Colony);
            Log.Warning("[CelesIM] " + "CelesIM_Keyed_QualityForced".Translate(product.Label));
            __result = product;
            return false;
        }
    }
}
