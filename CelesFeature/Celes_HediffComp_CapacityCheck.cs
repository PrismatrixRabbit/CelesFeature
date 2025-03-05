using RimWorld;
using Verse;

namespace CelesFeature
{
    public class Celes_HediffComp_CapacityCheck : HediffComp
    {
    
        public Celes_HediffCompProperties_CapacityCheck Props => (Celes_HediffCompProperties_CapacityCheck)props;
    
        private int ticksSinceLastCheck;
        public Pawn GetPawn => parent.pawn;
        
        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            if (GetPawn == null || !GetPawn.Spawned || GetPawn.Dead || GetPawn.IsMutant || GetPawn.InContainerEnclosed || GetPawn.health == null || GetPawn.health.hediffSet == null)
            {
                return;
            }
            if (++ticksSinceLastCheck >= Props.CheckTick)
            {
                ticksSinceLastCheck = 0;
                CheckCapacityLevel();
            }
        }
        private void CheckCapacityLevel()
        {
            float capacityLevel = Pawn.health.capacities.GetLevel(Props.CapacityToCheck);
        
            if (capacityLevel < Props.SeverityToCheck && !Pawn.health.hediffSet.HasHediff(Props.targetHediff) && Pawn.health.summaryHealth.SummaryHealthPercent<=Props.HPCheck)
            {
                Pawn.health.AddHediff(Props.targetHediff);
                Pawn.health.RemoveHediff(parent);
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref ticksSinceLastCheck, "ticksSinceLastCheck");
        }
    }

}