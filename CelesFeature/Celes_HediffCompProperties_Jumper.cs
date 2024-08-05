using Verse;

namespace CelesFeature
{
        public class Celes_HediffCompProperties_Jumper : HediffCompProperties
        {
            public HediffDef targetHediff;
            public float chanceToJump=1f;
            public bool minToJump=false;
            public Celes_HediffCompProperties_Jumper()
            {
                compClass = typeof(Celes_HediffComp_Jumper);
            }
        }
}