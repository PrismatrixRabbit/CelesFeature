using Verse;
// ReSharper disable CompareOfFloatsByEqualityOperator

namespace CelesFeature
{
    // ReSharper disable once InconsistentNaming
    public class Celes_HediffComp_Jumper : HediffComp
    {
        public Celes_HediffCompProperties_Jumper Props => (Celes_HediffCompProperties_Jumper)props;
        public override void CompPostTick(ref float severityAdjustment)
        {
            if (this.parent.Severity == this.parent.def.maxSeverity)
            {
                Pawn.health.AddHediff(Props.targetHediff);
                Pawn.health.RemoveHediff(parent);
            }
        }
    }
}