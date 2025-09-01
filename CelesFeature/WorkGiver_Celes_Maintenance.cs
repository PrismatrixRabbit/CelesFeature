using RimWorld;
using System.Collections.Generic;
using Verse.AI;
using Verse;
using System.Linq;

namespace CelesFeature
{
    public class WorkGiver_Celes_Maintenance : WorkGiver_Scanner
    {
        public JobDef Celes_Job_Maintenance => DefDatabase<JobDef>.GetNamed("Celes_Job_Maintenance");

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (thing.Faction != pawn.Faction)
            {
                return false;
            }

            if (thing.IsBurning())
            {
                return false;
            }

            if (thing.Map.designationManager.DesignationOn(thing, DesignationDefOf.Deconstruct) != null)
            {
                return false;
            }

            if (!(thing is ThingWithComps thing2))
            {
                return false;
            }

            Comp_Celes_Maintenance comp = thing2.TryGetComp<Comp_Celes_Maintenance>();
            if (comp == null)
            {
                return false;
            }

            if (!comp.ShouldMaintain())
            {
                return false;
            }

            if (!pawn.CanReserveAndReach(thing, PathEndMode.Touch, pawn.NormalMaxDanger(), 1, -1, null, forced))
            {
                return false;
            }

            return true;
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return pawn.Map.spawnedThings.ToList().FindAll(x => x.TryGetComp<Comp_Celes_Maintenance>() != null && x.TryGetComp<Comp_Celes_Maintenance>().ShouldMaintain());
        }

        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced)
        {
            return new Job(Celes_Job_Maintenance, thing);
        }

        public override bool ShouldSkip(Pawn pawn, bool forced)
        {
            return thingsNeedMaintenance(pawn.Map).Count < 1;
        }

        public List<Thing> thingsNeedMaintenance(Map map)
        {
            List<Thing> thingList = new List<Thing>();
            foreach (Thing thing in map.spawnedThings)
            {
                Comp_Celes_Maintenance comp = thing.TryGetComp<Comp_Celes_Maintenance>();
                if (comp != null)
                {
                    if (comp.ShouldMaintain())
                    {
                        thingList.Add(thing);
                    }
                }
            }
            return thingList;
        }
    }
}
