using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

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

        // ═══ U2：b 模型（Pawn 产出，2026-10-02）═══
        // 判别器单点（D12+F5-31⑵ 单点纪律）：本覆写与提取路由 patch（Patch_GenRecipe）两消费方共用。
        // category 比较用 ThingCategory.Pawn 枚举（原版惯例 Building_Door.cs:175）——
        // ThingCategoryDefOf 系物品分类树 DefOf，与 Thing.category 无关且无 Pawn 字段。
        public static bool ProducesPawn(RecipeDef r)
        {
            return r.ProducedThingDef?.category == ThingCategory.Pawn;
        }

        // 母本 Bill_ProductionMech.CreateProducts（:20-27）抄改：
        // ①BoundPawn.Faction → Producer.Faction（我方无绑定者，D2 剥 mechanitor；billGiver 为
        //   IBillGiver 接口无 Faction——经 Producer 属性转 Building 取 Thing.Faction）；
        // ②Overseer 绑定行 D34（10-02）恢复——D2 时代剥除，D32（b 模型=Biotech+机械师全链）翻转时
        //   漏枚举此第五消费点（红线⑥实例：裁决翻转须全枚举消费点），用户游戏内捕获"产出 mech 不绑
        //   机械师"补齐：绑 boundPawn（恒绑后必为做单机械师；守卫死亡——Cleaner 无死亡冻结可能
        //   死绑到达，跳过=该 mech 无监督出生，报备行为）。
        // PawnGenerationRequest 具名传参（母本位置参数冗余值=构造器默认可省，PawnGenerationRequest.cs:151）：
        // developmentalStages 默认 Adult 须显式覆盖 Newborn（机械体新生儿）；allowDowned 默认 false 须显式 true（母本同）。
        // 未命中判别器 → null（a 模型：提取时 MakeRecipeProducts 现做，原路径不动）。
        public override Thing CreateProducts()
        {
            if (!ProducesPawn(recipe))
            {
                return null;
            }
            PawnKindDef kind = DefDatabase<PawnKindDef>.AllDefs.Where((PawnKindDef pk) => pk.race == recipe.ProducedThingDef).First();
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, Producer.Faction,
                PawnGenerationContext.NonPlayer, allowDowned: true, developmentalStages: DevelopmentalStage.Newborn));
            if (boundPawn != null && !boundPawn.Dead)
            {
                boundPawn.relations.AddDirectRelation(PawnRelationDefOf.Overseer, pawn);
            }
            return pawn;
        }

        // ═══ U2 增补（2026-10-02）：操作者绑定（D31 裁决——原版 Bill_Mech 绑定体验同款，玩家零学习成本）═══
        // 开关=配方 ext CelesIM_PawnProductExt.bindOperator（仅 Pawn 产物配方生效）；Cleaner 对照轨不挂=全链关。
        // 消费点五处：Notify_DoBillStarted 绑定/PawnAllowedToStartAnew 拒绝+灰显/Reset 复位（红线 2 单点）/
        // Building.CanWork 死亡冻结（BindingBroken）/AppendInspectionData Preparing 提示行。
        // ⚠ 原版 CanBeUsedNowBy（Building_MechGestator:95-102）系全库零消费死槽——独占真身=此处拒绝检查。

        private Pawn boundPawn;   // 绑定操作者（母本 Bill_Mech.boundPawn 同位 :12/Scribe :182）

        public Pawn BoundPawn => boundPawn;

        // 绑定开关解析（last-wins 同 FormingGraphicExt 模式；判别器双闸=仅 b 模型可绑）
        private bool BindOperatorEnabled => ProducesPawn(recipe)
            && recipe.modExtensions?.OfType<CelesIM_PawnProductExt>().LastOrDefault()?.bindOperator == true;

        // 绑定开启且绑定者已死=冻结信号（Building.CanWork 消费——与断电/断连冻结同构 D7 语言；
        // 原版意图 BoundPawnStateAllowsForming :56-66 同款。偏离报备：原版电仍满载/音仍响，我方统一全冻结）
        public bool BindingBroken => BindOperatorEnabled && boundPawn != null && boundPawn.Dead;

        // 母本 :125-132 同款：谁来做就绑谁——**恒绑**（D34 修订 10-02：去 BindOperatorEnabled 条件；
        // boundPawn=操作者记录，CreateProducts 的 Overseer 绑定依赖它——两配方 mech 产出都必须有
        // 监督者，否则 race def 的 OverseerSubject comp 启动野化倒计时）。mechanitorOnlyRecipe 保证
        // 做单者必为机械师 → 绑定者必为机械师。bindOperator 开关收窄为三消费：独占拒绝/死亡冻结/
        // Inspect 名字行（Cleaner=绑而不独占不冻结不显示行）
        public override void Notify_DoBillStarted(Pawn billDoer)
        {
            base.Notify_DoBillStarted(billDoer);
            if (boundPawn != billDoer)
            {
                boundPawn = billDoer;
            }
        }

        // ═══ U2 修订（D32，2026-10-02）：带宽检查——机械师限定全链补齐 ═══
        // 自算等价 Pawn_MechanitorTracker.HasBandwidthForBill（:376-398）——其参数强类型 Bill_Mech，
        // 我方非其子类不可传 this，按原版公式：已占用+原版活跃 mech 单累计+本单成本 ≤ 总带宽
        // （三个 tracker 成员均 public：UsedBandwidthFromSubjects:55/ActiveMechBills:75/TotalBandwidth:71；
        // 成本口径=Bill_ProductionMech.BandwidthCost 同源 GetStatValueAbstract）。
        // 偏离报备：我方多张 b 单并存时各单独立判定（原版活跃单互算——我方单不入 ActiveMechBills 清单）
        private static bool HasBandwidthFor(Pawn worker, RecipeDef recipe)
        {
            if (worker?.mechanitor == null)
            {
                return true;   // 无 mechanitor 体系（非 Biotech/非机械师）不拦——上游已有机械师门槛
            }
            float cost = recipe.ProducedThingDef.GetStatValueAbstract(StatDefOf.BandwidthCost);
            float used = worker.mechanitor.UsedBandwidthFromSubjects;
            List<Bill_Mech> activeBills = worker.mechanitor.ActiveMechBills;
            for (int i = 0; i < activeBills.Count; i++)
            {
                used += activeBills[i].BandwidthCost;
            }
            return used + cost <= worker.mechanitor.TotalBandwidth;
        }

        // 母本 Bill_Mech.ShouldDoNow（:92-99）同构：绑定者带宽不足→拒做（bill 不进工作分配）
        public override bool ShouldDoNow()
        {
            if (ModsConfig.BiotechActive && BindOperatorEnabled && boundPawn != null
                && !HasBandwidthFor(boundPawn, recipe))
            {
                JobFailReason.Is("CelesIM_Keyed_NotEnoughBandwidth".Translate());
                return false;
            }
            return base.ShouldDoNow();
        }

        // 母本 :103-115 同构（剥 mechanitor 检查——由 mechanitorOnlyRecipe 经 base 链 Bill.cs:266
        // 原版代码免费提供「非机械师」拦截+灰显，原版键内嵌不可自建键化）：
        // 绑定≠p → 灰显带名；候选者带宽不足 → 灰显（JobFailReason 链免费）
        public override bool PawnAllowedToStartAnew(Pawn p)
        {
            if (!base.PawnAllowedToStartAnew(p))
            {
                return false;
            }
            if (BindOperatorEnabled && boundPawn != null && boundPawn != p)
            {
                JobFailReason.Is("CelesIM_Keyed_AlreadyAssigned".Translate(boundPawn.LabelShort));
                return false;
            }
            if (ModsConfig.BiotechActive && BindOperatorEnabled && !HasBandwidthFor(p, recipe))
            {
                JobFailReason.Is("CelesIM_Keyed_NotEnoughBandwidth".Translate());
                return false;
            }
            return true;
        }

        // 母本 :139-143 同款：DEV 全周期一步完成（Building GetGizmos 消费——cyclesDone=总周期+
        // formingTicks=0 → 下一 BillTick 即完成；单周期配方效果=立即 Formed）
        public void ForceCompleteAllCycles()
        {
            cyclesDone = recipe.gestationCycles;
            formingTicks = 0f;
        }

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
        // U2 增补：连绑定位一并清（母本 Reset boundPawn=null 同款——新一轮重新绑首个操作者）
        public override void Reset()
        {
            base.Reset();
            cyclesDone = 0;
            boundPawn = null;
        }

        // ═══ U2 修订（D32）：按母本 Bill_ProductionMech.AppendInspectionData（:29-51）结构重构 ═══
        // 原版结构=状态行在前（产物名行每态有）+周期行殿后（Bill_Mech :170-177 base 位）——
        // U2 首版把提示行插在周期行后+漏产物名行（用户验证捕获，根因=抄改母本无逐行清单）。
        // 行结构（全自建键+D27 锻造术语）：
        //   Gathering：装料计数（基类，共用不变）
        //   Preparing（仅多周期 b 配方可达）：「正在锻造{产物}」+绑定提示行+周期两行
        //   Forming：b 配方加「正在锻造{产物}」行（a 保持现状=零回归，该行原版为 mech 配方专属）+周期两行
        //   Formed：b 绑定有效=「已锻造{产物}（需要{绑定者}来完成）」（原版 GestatedMech :44-47 同构），
        //           否则 Finished（a/未绑定）
        public override void AppendInspectionData(StringBuilder sb)
        {
            if (State == FormingState.Gathering)
            {
                AppendCurrentIngredientCount(sb);
            }
            else if (State == FormingState.Preparing)
            {
                sb.AppendLine("CelesIM_Keyed_Forging".Translate(recipe.ProducedThingDef.LabelCap));
                if (BindOperatorEnabled && boundPawn != null)
                {
                    sb.AppendLine("CelesIM_Keyed_NeedsOperatorToContinue".Translate(boundPawn.LabelShort));
                }
                else
                {
                    sb.AppendLine("CelesIM_Keyed_NeedsOperationToContinue".Translate());
                }
                AppendForgeCycles(sb);
            }
            else if (State == FormingState.Forming)
            {
                if (ProducesPawn(recipe))
                {
                    sb.AppendLine("CelesIM_Keyed_Forging".Translate(recipe.ProducedThingDef.LabelCap));
                }
                AppendForgeCycles(sb);
            }
            else if (State == FormingState.Formed)
            {
                if (BindOperatorEnabled && boundPawn != null)
                {
                    sb.AppendLine("CelesIM_Keyed_ForgedMech".Translate(
                        recipe.ProducedThingDef.LabelCap, boundPawn.LabelShort));
                }
                else
                {
                    sb.AppendLine("Finished".Translate());
                }
            }
        }

        // 周期两行（原 Forming||Preparing 分支体提取——时间串预格式化，星铃规范一；D27 锻造术语；
        // 周期总数 Max(1,·)：cycles=0 显示「共 1」单周期语义）
        private void AppendForgeCycles(StringBuilder sb)
        {
            sb.AppendLine("CelesIM_Keyed_CurrentForgeCycle".Translate(
                ((int)(formingTicks * (1f / WorkSpeedMultiplier))).ToStringTicksToPeriod()));
            int total = Mathf.Max(1, recipe.gestationCycles);
            sb.AppendLine("CelesIM_Keyed_RemainingForgeCycles".Translate(total - cyclesDone, total));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref cyclesDone, "cyclesDone", 0);
            Scribe_References.Look(ref boundPawn, "boundPawn");   // 母本 :182 同款
        }
    }

    // ═══ U2 增补：Pawn 产物配方行为开关（2026-10-02，D31）═══
    // 独立于视觉 ext（FormingGraphicExt）——视觉/行为分域不混载；last-wins 解析同模式。
    // 当前单字段：操作者绑定全链总开关（挂单弹窗/绑定/拒绝灰显/死亡冻结）
    public class CelesIM_PawnProductExt : DefModExtension
    {
        public bool bindOperator = false;
    }
}
