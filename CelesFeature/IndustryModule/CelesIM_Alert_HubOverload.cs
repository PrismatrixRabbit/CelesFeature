using Verse;
using RimWorld;
using System.Collections.Generic;

namespace CelesFeature
{
    // 中枢超载 Alert（E+F 批：读取改自定义 FaultActive——原版 CompBreakdownable 已禁用；
    // 数据源全图扫描留 G 批改管理器注册表）
    public class CelesIM_Alert_HubOverload : Alert
    {
        private CelesIM_CompThreadProducer target;

        public CelesIM_Alert_HubOverload()
        {
            defaultLabel = "CelesIM_Keyed_AlertOverloadLabel".Translate();
            defaultExplanation = "CelesIM_Keyed_AlertOverloadDesc".Translate();
            defaultPriority = AlertPriority.High;
        }

        public override string GetLabel()
        {
            if (target == null)
                return defaultLabel;
            if (target.FaultActive)
                return "CelesIM_Keyed_AlertOverloadFaultLabel".Translate();
            if (!target.IsOnline && target.CurrentLoad > target.TotalCapacity)
                return "CelesIM_Keyed_AlertOverloadContinueLabel".Translate();
            // 回退式与 Inspect 同款；计时走原版统一格式化器（批 2 修正——裸秒废弃）
            return "CelesIM_Keyed_AlertOverloadCountdownLabel".Translate(
                (target.OverloadTimer > 0 ? target.OverloadTimer : target.Props.overloadCount).ToStringTicksToPeriod());
        }

        public override AlertReport GetReport()
        {
            target = null;
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                // G 批：数据源 = 管理器注册表（O(注册数)，替代全图遍历）——判定语义不变
                List<CelesIM_CompThreadProducer> hubs = CelesIM_ThreadNetworkManager.For(maps[i]).HubsForReading;
                for (int j = 0; j < hubs.Count; j++)
                {
                    CelesIM_CompThreadProducer p = hubs[j];
                    if (p == null || p.parent == null || p.IsShutdownByInterference)
                        continue;

                    if (p.FaultActive || p.CurrentLoad > p.TotalCapacity)
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
