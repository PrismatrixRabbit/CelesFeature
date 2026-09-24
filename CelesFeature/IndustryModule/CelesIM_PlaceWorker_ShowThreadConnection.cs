using System.Collections.Generic;
using Verse;
using RimWorld;
using UnityEngine;

namespace CelesFeature
{
    // 蓝图预览 PlaceWorker（I 批 2026-09-20：数据源 = 管理器注册表，替代全图扫描）
    //   范式对齐原版 CompAffectedByFacilities.DrawLinesToPotentialThingsToLinkTo（:393-62：
    //   def+pos 参数 + 索引结构查询 + 判定绘制一体）；判定与绘制算法与旧全图版逐行等价（Q1）
    public class CelesIM_PlaceWorker_ShowThreadConnection : PlaceWorker
    {
        public static readonly Color DimRingColor = new Color(0.4f, 0.55f, 0.85f, 0.35f);

        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot,
            Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            ThingDef thingDef = checkingDef as ThingDef;
            if (thingDef == null)
                return true;

            CelesIM_CompProperties_ThreadProducer producerProps = thingDef.GetCompProperties<CelesIM_CompProperties_ThreadProducer>();
            if (producerProps != null)
            {
                GenDraw.DrawRadiusRing(loc, producerProps.connectRadius);
                return true;
            }

            CelesIM_CompProperties_ThreadConsumer consumerProps = thingDef.GetCompProperties<CelesIM_CompProperties_ThreadConsumer>();
            if (consumerProps != null)
            {
                DrawConsumerPreview(consumerProps, loc, rot, thingDef, map);
                return true;
            }

            CelesIM_CompProperties_ThreadRelay relayProps = thingDef.GetCompProperties<CelesIM_CompProperties_ThreadRelay>();
            if (relayProps != null)
            {
                DrawRelayPreview(relayProps, loc, thingDef, map);
                return true;
            }

            return true;
        }

        private static void DrawConsumerPreview(CelesIM_CompProperties_ThreadConsumer props, IntVec3 loc, Rot4 rot,
            ThingDef def, Map map)
        {
            Vector3 myCenter = GenThing.TrueCenter(loc, rot, def.size, def.Altitude);
            CelesIM_ThreadNetworkManager mgr = CelesIM_ThreadNetworkManager.For(map);

            List<(Thing node, int priority, bool inRange, float distSq, bool canAccept, float ringRadius)>
                items = new List<(Thing, int, bool, float, bool, float)>();

            List<CelesIM_CompThreadProducer> hubs = mgr.HubsForReading;
            for (int i = 0; i < hubs.Count; i++)
            {
                CelesIM_CompThreadProducer producer = hubs[i];
                if (producer == null || producer.parent == null)
                    continue;
                Thing node = producer.parent;
                bool inRange = loc.InHorDistOf(node.Position, producer.Props.connectRadius);
                bool canAccept = inRange && producer.IsOnline
                    && producer.CurrentLoad + props.baseThreadsCost <= producer.TotalCapacity;
                float distSq = loc.DistanceToSquared(node.Position);
                items.Add((node, 0, inRange, distSq, canAccept, producer.Props.connectRadius));
            }

            List<CelesIM_CompThreadRelay> relays = mgr.RelaysForReading;
            for (int i = 0; i < relays.Count; i++)
            {
                CelesIM_CompThreadRelay relay = relays[i];
                if (relay == null || relay.parent == null)
                    continue;
                Thing node = relay.parent;
                bool inRange = loc.InHorDistOf(node.Position, relay.Props.connectRadius);
                // 与旧全图版逐行等价：此处不查 IsAccessible（预览与 TryConnect 的既有差异，等价迁移保留）
                bool canAccept = inRange && relay.IsOnline
                    && relay.ConnectionCount < relay.TotalCapacity;
                float distSq = loc.DistanceToSquared(node.Position);
                items.Add((node, 1, inRange, distSq, canAccept, relay.Props.connectRadius));
            }

            if (items.Count == 0)
                return;

            // Find best: in-range first, sorted by canAccept > priority > distance
            int bestIndex = -1;
            for (int i = 0; i < items.Count; i++)
            {
                if (!items[i].inRange)
                    continue;
                if (bestIndex < 0
                    || (items[i].canAccept && !items[bestIndex].canAccept)
                    || (items[i].canAccept == items[bestIndex].canAccept
                        && items[i].priority < items[bestIndex].priority)
                    || (items[i].canAccept == items[bestIndex].canAccept
                        && items[i].priority == items[bestIndex].priority
                        && items[i].distSq < items[bestIndex].distSq))
                    bestIndex = i;
            }

            // Draw: best = white, all others = dim
            for (int i = 0; i < items.Count; i++)
            {
                if (i == bestIndex)
                    GenDraw.DrawRadiusRing(items[i].node.Position, items[i].ringRadius);
                else
                    GenDraw.DrawRadiusRing(items[i].node.Position, items[i].ringRadius, DimRingColor);
            }

            // Line to best (if any in range)
            if (bestIndex >= 0)
            {
                var best = items[bestIndex];
                if (best.canAccept)
                    GenDraw.DrawLineBetween(myCenter, best.node.TrueCenter());
                else
                    GenDraw.DrawLineBetween(myCenter, best.node.TrueCenter(),
                        CompAffectedByFacilities.InactiveFacilityLineMat);
            }
        }

