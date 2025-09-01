using Verse;

namespace CelesFeature
{
    public class CompProperties_Celes_Maintenance : CompProperties
    {
        public CompProperties_Celes_Maintenance()
        {
            this.compClass = typeof(Comp_Celes_Maintenance);
        }

        public float maintenanceCapacity = 1.2f;
        public int workAmount = 1200;
        public float recoveryAmount = 0.3f;
        public float maintenanceFreeDays = 1f;
        public float decayPerDay = 0.1f;
    }
}
