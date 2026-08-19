using RimWorld;
using Verse;

namespace CelesFeature
{
    // 主界面到期警报（v4.3 B2 定稿：原版 Alert 体系——AlertsReadout 构造时 typeof(Alert).AllLeafSubclasses()
    // 反射自动实例化全部 Alert 子类，零注册；AlertsReadout.cs:56-78 实证）
    // 分级（用户裁决 2026-08-15）：剩余 < 1 天 → 红色脉冲（Alert_Critical 派生）；1 天 ≤ 剩余 < 5 天 → 白色提示（本类）
    // 聚合语义：单实例 GetReport 聚合窗口内订单，GetLabel 自拼数量（原版 Alert_ColonistsIdle.cs:51-58 同构）
    // 已知 bug 预防（2026-08-15 归档）：数量传参用 ToStringCached（原版模式）——Translate 裸 int 参数曾致"数字后异常换行"（根因待实证，先按原版模式规避）
    public class CelesFD_Alert_OrderDeadline : Alert
    {
        // 白色提示窗口：1 天 ≤ 剩余 < 5 天（阈值常量可调；60000 ticks = 1 游戏日——2026-08-15 修复：原 5×60000×24 = 120 天单位错误）
        public const long WarnWindowTicks = GenDate.TicksPerDay * 5;

        private int orderCount;   // GetReport 缓存（GetLabel/GetExplanation 复用）

        public CelesFD_Alert_OrderDeadline()
        {
            defaultPriority = AlertPriority.High;   // 白色提示（非 Critical——颜色与 Priority 无关，仅排序）
        }

        public override AlertReport GetReport()
        {
            AlertReport report = default;
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return report;
            orderCount = 0;
            long now = Find.TickManager.TicksGame;
            foreach (CelesFD_Order o in gc.MarketOrders)
            {
                if (o.state != CelesFD_OrderState.Accepted || o.deadlineTick < 0) continue;
                long remain = o.deadlineTick - now;
                if (remain >= GenDate.TicksPerDay && remain < WarnWindowTicks)
                    orderCount++;
            }
            report.active = orderCount > 0;
            return report;
        }

        public override string GetLabel()
        {
            return orderCount > 1
                ? "CelesFD_Keyed_AlertOrderDeadlinePlural".Translate(orderCount.ToStringCached())
                : "CelesFD_Keyed_AlertOrderDeadline".Translate();
        }

        public override TaggedString GetExplanation()
        {
            // 白色提示窗口（1~5 天）专属文案——原误用"不足一天"Desc（2026-08-15 修复）
            return "CelesFD_Keyed_AlertOrderDeadlineWarnDesc".Translate(orderCount.ToStringCached());
        }
    }

    // 红色脉冲（剩余 < 1 天）：Alert_Critical = BGColor 红脉冲背景 + 激活消息（Alert_Critical.cs:14-22 实证——"红色提示"语义）
    public class CelesFD_Alert_OrderDeadlineCritical : Alert_Critical
    {
        private int orderCount;

        public override AlertReport GetReport()
        {
            AlertReport report = default;
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return report;
            orderCount = 0;
            long now = Find.TickManager.TicksGame;
            foreach (CelesFD_Order o in gc.MarketOrders)
            {
                if (o.state != CelesFD_OrderState.Accepted || o.deadlineTick < 0) continue;
                if (o.deadlineTick - now < GenDate.TicksPerDay)   // 剩余 < 1 天
                    orderCount++;
            }
            report.active = orderCount > 0;
            return report;
        }

        public override string GetLabel()
        {
            return orderCount > 1
                ? "CelesFD_Keyed_AlertOrderDeadlinePlural".Translate(orderCount.ToStringCached())
                : "CelesFD_Keyed_AlertOrderDeadline".Translate();
        }

        public override TaggedString GetExplanation()
        {
            return "CelesFD_Keyed_AlertOrderDeadlineDesc".Translate(orderCount.ToStringCached());
        }
    }
}
