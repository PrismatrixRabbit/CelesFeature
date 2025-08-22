using Verse;

namespace CelesFeature
{
    public class CompProperties_Celes_ConsumptionFactors : CompProperties
    {
        public CompProperties_Celes_ConsumptionFactors()
        {
            this.compClass = typeof(Comp_Celes_ConsumptionFactors);
        }

        public float powerIdleFactor = 1.0f;
        public float fuelIdleFactor = 1.0f;
    }
}
