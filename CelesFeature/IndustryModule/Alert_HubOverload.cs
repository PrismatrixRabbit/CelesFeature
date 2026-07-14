using System.Collections.Generic;
using Verse;
using RimWorld;

namespace CelesFeature
{
    public class Alert_HubOverload : Alert
    {
        private CompThreadProducer target;

        public Alert_HubOverload()
        {
            defaultLabel = "中枢超载";
            defaultExplanation = "星铃编译中枢的线程负载超过其产出上限，正在超载运行。若不及时处理，中枢将因超载而故障损坏，需要消耗零部件维修。";
            defaultPriority = AlertPriority.High;
        }

        public override string GetLabel()
        {
            if (target == null)
                return defaultLabel;
            if (target.parent.IsBrokenDown())
                return "中枢超载故障";
            if (!target.IsOnline && target.CurrentLoad > target.TotalCapacity)
                return "中枢超载：继续运行将故障";
            return "中枢超载: " + (target.OverloadTimer / 60) + "秒后故障";
        }

        public override AlertReport GetReport()
        {
            target = null;
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                List<Building> buildings = maps[i].listerBuildings.allBuildingsColonist;
                for (int j = 0; j < buildings.Count; j++)
                {
                    CompThreadProducer p = buildings[j].TryGetComp<CompThreadProducer>();
                    if (p == null || p.IsShutdownByInterference)
                        continue;

                    if (p.parent.IsBrokenDown() && p.IsBrokenDownByOverload)
                    {
                        target = p;
                        return AlertReport.CulpritIs(p.parent);
                    }

                    if (p.CurrentLoad > p.TotalCapacity && !p.parent.IsBrokenDown())
                    {
                        target = p;
                        return AlertReport.CulpritIs(p.parent);
                    }
                }
            }
            return false;
        }
    }
}