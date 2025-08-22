using Verse;

namespace CelesFeature
{
    public class CompProperties_Celes_AutomaticIndustry : CompProperties
    {
        public CompProperties_Celes_AutomaticIndustry()
        {
            this.compClass = typeof(Comp_Celes_AutomaticIndustry);
        }

        public bool autoEjectProducts = false;
    }
}
