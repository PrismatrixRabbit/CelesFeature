using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace CelesFeature
{
    public class JobDriver_Celes_Maintenance : JobDriver
    {
        private float maintenanceProgress = 0f;
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref maintenanceProgress, "Celes_SaveKey_float_JobDriver_Maintenance_maintenanceProgress");
        }
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return ReservationUtility.Reserve(this.pawn, this.job.targetA, this.job, 1, -1, null, true, false);
        }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil wait = ToilMaker.MakeToil("MakeNewToils");
            Comp_Celes_Maintenance comp = job.GetTarget(TargetIndex.A).Thing.TryGetComp<Comp_Celes_Maintenance>();
            wait.initAction = delegate
            {
                Pawn actor2 = wait.actor;
                actor2.pather.StopDead();
            };
            wait.tickIntervalAction = delegate (int delta)
            {
                Pawn actor = wait.actor;
                actor.skills.Learn(SkillDefOf.Intellectual, 0.1f * (float)delta);
                maintenanceProgress += actor.GetStatValue(StatDefOf.ResearchSpeed) * (float)delta;
                if (maintenanceProgress >= comp.Props.workAmount)
                {
                    comp.GetMaintenance();
                    actor.jobs.EndCurrentJob(JobCondition.Succeeded);
                }
            };
            wait.FailOnDespawnedOrNull(TargetIndex.A);
            wait.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            wait.AddEndCondition(() => comp.ShouldMaintain() ? JobCondition.Ongoing : JobCondition.Incompletable);
            wait.defaultCompleteMode = ToilCompleteMode.Never;
            wait.WithProgressBar(TargetIndex.A, () => maintenanceProgress / comp.Props.workAmount);
            wait.activeSkill = () => SkillDefOf.Intellectual;
            yield return wait;
        }
    }
}
