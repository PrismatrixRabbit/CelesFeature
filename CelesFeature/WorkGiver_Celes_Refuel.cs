using RimWorld;
using Verse.AI;
using Verse;
using System.Collections.Generic;
using System;
using System.Linq;

namespace CelesFeature
{
    public class WorkGiver_Celes_Refuel : WorkGiver_Scanner
    {
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return pawn.Map.spawnedThings.ToList().FindAll(x => x.TryGetComp<Comp_Celes_Refuelable>() != null && x.TryGetComp<Comp_Celes_Refuelable>().ShouldAutoRefuelNow);
        }

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return this.CanRefuel(pawn, t, forced);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return this.RefuelJob(pawn, t, forced, Celes_DefOf_Job.Celes_Job_Refuel);
        }

        public bool CanRefuel(Pawn pawn, Thing t, bool forced = false)
        {
            Comp_Celes_Refuelable compRefuelable = t.TryGetComp<Comp_Celes_Refuelable>();
            if (compRefuelable == null || compRefuelable.parent.Fogged() || compRefuelable.IsFull || (!forced && !compRefuelable.allowAutoRefuel))
            {
                return false;
            }

            if (compRefuelable.FuelPercentOfMax > 0f && !compRefuelable.Props.allowRefuelIfNotEmpty)
            {
                return false;
            }

            if (!forced && !compRefuelable.ShouldAutoRefuelNow)
            {
                return false;
            }

            if (!pawn.CanReserve(t, 1, -1, null, forced))
            {
                return false;
            }

            if (t.Faction != pawn.Faction)
            {
                return false;
            }

            if (t.TryGetComp(out CompInteractable comp) && comp.Props.cooldownPreventsRefuel && comp.OnCooldown)
            {
                JobFailReason.Is(comp.Props.onCooldownString.CapitalizeFirst());
                return false;
            }

            if (FindBestFuel(pawn, t) == null)
            {
                ThingFilter fuelFilter = compRefuelable.Props.fuelFilter;
                JobFailReason.Is("NoFuelToRefuel".Translate(fuelFilter.Summary));
                return false;
            }

            return true;
        }
        private static List<Thing> FindAllFuel(Pawn pawn, Thing refuelable)
        {
            int fuelCountToFullyRefuel = refuelable.TryGetComp<Comp_Celes_Refuelable>().GetFuelCountToFullyRefuel();
            ThingFilter filter = refuelable.TryGetComp<Comp_Celes_Refuelable>().Props.fuelFilter;
            return FindEnoughReservableThings(pawn, refuelable.Position, new IntRange(fuelCountToFullyRefuel, fuelCountToFullyRefuel), (Thing t) => filter.Allows(t));
        }
        private static List<Thing> FindEnoughReservableThings(Pawn pawn, IntVec3 rootCell, IntRange desiredQuantity, Predicate<Thing> validThing)
        {
            Region region2 = rootCell.GetRegion(pawn.Map);
            TraverseParms traverseParams = TraverseParms.For(pawn);
            List<Thing> chosenThings = new List<Thing>();
            int accumulatedQuantity = 0;
            ThingListProcessor(rootCell.GetThingList(region2.Map), region2);
            if (accumulatedQuantity < desiredQuantity.max)
            {
                RegionTraverser.BreadthFirstTraverse(region2, EntryCondition, RegionProcessor, 99999);
            }

            if (accumulatedQuantity >= desiredQuantity.min)
            {
                return chosenThings;
            }

            return null;
            bool EntryCondition(Region from, Region r)
            {
                return r.Allows(traverseParams, isDestination: false);
            }

            bool RegionProcessor(Region r)
            {
                List<Thing> things2 = r.ListerThings.ThingsMatching(ThingRequest.ForGroup(ThingRequestGroup.HaulableEver));
                return ThingListProcessor(things2, r);
            }

            bool ThingListProcessor(List<Thing> things, Region region)
            {
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (Validator(thing) && !chosenThings.Contains(thing) && ReachabilityWithinRegion.ThingFromRegionListerReachable(thing, region, PathEndMode.ClosestTouch, pawn))
                    {
                        chosenThings.Add(thing);
                        accumulatedQuantity += thing.stackCount;
                        if (accumulatedQuantity >= desiredQuantity.max)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            bool Validator(Thing x)
            {
                if (x.Fogged() || x.IsForbidden(pawn) || !pawn.CanReserve(x))
                {
                    return false;
                }

                if (!validThing(x))
                {
                    return false;
                }

                return true;
            }
        }
        private Job RefuelJob(Pawn pawn, Thing t, bool forced = false, JobDef customRefuelJob = null)
        {
            Thing thing = FindBestFuel(pawn, t);
            return JobMaker.MakeJob(customRefuelJob ?? JobDefOf.Refuel, t, thing);
        }
        private static Thing FindBestFuel(Pawn pawn, Thing refuelable)
        {
            ThingFilter filter = refuelable.TryGetComp<Comp_Celes_Refuelable>().Props.fuelFilter;
            return GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, filter.BestThingRequest, PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f, Validator);
            bool Validator(Thing x)
            {
                if (x.IsForbidden(pawn) || !pawn.CanReserve(x))
                {
                    return false;
                }

                if (!filter.Allows(x))
                {
                    return false;
                }

                return true;
            }
        }
    }
}
