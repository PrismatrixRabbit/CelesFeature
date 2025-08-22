using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace CelesFeature
{
    public class JobDriver_Celes_LoadTransformer : JobDriver
    {
        private const TargetIndex TransformerInd = TargetIndex.A;

        private const TargetIndex LoadInd = TargetIndex.B;

        public const int LoadingDuration = 240;

        protected Thing TransformerThing => job.GetTarget(TransformerInd).Thing;

        protected Thing Load => job.GetTarget(LoadInd).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (pawn.Reserve(TransformerThing, job, 1, -1, null, errorOnFailed))
            {
                return pawn.Reserve(Load, job, 1, -1, null, errorOnFailed);
            }

            return false;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Building_Celes_Transformer Transformer = TransformerThing as Building_Celes_Transformer;
            this.FailOnDespawnedNullOrForbidden(TransformerInd);
            AddEndCondition(() => Transformer.NeedToBeLoaded() ? JobCondition.Ongoing : JobCondition.Succeeded);
            Toil reserveFuel = Toils_Reserve.Reserve(LoadInd);
            yield return reserveFuel;
            yield return Toils_Goto.GotoThing(LoadInd, PathEndMode.ClosestTouch).FailOnDespawnedNullOrForbidden(LoadInd).FailOnSomeonePhysicallyInteracting(LoadInd);
            yield return Toils_Haul.StartCarryThing(LoadInd, putRemainderInQueue: false, subtractNumTakenFromJobCount: true).FailOnDestroyedNullOrForbidden(LoadInd);
            yield return Toils_Haul.CheckForGetOpportunityDuplicate(reserveFuel, LoadInd, TargetIndex.None, takeFromValidStorage: true);
            yield return Toils_Goto.GotoThing(TransformerInd, PathEndMode.Touch);
            yield return Toils_General.Wait(LoadingDuration).FailOnDestroyedNullOrForbidden(LoadInd).FailOnDestroyedNullOrForbidden(TransformerInd)
                .FailOnCannotTouch(TransformerInd, PathEndMode.Touch)
                .WithProgressBarToilDelay(TransformerInd);
            yield return Loaded(TransformerInd, LoadInd);
        }

        private static Toil Loaded(TargetIndex transformerInd, TargetIndex fuelInd)
        {
            Toil toil = ToilMaker.MakeToil("FinalizeLoading");
            toil.initAction = delegate
            {
                Job curJob = toil.actor.CurJob;
                Thing transformerThing = curJob.GetTarget(transformerInd).Thing;
                Building_Celes_Transformer transformer = transformerThing as Building_Celes_Transformer;
                if (toil.actor.CurJob.placedThings.NullOrEmpty())
                {
                    transformer.innerContainer.TryAddRangeOrTransfer(new List<Thing> { curJob.GetTarget(fuelInd).Thing });
                }
                else
                {
                    transformer.innerContainer.TryAddRangeOrTransfer(toil.actor.CurJob.placedThings.Select((ThingCountClass p) => p.thing).ToList());
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}
