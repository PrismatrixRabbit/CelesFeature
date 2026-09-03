using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace CelesFeature
{
    // W-1：支援呼叫器穿戴拦截（复刻原版武器规则，2026-08-31）
    // 原版 FloatMenuOptionProvider_Equip（:26-41）对武器做暴力/操作检查，但 FloatMenuOptionProvider_Wear（穿衣）无此检查
    // （服装无暴力语义）——呼叫器是 apparel，需按武器规则拦截。挂载点 = EquipmentUtility.CanEquip（Wear provider :54 的收敛检查：
    //   CanEquip false → Wear provider 自动出"无法穿上：{reason}"禁用项——单一选项，无 provider 冲突）
    // 文案：原版 Keyed（IsIncapableOfViolenceLower"无法进行暴力行为" / Incapable"无法胜任"），gizmo 侧同源
    // 空 [HarmonyPatch] + TargetMethod()：特性实参须常量（typeof(string).MakeByRefType() 非常量表达式——编译错），
    //   运行时取精确重载（CanEquip(Thing, Pawn, out string, bool) 四参）
    [HarmonyPatch]
    public static class CelesFD_Patch_EquipmentUtility_CanEquip
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(EquipmentUtility), "CanEquip",
                new[] { typeof(Thing), typeof(Pawn), typeof(string).MakeByRefType(), typeof(bool) });
        }

        public static bool Prefix(Thing thing, Pawn pawn, out string cantReason, ref bool __result)
        {
            cantReason = null;
            if (thing == null || thing.def != CelesFD_DefOf.CelesFD_SupportCaller)
                return true;   // 非呼叫器 → 交还原版检查（bladelink/生物编码/角色/生命阶段）
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                cantReason = "IsIncapableOfViolenceLower".Translate(pawn.LabelShort, pawn);
                __result = false;
                return false;
            }
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
            {
                cantReason = "Incapable".Translate().CapitalizeFirst();
                __result = false;
                return false;
            }
            return true;   // 通过 → 原版检查继续（生命阶段等）
        }
    }
}
