using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace CelesFeature
{
    public class CelesFD_JobDriver_UseAntenna : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.InteractionCell)
                .FailOn((Toil to) => !((CelesFD_Building_Antenna)to.actor.jobs.curJob.GetTarget(TargetIndex.A).Thing).CanUseCommsNow);
            Toil openComms = ToilMaker.MakeToil("CelesFD_OpenComms");
            openComms.initAction = delegate
            {
                Pawn actor = openComms.actor;
                CelesFD_Building_Antenna antenna = (CelesFD_Building_Antenna)actor.jobs.curJob.GetTarget(TargetIndex.A).Thing;
                if (antenna.CanUseCommsNow)
                {
                    Find.WindowStack.Add(new CelesFD_Dialog_Comms());   // ← 集成点
                }
            };
            yield return openComms;
        }
    }
}