using Verse;

namespace CelesFeature
{
    public class CompProperties_Celes_Terminal : CompProperties
    {
        public CompProperties_Celes_Terminal()
        {
            this.compClass = typeof(Comp_Celes_Terminal);
        }

        public float extraPowerConsumptionForCore = 0f;
        public float extraFuelConsumptionForCore = 0f;
        public float extraMaintenanceDecayForCore = 0f;
        public float efficiencyWithoutCore = 1f;
    }
}
