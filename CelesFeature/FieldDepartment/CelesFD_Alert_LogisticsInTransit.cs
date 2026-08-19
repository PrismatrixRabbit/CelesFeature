using RimWorld;
using Verse;

namespace CelesFeature
{
    // M7 运输中警报（用户裁决 2026-08-15：订阅到货提醒的物流单在途时右侧白色 Alert；未订阅单不计入——全无提示）
    // 反射零注册（AlertsReadout AllLeafSubclasses 自动收集，AlertsReadout.cs:56-78 实证）；普通 Alert（白色，非 Critical）
    public class CelesFD_Alert_LogisticsInTransit : Alert
    {
        private int count;

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
            foreach (CelesFD_LogisticsOrder lo in gc.InTransitList)
                if (lo.notifyArrival) count++;   // 仅订阅单计入（未订阅 → 无 Alert）
            report.active = count > 0;
            return report;
        }

        public override string GetLabel()
        {
            return count > 1
                ? "CelesFD_Keyed_AlertInTransitPlural".Translate(count.ToStringCached())
                : "CelesFD_Keyed_AlertInTransit".Translate();
        }

        public override TaggedString GetExplanation()
        {
            return "CelesFD_Keyed_AlertInTransitDesc".Translate(count.ToStringCached());
        }
    }
}
