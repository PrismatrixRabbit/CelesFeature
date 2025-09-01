using System.Collections.Generic;
using Verse;

namespace CelesFeature
{
    public class CompProperties_Celes_Terminal : CompProperties
    {
        public CompProperties_Celes_Terminal()
        {
            this.compClass = typeof(Comp_Celes_Terminal);
        }

        public List<ThingDef> thingsWillLinkThis = new List<ThingDef>();
        public bool consumeCapacity = true;
        public float extraPowerConsumptionForCore = 0f;
        public float extraFuelConsumptionForCore = 0f;
        public float extraMaintenanceDecayForCore = 0f;
        public float efficiencyWithoutCore = 1f;
    }
}
