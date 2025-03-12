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
            if (Props.specialRace == null && Props.useRace)
            {
                Log.Error("你特殊种族忘记写啦");
                return;
            }
            
            if (Props.useRace && Pawn.def == Props.specialRace)
            { 
                if (Props.minToJump)
                {
                    MinToJump(Props.hediffForSpRace);
                }
                else
                {
                    MaxToJump(Props.hediffForSpRace);
                }
            }
            else
            {
                if (Props.minToJump)
                {
                    MinToJump(Props.targetHediff);
                }
                else
                {
                    MaxToJump(Props.targetHediff);
                }
            }
        }

        private void MinToJump(HediffDef targetHediff)
        {
            rand = Rand.Range(0f, 1f);
            if (rand <= Props.chanceToJump)
            {
                if (this.parent.Severity == this.parent.def.minSeverity)
                {
                    Pawn.health.RemoveHediff(parent);
                    Pawn.health.AddHediff(targetHediff);
                }
            }
        }

        private void MaxToJump(HediffDef targetHediff)
        {
            rand = Rand.Range(0f, 1f);
            if (rand <= Props.chanceToJump)
            {
                if (this.parent.Severity == this.parent.def.maxSeverity)
                {
                    Pawn.health.RemoveHediff(parent);
                    Pawn.health.AddHediff(targetHediff);
                }
            }
        }
    }
}