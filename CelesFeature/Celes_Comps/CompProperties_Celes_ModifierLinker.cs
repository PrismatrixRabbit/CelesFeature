using System.Collections.Generic;
using Verse;

namespace CelesFeature
{
    public class CompProperties_Celes_ModifierLinker : CompProperties
    {
        public CompProperties_Celes_ModifierLinker()
        {
            this.compClass = typeof(Comp_Celes_ModifierLinker);
        }

        public int maxLinkableCount = 1;
        public float extraLinkableDistance = 0f;
        public int extraLinkableCount = 0;
        public float extraCoreEfficiency = 0f;
        public float extraTerminalEfficiency = 0f;
    }
}
