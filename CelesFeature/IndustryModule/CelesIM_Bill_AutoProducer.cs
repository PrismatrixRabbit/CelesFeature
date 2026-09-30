using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // ════════════════════════════════════════════════════════════════
    //  批 5 二类生产：自主生产单（U1 单元 · 2026-09-29）
    //  · 母本：Bill_Mech.cs:147-177 抄改（多周期回环+周期显示）——不继承 Bill_Mech
    //    （剥 mechanitor/带宽/Gestator 硬 cast，D2 裁决）
    //  · 速度乘数（D5）：建筑 recipe.workTableSpeedStat（默认 WorkTableWorkSpeedFactor，
    //    RecipeDef.cs:484-490 零配置）——批 4 postfix 链天然接管（预热/依赖 floor/环境 StatPart）
    //  · 实例化布线：CelesIM_Patch_BillUtility（MakeNewBill postfix+启动期注册表）——
    //    原版工厂无 XML 通道（BillUtility.cs:36-55 硬编码决策树）
    //  · 显式无参构造必须（Clone 走 Activator.CreateInstance(GetType())，Bill.cs:462）
    //  · cycles 语义：配方省略 gestationCycles（=0）= 单周期（1>=0 首周期即完成）——
    //    布线补丁失效时自然降级为裸 Bill_Autonomous（可用，仅失增强，裁决 R3）
    // ════════════════════════════════════════════════════════════════
    public class CelesIM_Bill_AutoProducer : Bill_Autonomous
    {
        private int cyclesDone;   // 已完成周期数（母本 Bill_Mech.gestationCycles 同位）

        public CelesIM_Bill_AutoProducer()
        {
        }

        public CelesIM_Bill_AutoProducer(RecipeDef recipe, Precept_ThingStyle precept = null)
            : base(recipe, precept)
        {
        }

        private CelesIM_Building_AutoProducer Producer => (CelesIM_Building_AutoProducer)billStack.billGiver;

        private float WorkSpeedMultiplier => Producer.GetStatValue(recipe.workTableSpeedStat);

        // 母本 :147-168 逐行抄改（乘数换源：绑定者 stat → 建筑 stat）
        public override void BillTick()
        {
            if (suspended || state != FormingState.Forming)
            {
                return;
            }
            formingTicks -= 1f * WorkSpeedMultiplier;
            if (formingTicks <= 0f)
            {
                cyclesDone++;
                if (cyclesDone >= recipe.gestationCycles)
                {
                    state = FormingState.Formed;
                    Producer.Notify_FormingCompleted();
                }
                else
                {
                    formingTicks = recipe.formingTicks;
                    state = FormingState.Preparing;   // 多周期回环：等待下一轮工作段（原料全程在容器，非再装料）
                }
            }
        }

        // 母本 :133-138 同位抄改（去 boundPawn 行——我方无绑定者）：
        // 迭代复位单点（Notify_IterationCompleted/Destroy/EjectContents 三入口共用）——
        // 漏抄此件 = cyclesDone 跨迭代存活（剩余周期逐轮递减；多周期配方第二次生产一步完成=U2 阻断）
        public override void Reset()
        {
            base.Reset();
            cyclesDone = 0;
        }

        // 母本 :170-177 抄改 + 两处增补：Gathering 装料计数行（Bill_ProductionMech 同款观察位）、
        // Formed 完成行（母本无——补基类 Finished 语义）；周期总数 Max(1,·)（cycles=0 显示「共 1」）
        public override void AppendInspectionData(StringBuilder sb)
        {
            if (State == FormingState.Gathering)
            {
                AppendCurrentIngredientCount(sb);
            }
            else if (State == FormingState.Forming || State == FormingState.Preparing)
            {
                // 2026-09-30 裁决：孕育→锻造——两行换我方 Keyed（时间串预格式化，星铃规范一）
                sb.AppendLine("CelesIM_Keyed_CurrentForgeCycle".Translate(
                    ((int)(formingTicks * (1f / WorkSpeedMultiplier))).ToStringTicksToPeriod()));
                int total = Mathf.Max(1, recipe.gestationCycles);   // cycles=0 显示「共 1」（单周期语义）
                sb.AppendLine("CelesIM_Keyed_RemainingForgeCycles".Translate(total - cyclesDone, total));
            }
            else if (State == FormingState.Formed)
            {
                sb.AppendLine("Finished".Translate());
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref cyclesDone, "cyclesDone", 0);
        }
    }
}
