using Verse;

namespace CelesFeature
{
    public class CompProperties_Celes_Maintenance : CompProperties
    {
        public CompProperties_Celes_Maintenance()
        {
            this.compClass = typeof(Comp_Celes_Maintenance);
        }

        public int workAmount = 600;
        public float recoveryAmount = 0.3f;
        public float maintenanceFreeDays = 1f;
        public float decayPerDay = 0.1f;
    }
}
