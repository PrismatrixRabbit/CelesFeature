using Verse;
// ReSharper disable CompareOfFloatsByEqualityOperator

namespace CelesFeature
{
    // ReSharper disable once InconsistentNaming
    public class Celes_HediffComp_Jumper : HediffComp
    {
        public Celes_HediffCompProperties_Jumper Props => (Celes_HediffCompProperties_Jumper)props;
        public float rand;
        public override void CompPostTick(ref float severityAdjustment)
        {
            if(!Props.minToJump)
            {
                rand = Rand.Range(0f, 1f);
                if(rand<=Props.chanceToJump)
                {
                    if (this.parent.Severity == this.parent.def.maxSeverity)
                    {
                        Pawn.health.RemoveHediff(parent);
                        Pawn.health.AddHediff(Props.targetHediff);
                    }
                }
            }
            else if (Props.minToJump)
            {
                rand = Rand.Range(0f, 1f);
                if(rand<=Props.chanceToJump)
                {
                    if (this.parent.Severity == this.parent.def.minSeverity)
                    {
                        Pawn.health.RemoveHediff(parent);
                        Pawn.health.AddHediff(Props.targetHediff);
                    }
                }
            }
        }
    }
}