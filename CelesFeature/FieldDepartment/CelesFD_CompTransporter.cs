using System.Collections.Generic;
using RimWorld;
using Verse;

namespace CelesFeature
{
    // 货运单元装载 Comp（2026-08-15 鲁棒性修正）：
    //   不再完整重写 CompGetGizmosExtra（完整替代原版行为 = 兼容性风险：依赖 base 行为的 Mod 被破坏、原版更新差异暴露）——
    //   调 base 产出原版全部 gizmo（取消装载/编组选择/装载命令），后处理仅对装载命令施加电力检查：
    //   原版燃料检查因 requiresFuelingPort=false 不进入（实证 CompTransporter.cs:450-462）；
    //   编组计数在无 CompRefuelable 时与原版等价（实证 CompTransporter.cs:427-444）
    public class CelesFD_CompTransporter : CompTransporter
    {
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                // 电力检查（替代原版燃料检查的禁用语义）：本舱对应发射平台断电 → 装载命令禁用 + reason
                if (g is Command_LoadToTransporter cmd && !LaunchPadPowered())
                    cmd.Disable("CelesFD_LaunchGroupNoPower".Translate());
                yield return g;
            }
        }

        // 本舱对应发射平台电力（无平台或平台无电力 Comp → 视为通过）
        private bool LaunchPadPowered()
        {
            Building pad = CelesFD_LaunchPortUtility.GetLaunchPadForPod(parent as Building);
            CompPowerTrader pc = pad?.GetComp<CompPowerTrader>();
            return pc == null || pc.PowerOn;
        }
    }

    // 挂 CargoPod：compClass 换 CelesFD_CompTransporter（字段全部继承原版）
    public class CelesFD_CompProperties_Transporter : CompProperties_Transporter
    {
        public CelesFD_CompProperties_Transporter()
        {
            compClass = typeof(CelesFD_CompTransporter);
        }
    }
}
