using Verse;

namespace CelesFeature
{
    public class Celes_HediffCompProperties_CapacityCheck : HediffCompProperties
    {

        public HediffDef targetHediff;
        public PawnCapacityDef CapacityToCheck;
        public float SeverityToCheck = 0.33f;
        public int CheckTick = 60;
        
        public Celes_HediffCompProperties_CapacityCheck()
        {
            compClass = typeof(Celes_HediffComp_CapacityCheck);
        }
    
    }

}