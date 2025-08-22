using RimWorld;
using System.Collections.Generic;
using Verse.AI;
using Verse;

namespace CelesFeature
{
    public class WorkGiver_Celes_LoadTransformer : WorkGiver_Scanner
    {
        public JobDef Celes_Job_LoadTransformer => DefDatabase<JobDef>.GetNamed("Celes_Job_LoadTransformer");

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (thing.Faction != pawn.Faction)
            {
                return false;
            }

            if (!(thing is Building_Celes_Transformer transformer))
            {
                return false;
            }

            if (transformer.IsBurning())
            {
                return false;
            }

            if (transformer.Map.designationManager.DesignationOn(transformer, DesignationDefOf.Deconstruct) != null)
            {
                return false;
            }

            if (!transformer.NeedToBeLoaded())
            {
                return false;
            }

            if (!pawn.CanReserveAndReach(thing, PathEndMode.Touch, pawn.NormalMaxDanger(), 1, -1, null, forced))
            {
                return false;
            }

            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return this.LoadJob(pawn, t, forced);
        }

        public override bool ShouldSkip(Pawn pawn, bool forced)
        {
            return transformersNeedToBeLoaded(pawn.Map).Count < 1;
        }

        public List<Building_Celes_Transformer> transformersNeedToBeLoaded(Map map)
        {
            List<Building_Celes_Transformer> thingList = new List<Building_Celes_Transformer>();
            foreach (Thing thing in map.spawnedThings)
            {
                if (thing is Building_Celes_Transformer transformer)
                {
                    if (transformer.NeedToBeLoaded())
                    {
                        thingList.Add(transformer);
                    }
                }
            }
            return thingList;
        }

        private Job LoadJob(Pawn pawn, Thing t, bool forced = false)
        {
            Thing thing = FindLoad(pawn, t);
            return JobMaker.MakeJob(Celes_DefOf_Job.Celes_Job_LoadTransformer, t, thing);
        }
        private static Thing FindLoad(Pawn pawn, Thing thing)
        {
            if (thing is Building_Celes_Transformer transformer)
            {
                if (transformer.chosenTransformingMode == 0)
                {
                    return GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForGroup(ThingRequestGroup.Everything), PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f, Validator);
                    bool Validator(Thing x)
                    {
                        if (x.IsForbidden(pawn) || !pawn.CanReserve(x))
                        {
                            return false;
                        }

                        if (!x.def.thingCategories.Contains(ThingCategoryDefOf.StoneChunks))
                        {
                            return false;
                        }

                        return true;
                    }
                }
                if (transformer.chosenTransformingMode == 1)
                {
                    return transformer.wantedPawn;
                }
            }
            return null;
        }
    }
}
