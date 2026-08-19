using HarmonyLib;
using RimWorld;
using Verse;

namespace CelesFeature
{
    // 发射平台待机电力修复（2026-08-15，反编译实证）：
    // 根因链：读档 → 电网注册 → PowerNetManager delayedAction 调 CompPowerTrader.SetUpPowerVars
    //   （PowerNetManager.cs:133 RegisterTransmitter / :168 RegisterConnector）→ PowerOn=true →
    //   PowerOutput = -PowerConsumption（发射耗电，CompPowerTrader.cs:245-258）——覆盖
    //   CompLaunchCharge.PostSpawnSetup 的待机设置（其执行早于电网注册）→ 任何读档后平台恒为发射耗电
    // 修复：Postfix 恒设待机（生成/电网注册时无前摇，恒待机正确；前摇发射态由 BeginChargeLocal 运行期设置，不冲突；
    //   前摇中电网事件覆盖由 CompLaunchable.CompTick charging 分支恢复检查兜底——双保险）
    [HarmonyPatch(typeof(CompPowerTrader), "SetUpPowerVars")]
    public static class CelesFD_Patch_PowerTrader_LaunchPadIdle
    {
        public static void Postfix(CompPowerTrader __instance)
        {
            if (__instance.parent == null || __instance.parent.GetComp<CelesFD_CompLaunchCharge>() == null) return;   // 仅发射平台
            if (__instance.Props.idlePowerDraw > 0f)
                __instance.PowerOutput = -__instance.Props.idlePowerDraw;
        }
    }
}
