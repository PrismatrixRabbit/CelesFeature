using Verse;

namespace CelesFeature
{
        public class Celes_HediffCompProperties_Jumper : HediffCompProperties
        {
            public HediffDef targetHediff;

            public Celes_HediffCompProperties_Jumper()
            {
                compClass = typeof(Celes_HediffComp_Jumper);
            }
        }
}