using RimWorld;
using Verse;

namespace CelesFeature
{
    // M7 运输中警报 + W-4 双态升级（2026-09-04）
    // 双态：①运输中（阶段 1——仅订阅单计入，"N 单运输中"） ②即将抵达（阶段 2——全部单计入，"货物购买 即将抵达：xx秒"）
    // 反射零注册（AlertsReadout AllLeafSubclasses 自动收集）；普通 Alert（白色）
    public class CelesFD_Alert_LogisticsInTransit : Alert
    {
        private int count;            // 运输中计数（阶段 1，仅订阅）
        private int countdownCount;   // 即将抵达计数（阶段 2，全部单）
        private int countdownSec;     // 最短剩余秒（多单时取最紧）

        public CelesFD_Alert_LogisticsInTransit()
        {
            defaultPriority = AlertPriority.Medium;
        }

        public override AlertReport GetReport()
        {
            AlertReport report = default;
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return report;
            count = 0;
            countdownCount = 0;
            countdownSec = int.MaxValue;
            long now = Find.TickManager.TicksGame;
            foreach (CelesFD_LogisticsOrder lo in gc.InTransitList)
            {
                if (lo.countdownStartTick >= 0)
                {
                    // 阶段 2：即将抵达——全部单计入（告知性提示，不论订阅）
                    countdownCount++;
                    long remainTicks = lo.countdownStartTick + CelesFD_LogisticsOrder.CountdownTicks - now;
                    int sec = (int)(remainTicks / 60);
                    if (sec < countdownSec) countdownSec = sec;
                }
                else if (lo.notifyArrival)
                {
                    // 阶段 1：运输中——仅订阅单计入
                    count++;
                }
            }
            report.active = countdownCount > 0 || count > 0;
            return report;
        }

        public override string GetLabel()
        {
            // 即将抵达优先显示（更紧急）
            if (countdownCount > 0)
            {
                if (countdownSec < 0) countdownSec = 0;
                return "CelesFD_Keyed_LogisticsIncoming".Translate(countdownSec.ToStringCached());
            }
            return count > 1
                ? "CelesFD_Keyed_AlertInTransitPlural".Translate(count.ToStringCached())
                : "CelesFD_Keyed_AlertInTransit".Translate();
        }

        public override TaggedString GetExplanation()
        {
            var sb = new System.Text.StringBuilder();
            if (countdownCount > 0)
                sb.AppendLine("CelesFD_Keyed_LogisticsIncoming".Translate(countdownSec.ToStringCached()));
            if (count > 0)
                sb.AppendLine("CelesFD_Keyed_AlertInTransitDesc".Translate(count.ToStringCached()));
            return sb.ToString().TrimEnd('\n');
        }
    }
}
