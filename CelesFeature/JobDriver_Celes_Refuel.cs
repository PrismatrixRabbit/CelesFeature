using System.Collections.Generic;
using System.Linq;
using Verse.AI;
using Verse;

namespace CelesFeature
{
    public class JobDriver_Celes_Refuel : JobDriver
    {
        private const TargetIndex RefuelableInd = TargetIndex.A;

        private const TargetIndex FuelInd = TargetIndex.B;

        public const int RefuelingDuration = 240;

        protected Thing Refuelable => job.GetTarget(RefuelableInd).Thing;

        protected Comp_Celes_Refuelable RefuelableComp => Refuelable.TryGetComp<Comp_Celes_Refuelable>();

        protected Thing Fuel => job.GetTarget(FuelInd).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (pawn.Reserve(Refuelable, job, 1, -1, null, errorOnFailed))
            {
                return pawn.Reserve(Fuel, job, 1, -1, null, errorOnFailed);
            }

            return false;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(RefuelableInd);
            AddEndCondition(() => (!RefuelableComp.IsFull) ? JobCondition.Ongoing : JobCondition.Succeeded);
            AddFailCondition(() => !job.playerForced && !RefuelableComp.ShouldAutoRefuelNowIgnoringFuelPct);
            AddFailCondition(() => !RefuelableComp.allowAutoRefuel && !job.playerForced);
            yield return Toils_General.DoAtomic(delegate
            {
                job.count = RefuelableComp.GetFuelCountToFullyRefuel();
            });
            Toil reserveFuel = Toils_Reserve.Reserve(FuelInd);
            yield return reserveFuel;
            yield return Toils_Goto.GotoThing(FuelInd, PathEndMode.ClosestTouch).FailOnDespawnedNullOrForbidden(FuelInd).FailOnSomeonePhysicallyInteracting(FuelInd);
            yield return Toils_Haul.StartCarryThing(FuelInd, putRemainderInQueue: false, subtractNumTakenFromJobCount: true).FailOnDestroyedNullOrForbidden(FuelInd);
            yield return Toils_Haul.CheckForGetOpportunityDuplicate(reserveFuel, FuelInd, TargetIndex.None, takeFromValidStorage: true);
            yield return Toils_Goto.GotoThing(RefuelableInd, PathEndMode.Touch);
            yield return Toils_General.Wait(RefuelingDuration).FailOnDestroyedNullOrForbidden(FuelInd).FailOnDestroyedNullOrForbidden(RefuelableInd)
                .FailOnCannotTouch(RefuelableInd, PathEndMode.Touch)
                .WithProgressBarToilDelay(RefuelableInd);
            yield return Refuel(RefuelableInd, FuelInd);
        }

        private static Toil Refuel(TargetIndex refuelableInd, TargetIndex fuelInd)
        {
            Toil toil = ToilMaker.MakeToil("FinalizeRefueling");
            toil.initAction = delegate
            {
                Job curJob = toil.actor.CurJob;
                Thing thing = curJob.GetTarget(refuelableInd).Thing;
                if (toil.actor.CurJob.placedThings.NullOrEmpty())
                {
                    thing.TryGetComp<Comp_Celes_Refuelable>().Refuel(new List<Thing> { curJob.GetTarget(fuelInd).Thing });
                }
                else
                {
                    thing.TryGetComp<Comp_Celes_Refuelable>().Refuel(toil.actor.CurJob.placedThings.Select((ThingCountClass p) => p.thing).ToList());
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}
