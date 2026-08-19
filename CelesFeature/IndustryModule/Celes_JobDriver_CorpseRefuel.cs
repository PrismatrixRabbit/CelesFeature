/* 尸体投入机制暂时注释（K7 搁置，恢复时移除首尾注释）
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace CelesFeature
{
    // [事实] 简化自 JobDriver_Refuel：Reserve 双方 → 搬运 → Wait(240) → 结算（JobDriver_Refuel.cs:30-50）
    public class Celes_JobDriver_CorpseRefuel : JobDriver
    {
        private const TargetIndex SeederInd = TargetIndex.A;
        private const TargetIndex CorpseInd = TargetIndex.B;

        private Thing Seeder => job.GetTarget(SeederInd).Thing;
        private Corpse CorpseTarget => job.GetTarget(CorpseInd).Thing as Corpse;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Seeder, job, 1, -1, null, errorOnFailed)
                && pawn.Reserve(CorpseTarget, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(SeederInd);
            this.FailOnDespawnedNullOrForbidden(CorpseInd);
            yield return Toils_Goto.GotoThing(CorpseInd, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(CorpseInd)
                .FailOnSomeonePhysicallyInteracting(CorpseInd);
            yield return Toils_Haul.StartCarryThing(CorpseInd);
            yield return Toils_Goto.GotoThing(SeederInd, PathEndMode.Touch);
            yield return Toils_General.Wait(240)
                .FailOnDestroyedNullOrForbidden(CorpseInd)
                .FailOnDestroyedNullOrForbidden(SeederInd)
                .FailOnCannotTouch(SeederInd, PathEndMode.Touch)
                .WithProgressBarToilDelay(SeederInd);
            yield return Toils_General.Do(FinalizeRefueling);
        }

        private void FinalizeRefueling()
        {
            Celes_CompCorpseFuel fuelComp = Seeder.TryGetComp<Celes_CompCorpseFuel>();
            CompRefuelable refuelable = Seeder.TryGetComp<CompRefuelable>();
            float amount = fuelComp != null ? fuelComp.GetCorpseFuelAmount(CorpseTarget) : 0f;
            if (amount > 0f)
                refuelable?.Refuel(amount);
            CorpseTarget?.Destroy();
        }
    }
}
*/
