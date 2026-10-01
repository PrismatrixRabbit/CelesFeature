using System.Collections.Generic;
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

    // ═══ U2：b 模型提取路由（D12+F5-31⑵ · 2026-10-02）═══
    // MakeRecipeProducts 对 race 产物=ThingMaker.MakeThing(Pawn def) 错误路径（未初始化裸 Pawn）——
    // prefix skip 整个迭代器（外壳方法层拦截=状态机从不创建体内不执行；与 D19 品质闸 postfix
    // 拦不到体内为对偶原理：skip 不依赖体内时机）；
    // a 模型不命中（category!=Pawn）原样放行——「零 patch」承诺对 a 保持。
    // skip 后 toil 层 ConsumeIngredients（b 模型空表无害）/Notify_IterationCompleted（Reset 复位）照常执行
    //（Toils_Recipe.cs:199-201）；Pawn 产物无 CompQuality=品质闸 prefix 天然放行，两闸无交集。
    [HarmonyPatch(typeof(GenRecipe), "MakeRecipeProducts")]
    public static class CelesIM_Patch_GenRecipe_PawnRoute
    {
        public static bool Prefix(RecipeDef recipeDef, IBillGiver billGiver, ref IEnumerable<Thing> __result)
        {
            if (!(billGiver is CelesIM_Building_AutoProducer producer) || !CelesIM_Bill_AutoProducer.ProducesPawn(recipeDef))
            {
                return true;
            }
            Pawn pawn = producer.GestatingPawn;
            if (pawn == null)
            {
                Log.Error("[CelesIM] Pawn-route extraction hit but container holds no Pawn. recipe=" + recipeDef.defName);
                __result = new List<Thing>();   // 空产物终止（勿回落原方法——MakeThing(race)=错产）
                return false;
            }
            __result = new List<Thing> { pawn };
            return false;
        }
    }
}
