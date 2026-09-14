using System.Collections.Generic;
using RimWorld;
using Verse;

namespace CelesFeature
{
    // ═══ R-1 修复3：人员 Alert 槽位池模式（严格复刻武备 SupportIncoming 模式——每单独立行，非合并）═══
    // 设计：预声明 N 个空壳子类（AlertsReadout 构造期自动实例化全部 Alert 叶子子类——零注册）；
    //   订单进入 Incoming/LeavingSoon 时 GameComponent 分配最低空槽，离开时释放
    // 纪律：自定义 Alert 子类必须有无参构造（AlertsReadout Activator.CreateInstance——A2-18 事故根因）

    // ── Incoming 槽位基类 ──
    public abstract class CelesFD_Alert_PersonnelIncomingBase : Alert
    {
        public virtual int SlotIndex => -1;

        protected CelesFD_Alert_PersonnelIncomingBase()
        {
            defaultPriority = AlertPriority.Medium;
        }

        protected CelesFD_PersonnelOrder Order => CelesFD_PersonnelAlertSlots.GetIncoming(SlotIndex);

        public override AlertReport GetReport()
        {
            AlertReport report = default;
            CelesFD_PersonnelOrder o = Order;
            report.active = o != null && o.Def != null && o.phase == CelesFD_PersonnelOrder.Phase.Incoming;
            return report;
        }

        public override string GetLabel()
        {
            CelesFD_PersonnelOrder o = Order;
            if (o == null || o.Def == null) return "CelesFD_Keyed_SupportFallbackLabel".Translate();
            int left = (int)(o.incomingStartTick + o.Def.arrivalDelayTicks - Find.TickManager.TicksGame);
            return "CelesFD_Keyed_PersonnelIncoming".Translate(o.Def.LabelCap,
                CelesFD_Alert_SupportSlotBase.FormatTicks(left));
        }

        public override TaggedString GetExplanation()
        {
            return "CelesFD_Keyed_PersonnelIncomingDesc".Translate();
        }
    }

    // ── Leaving 槽位基类 ──
    public abstract class CelesFD_Alert_PersonnelLeavingBase : Alert
    {
        public virtual int SlotIndex => -1;

        protected CelesFD_Alert_PersonnelLeavingBase()
        {
            defaultPriority = AlertPriority.Medium;
        }

        protected CelesFD_PersonnelOrder Order => CelesFD_PersonnelAlertSlots.GetLeaving(SlotIndex);

        public override AlertReport GetReport()
        {
            AlertReport report = default;
            CelesFD_PersonnelOrder o = Order;
            report.active = o != null && o.Def != null && o.phase == CelesFD_PersonnelOrder.Phase.LeavingSoon;
            return report;
        }

        public override string GetLabel()
        {
            CelesFD_PersonnelOrder o = Order;
            if (o == null || o.Def == null) return "CelesFD_Keyed_SupportFallbackLabel".Translate();
            long leftTicks = o.stayUntilTick - Find.TickManager.TicksGame;
            int hoursLeft = (int)(leftTicks / 2500L) + 1;
            if (hoursLeft < 1) hoursLeft = 1;
            return "CelesFD_Keyed_PersonnelLeaving".Translate(o.Def.LabelCap, hoursLeft.ToStringCached());
        }

        public override TaggedString GetExplanation()
        {
            return "CelesFD_Keyed_PersonnelLeavingDesc".Translate();
        }
    }

    // 4 槽 Incoming 空壳
    public class CelesFD_Alert_PIncoming1 : CelesFD_Alert_PersonnelIncomingBase { public override int SlotIndex => 0; }
    public class CelesFD_Alert_PIncoming2 : CelesFD_Alert_PersonnelIncomingBase { public override int SlotIndex => 1; }
    public class CelesFD_Alert_PIncoming3 : CelesFD_Alert_PersonnelIncomingBase { public override int SlotIndex => 2; }
    public class CelesFD_Alert_PIncoming4 : CelesFD_Alert_PersonnelIncomingBase { public override int SlotIndex => 3; }

    // 4 槽 Leaving 空壳
    public class CelesFD_Alert_PLeaving1 : CelesFD_Alert_PersonnelLeavingBase { public override int SlotIndex => 0; }
    public class CelesFD_Alert_PLeaving2 : CelesFD_Alert_PersonnelLeavingBase { public override int SlotIndex => 1; }
    public class CelesFD_Alert_PLeaving3 : CelesFD_Alert_PersonnelLeavingBase { public override int SlotIndex => 2; }
    public class CelesFD_Alert_PLeaving4 : CelesFD_Alert_PersonnelLeavingBase { public override int SlotIndex => 3; }

    // 槽位注册表（[Unsaved] 运行期——复刻 SupportAlertSlots 模式）
    public static class CelesFD_PersonnelAlertSlots
    {
        public const int SlotCount = 4;
        private static readonly CelesFD_PersonnelOrder[] incomingSlots = new CelesFD_PersonnelOrder[SlotCount];
        private static readonly CelesFD_PersonnelOrder[] leavingSlots = new CelesFD_PersonnelOrder[SlotCount];

        public static CelesFD_PersonnelOrder GetIncoming(int index)
        {
            if (index < 0 || index >= SlotCount) return null;
            CelesFD_PersonnelOrder o = incomingSlots[index];
            if (o != null && (o.phase != CelesFD_PersonnelOrder.Phase.Incoming))
            {
                incomingSlots[index] = null;   // 惰性清理（阶段已过）
                return null;
            }
            return o;
        }

        public static CelesFD_PersonnelOrder GetLeaving(int index)
        {
            if (index < 0 || index >= SlotCount) return null;
            CelesFD_PersonnelOrder o = leavingSlots[index];
            if (o != null && o.phase != CelesFD_PersonnelOrder.Phase.LeavingSoon)
            {
                leavingSlots[index] = null;
                return null;
            }
            return o;
        }

        // 分配（进入阶段时调用——取最低空槽；无空槽不显示）
        public static void AssignIncoming(CelesFD_PersonnelOrder order)
        {
            for (int i = 0; i < SlotCount; i++)
                if (GetIncoming(i) == null) { incomingSlots[i] = order; return; }
        }

        public static void AssignLeaving(CelesFD_PersonnelOrder order)
        {
            for (int i = 0; i < SlotCount; i++)
                if (GetLeaving(i) == null) { leavingSlots[i] = order; return; }
        }

        // 跨存档清理（GameComponent 构造函数调用）
        public static void ClearAll()
        {
            for (int i = 0; i < SlotCount; i++) { incomingSlots[i] = null; leavingSlots[i] = null; }
        }
    }
}
