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
            if (Props.useRace && Pawn.kindDef == Props.specialRace)
            {
                if (!Props.minToJump)
                {
                    MinToJump(Props.hediffForSpRace);
                }
                else if (Props.minToJump)
                {
                    MaxToJump(Props.hediffForSpRace);
                }
                else if (Props.specialRace == null)
                {
                    Log.Error("你特殊种族忘记写啦");
                }
            }
            else
            {
                if (!Props.minToJump)
                {
                    MinToJump(Props.targetHediff);
                }
                else if (Props.minToJump)
                {
                    MaxToJump(Props.targetHediff);
                }
            }
        }

        public void MinToJump(HediffDef TargetHediff)
        {
            rand = Rand.Range(0f, 1f);
            if (rand <= Props.chanceToJump)
            {
                if (this.parent.Severity == this.parent.def.maxSeverity)
                {
                    Pawn.health.RemoveHediff(parent);
                    Pawn.health.AddHediff(TargetHediff);
                }
            }
        }

        public void MaxToJump(HediffDef TargetHediff)
        {
            rand = Rand.Range(0f, 1f);
            if (rand <= Props.chanceToJump)
            {
                if (this.parent.Severity == this.parent.def.minSeverity)
                {
                    Pawn.health.RemoveHediff(parent);
                    Pawn.health.AddHediff(TargetHediff);
                }
            }
        }
    }
}