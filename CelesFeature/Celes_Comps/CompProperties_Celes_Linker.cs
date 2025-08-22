using System.Collections.Generic;
using Verse;

namespace CelesFeature
{
    public class CompProperties_Celes_Linker : CompProperties
    {
        public CompProperties_Celes_Linker()
        {
            this.compClass = typeof(Comp_Celes_Linker);
        }

        public List<ThingDef> thingsToLink;
        public float maxLinkableDistance = 8f;
        public int maxLinkableCount = 4;
    }
}
