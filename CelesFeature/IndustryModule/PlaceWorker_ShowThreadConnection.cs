using System.Collections.Generic;
using Verse;
using RimWorld;
using UnityEngine;

namespace CelesFeature
{
    public class PlaceWorker_ShowThreadConnection : PlaceWorker
    {
        public static readonly Color DimRingColor = new Color(0.4f, 0.55f, 0.85f, 0.35f);

        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot,
            Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            ThingDef thingDef = checkingDef as ThingDef;
            if (thingDef == null)
                return true;

            CompProperties_ThreadProducer producerProps = thingDef.GetCompProperties<CompProperties_ThreadProducer>();
            if (producerProps != null)
            {
                GenDraw.DrawRadiusRing(loc, producerProps.connectRadius);
                return true;
            }

            CompProperties_ThreadConsumer consumerProps = thingDef.GetCompProperties<CompProperties_ThreadConsumer>();
            if (consumerProps != null)
            {
                DrawConsumerPreview(consumerProps, loc, rot, thingDef, map);
                return true;
            }

            CompProperties_ThreadRelay relayProps = thingDef.GetCompProperties<CompProperties_ThreadRelay>();
            if (relayProps != null)
            {
                DrawRelayPreview(relayProps, loc, thingDef, map);
                return true;
            }

            return true;
        }

        private static void DrawConsumerPreview(CompProperties_ThreadConsumer props, IntVec3 loc, Rot4 rot,
            ThingDef def, Map map)
        {
            Vector3 myCenter = GenThing.TrueCenter(loc, rot, def.size, def.Altitude);
            List<Building> allBuildings = map.listerBuildings.allBuildingsColonist;

            List<(Building building, int priority, bool inRange, float distSq, bool canAccept, float ringRadius)>
                items = new List<(Building, int, bool, float, bool, float)>();

            for (int i = 0; i < allBuildings.Count; i++)
            {
                Building building = allBuildings[i];
                CompThreadProducer producer = building.TryGetComp<CompThreadProducer>();
                if (producer != null)
                {
                    bool inRange = loc.InHorDistOf(building.Position, producer.Props.connectRadius);
                    bool canAccept = inRange && producer.IsOnline
                        && producer.CurrentLoad + props.baseThreadsCost <= producer.TotalCapacity;
                    float distSq = loc.DistanceToSquared(building.Position);
                    items.Add((building, 0, inRange, distSq, canAccept, producer.Props.connectRadius));
                }

                CompThreadRelay relay = building.TryGetComp<CompThreadRelay>();
                if (relay != null)
                {
                    bool inRange = loc.InHorDistOf(building.Position, relay.Props.connectRadius);
                    bool canAccept = inRange && relay.IsOnline
                        && relay.ConnectionCount < relay.TotalCapacity;
                    float distSq = loc.DistanceToSquared(building.Position);
                    items.Add((building, 1, inRange, distSq, canAccept, relay.Props.connectRadius));
                }
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
                    GenDraw.DrawRadiusRing(items[i].building.Position, items[i].ringRadius);
                else
                    GenDraw.DrawRadiusRing(items[i].building.Position, items[i].ringRadius, DimRingColor);
            }

            // Line to best (if any in range)
            if (bestIndex >= 0)
            {
                var best = items[bestIndex];
                if (best.canAccept)
                    GenDraw.DrawLineBetween(myCenter, best.building.TrueCenter());
                else
                    GenDraw.DrawLineBetween(myCenter, best.building.TrueCenter(),
                        CompAffectedByFacilities.InactiveFacilityLineMat);
            }
        }

        private static void DrawRelayPreview(CompProperties_ThreadRelay props, IntVec3 loc,
            ThingDef def, Map map)
        {
            Vector3 myCenter = GenThing.TrueCenter(loc, Rot4.North, def.size, def.Altitude);
            List<Building> allBuildings = map.listerBuildings.allBuildingsColonist;

            GenDraw.DrawRadiusRing(loc, props.connectRadius);

            List<(Building building, int priority, bool inRange, float distSq, bool canAccept, float ringRadius)>
                items = new List<(Building, int, bool, float, bool, float)>();
            float selfRadius = props.connectRadius;

            for (int i = 0; i < allBuildings.Count; i++)
            {
                Building building = allBuildings[i];
                CompThreadProducer producer = building.TryGetComp<CompThreadProducer>();
                if (producer != null)
                {
                    float maxRadius = Mathf.Max(selfRadius, producer.Props.connectRadius);
                    bool inRange = loc.InHorDistOf(building.Position, maxRadius);
                    bool canAccept = inRange && producer.IsOnline;
                    float distSq = loc.DistanceToSquared(building.Position);
                    items.Add((building, 0, inRange, distSq, canAccept, producer.Props.connectRadius));
                }

                CompThreadRelay relay = building.TryGetComp<CompThreadRelay>();
                if (relay != null)
                {
                    float maxRadius = Mathf.Max(selfRadius, relay.Props.connectRadius);
                    bool inRange = loc.InHorDistOf(building.Position, maxRadius);
                    bool canAccept = inRange && relay.IsOnline;
                    float distSq = loc.DistanceToSquared(building.Position);
                    items.Add((building, 1, inRange, distSq, canAccept, relay.Props.connectRadius));
                }
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
                    GenDraw.DrawRadiusRing(items[i].building.Position, items[i].ringRadius);
                else
                    GenDraw.DrawRadiusRing(items[i].building.Position, items[i].ringRadius, DimRingColor);
            }

            if (bestIndex >= 0)
            {
                var best = items[bestIndex];
                if (best.canAccept)
                    GenDraw.DrawLineBetween(myCenter, best.building.TrueCenter());
                else
                    GenDraw.DrawLineBetween(myCenter, best.building.TrueCenter(),
                        CompAffectedByFacilities.InactiveFacilityLineMat);
            }
        }
    }
}
