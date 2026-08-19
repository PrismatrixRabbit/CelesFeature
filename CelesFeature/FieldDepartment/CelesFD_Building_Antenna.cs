using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace CelesFeature
{
    public class CelesFD_Building_Antenna : Building
    {
        private CompPowerTrader powerComp;

        public bool CanUseCommsNow
        {
            get
            {
                if (base.Spawned && base.Map.gameConditionManager.ElectricityDisabled(base.Map))
                    return false;
                if (powerComp != null)
                    return powerComp.PowerOn;
                return true;
            }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            powerComp = GetComp<CompPowerTrader>();
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn myPawn)
        {
            FloatMenuOption failureReason = GetFailureReason(myPawn);
            if (failureReason != null)
            {
                yield return failureReason;
                yield break;
            }
            yield return new FloatMenuOption("CelesFD_Keyed_OpenComms".Translate(), delegate
            {
                Job job = JobMaker.MakeJob(CelesFD_DefOf.CelesFD_UseAntenna, this);
                myPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            });
        }

        private FloatMenuOption GetFailureReason(Pawn myPawn)
        {
            if (!myPawn.CanReach(this, PathEndMode.InteractionCell, Danger.Some))
                return new FloatMenuOption("CannotUseNoPath".Translate(), null);
            if (base.Spawned && base.Map.gameConditionManager.ElectricityDisabled(base.Map))
                return new FloatMenuOption("CannotUseSolarFlare".Translate(), null);
            if (powerComp != null && !powerComp.PowerOn)
                return new FloatMenuOption("CannotUseNoPower".Translate(), null);
            if (!myPawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
                return new FloatMenuOption("CannotUseReason".Translate("IncapableOfCapacity".Translate(PawnCapacityDefOf.Talking.label, myPawn.Named("PAWN"))), null);
            return null;
        }
    }
}