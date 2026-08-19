using System.Collections.Generic;
using Verse;
using RimWorld;

namespace CelesFeature
{
    public class Alert_HubInterference : Alert
    {
        public Alert_HubInterference()
        {
            defaultLabel = "中枢干扰宕机";
            defaultExplanation = "地图上存在多个活跃的星铃编译中枢，其频率重叠会导致互相干扰，可能至少有一个因信号干扰而宕机。";
            defaultPriority = AlertPriority.Medium;
        }

        public override AlertReport GetReport()
        {
            List<Thing> culprits = new List<Thing>();
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                List<Building> buildings = maps[i].listerBuildings.allBuildingsColonist;
                for (int j = 0; j < buildings.Count; j++)
                {
                    CompThreadProducer producer = buildings[j].TryGetComp<CompThreadProducer>();
                    if (producer != null && producer.IsShutdownByInterference)
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