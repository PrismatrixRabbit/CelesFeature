using System.Collections.Generic;
using Verse;
using RimWorld;

namespace CelesFeature
{
    public class CelesIM_Alert_HubInterference : Alert
    {
        public CelesIM_Alert_HubInterference()
        {
            defaultLabel = "CelesIM_Keyed_AlertInterferenceLabel".Translate();
            defaultExplanation = "CelesIM_Keyed_AlertInterferenceDesc".Translate();
            defaultPriority = AlertPriority.Medium;
        }

        public override AlertReport GetReport()
        {
            List<Thing> culprits = new List<Thing>();
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                // G 批：数据源 = 管理器注册表——判定语义不变
                List<CelesIM_CompThreadProducer> hubs = CelesIM_ThreadNetworkManager.For(maps[i]).HubsForReading;
                for (int j = 0; j < hubs.Count; j++)
                {
                    CelesIM_CompThreadProducer producer = hubs[j];
                    if (producer != null && producer.parent != null && producer.IsShutdownByInterference)
                        culprits.Add(producer.parent);
                }
            }
            if (culprits.Count > 1)
                return AlertReport.CulpritsAre(culprits);   // 多目标
            if (culprits.Count == 1)
                return AlertReport.CulpritIs(culprits[0]);   // 单目标
            return false;
        }
    }
}