        private static void DrawRelayPreview(CelesIM_CompProperties_ThreadRelay props, IntVec3 loc,
            ThingDef def, Map map)
        {
            Vector3 myCenter = GenThing.TrueCenter(loc, Rot4.North, def.size, def.Altitude);
            CelesIM_ThreadNetworkManager mgr = CelesIM_ThreadNetworkManager.For(map);

            GenDraw.DrawRadiusRing(loc, props.connectRadius);

            List<(Thing node, int priority, bool inRange, float distSq, bool canAccept, float ringRadius)>
                items = new List<(Thing, int, bool, float, bool, float)>();
            float selfRadius = props.connectRadius;

            List<CelesIM_CompThreadProducer> hubs = mgr.HubsForReading;
            for (int i = 0; i < hubs.Count; i++)
            {
                CelesIM_CompThreadProducer producer = hubs[i];
                if (producer == null || producer.parent == null)
                    continue;
                Thing node = producer.parent;
                float maxRadius = Mathf.Max(selfRadius, producer.Props.connectRadius);
                bool inRange = loc.InHorDistOf(node.Position, maxRadius);
                bool canAccept = inRange && producer.IsOnline;
                float distSq = loc.DistanceToSquared(node.Position);
                items.Add((node, 0, inRange, distSq, canAccept, producer.Props.connectRadius));
            }

            List<CelesIM_CompThreadRelay> relays = mgr.RelaysForReading;
            for (int i = 0; i < relays.Count; i++)
            {
                CelesIM_CompThreadRelay relay = relays[i];
                if (relay == null || relay.parent == null)
                    continue;
                Thing node = relay.parent;
                float maxRadius = Mathf.Max(selfRadius, relay.Props.connectRadius);
                bool inRange = loc.InHorDistOf(node.Position, maxRadius);
                bool canAccept = inRange && relay.IsOnline
                    && relay.ConnectionCount < relay.TotalCapacity;
                float distSq = loc.DistanceToSquared(node.Position);
                items.Add((node, 1, inRange, distSq, canAccept, relay.Props.connectRadius));
            }

            if (items.Count == 0)
                return;

            int bestIndex = -1;
            for (int i = 0; i < items.Count; i++)
            {
                if (!items[i].inRange)
                    continue;
                if (bestIndex < 0
                    || (items[i].canAccept && !items[bestIndex].canAccept)
                    || (items[i].canAccept == items[bestIndex].canAccept
                        && items[i].priority < items[bestIndex].priority)
                    || (items[i].canAccept == items[bestIndex].canAccept
                        && items[i].priority == items[bestIndex].priority
                        && items[i].distSq < items[bestIndex].distSq))
                    bestIndex = i;
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (i == bestIndex)
                    GenDraw.DrawRadiusRing(items[i].node.Position, items[i].ringRadius);
                else
                    GenDraw.DrawRadiusRing(items[i].node.Position, items[i].ringRadius, DimRingColor);
            }

            if (bestIndex >= 0)
            {
                var best = items[bestIndex];
                if (best.canAccept)
                    GenDraw.DrawLineBetween(myCenter, best.node.TrueCenter());
                else
                    GenDraw.DrawLineBetween(myCenter, best.node.TrueCenter(),
                        CompAffectedByFacilities.InactiveFacilityLineMat);
            }
        }
    }
}
