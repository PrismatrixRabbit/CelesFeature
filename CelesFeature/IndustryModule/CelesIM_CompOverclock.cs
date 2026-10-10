using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // ════════════════════════════════════════════════════════════════
    // 批 6 C：超频（2026-10-03 · D37/D42 终版）——挂终端 def（Celes_TestAutoProducer）
    // 「连接时终端获得 Toggle」：解锁唯一条件=Buff 链接（终端 AffectedByFacilities 链接含
    // 携 unlockOverclock 的超频器且其 CanBeActive）——建了并链接即可超频
    //（建造门已由 D57 移除——原超频科技随批清理；D57 起本 comp 的 Active 亦为配方解锁门）。
    // 修正两点（电力×3.5 已移除 D42-③）全部作用于终端=连接对象，超频器自身零修正（D37）：
    //   效率：CelesIM_StatPart_Overclock 挂 Celes_ThreadLinkEfficiency parts（XML li）——显示免费
    //   线程：Consumer.ThreadCost 属性单点 ×threadCostFactor（D42-④ 方案1 乘数；消费面 7 点实证）
    // 自动关停（CompTick 250t 节流）：终端失电/断连/失解锁 → active=false（不自动重开；
    // 网络超载不触发关停=压力阀语义，报备）。Toggle 模式参照 CompThreadRelay:281-304。
    // 图标 XML 可配（iconPath，默认 ThreadConnect——用户修正⑤；无灯图，状态灯待补图即挂）。
    // ════════════════════════════════════════════════════════════════
    public class CelesIM_CompProperties_Overclock : CompProperties
    {
        public float efficiencyFactor = 2.0f;   // 效率乘数（占位实测）
        public float threadCostFactor = 2.0f;   // 线程占用乘数（占位实测）
        public string iconPath = "Celes/UI/Icons/ThreadConnect";   // Toggle 图标（XML 可配）

        public CelesIM_CompProperties_Overclock()
        {
            compClass = typeof(CelesIM_CompOverclock);
        }
    }

    public class CelesIM_CompOverclock : ThingComp
    {
        private bool active;   // 超频开启（Scribe；自动关停不自动重开）
        [Unsaved] private int tickCounter;
        [Unsaved] private CompPowerTrader powerComp;

        public bool Active => active;

        public CelesIM_CompProperties_Overclock Props => (CelesIM_CompProperties_Overclock)props;

        // 解锁=Buff 链接（唯一条件 D42-①）：只读确认链接零遍历（CompAffectedByFacilities 列表）
        private bool Unlocked
        {
            get
            {
                CompAffectedByFacilities aff = parent?.GetComp<CompAffectedByFacilities>();
                if (aff == null)
                {
                    return false;
                }
                List<Thing> linked = aff.LinkedFacilitiesListForReading;
                for (int i = 0; i < linked.Count; i++)
                {
                    CompFacility fac = linked[i]?.TryGetComp<CompFacility>();
                    if (fac != null && fac.CanBeActive
                        && linked[i].def.GetModExtension<CelesIM_AmplifierExt>()?.unlockOverclock == true)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!Unlocked)
            {
                yield break;   // 未解锁不显示（连接断开 Toggle 消失）
            }
            yield return new Command_Toggle
            {
                defaultLabel = "CelesIM_Keyed_OverclockLabel".Translate(),
                defaultDesc = "CelesIM_Keyed_OverclockDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get(Props.iconPath),
                isActive = () => active,
                toggleAction = delegate
                {
                    active = !active;
                }
            };
        }

        public override void CompTick()
        {
            tickCounter++;
            if (tickCounter % 250 != 0 || !active)
            {
                return;
            }
            powerComp ??= parent.GetComp<CompPowerTrader>();
            if (powerComp != null && !powerComp.PowerOn)
            {
                ShutDown();
                return;
            }
            if (parent.GetComp<CelesIM_CompThreadConsumer>() is CelesIM_CompThreadConsumer tc
                && tc.CannotWorkUnconnected)
            {
                ShutDown();
                return;
            }
            if (!Unlocked)
            {
                ShutDown();   // 失增幅器（断链/超频器失电）→ 自动关停
            }
        }

        private void ShutDown()
        {
            active = false;
        }

        // ThingComp 存档钩子=PostExposeData（ThingComp.cs:27——非 ExposeData，编译实证）
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref active, "overclockActive", false);
        }
    }

    // ════════════════════════════════════════════════════════════════
    // 批 6 C：超频效率乘数 part（挂 Celes_ThreadLinkEfficiency parts，XML li）
    // 读查询物（终端）的 CompOverclock.Active——与 ThreadCost 同旗标同源，两修正永同步。
    // ════════════════════════════════════════════════════════════════
    public class CelesIM_StatPart_Overclock : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            CelesIM_CompOverclock oc = ActiveOverclock(req);
            if (oc == null)
            {
                return;
            }
            val *= oc.Props.efficiencyFactor;
        }

        public override string ExplanationPart(StatRequest req)
        {
            CelesIM_CompOverclock oc = ActiveOverclock(req);
            if (oc == null)
            {
                return null;   // 未超频不占分解行
            }
            return "CelesIM_Keyed_OverclockStatLine".Translate(oc.Props.efficiencyFactor.ToStringPercent());
        }

        private static CelesIM_CompOverclock ActiveOverclock(StatRequest req)
        {
            if (req.Thing == null)
            {
                return null;
            }
            CelesIM_CompOverclock oc = req.Thing.TryGetComp<CelesIM_CompOverclock>();
            return oc != null && oc.Active ? oc : null;
        }
    }
}
