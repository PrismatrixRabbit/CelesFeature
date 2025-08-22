using System.Collections.Generic;
using Verse;

namespace CelesFeature
{
    public class CompProperties_Celes_DeepDrill : CompProperties
    {
        public CompProperties_Celes_DeepDrill()
        {
            this.compClass = typeof(Comp_Celes_DeepDrill);
        }

        public int drillingWorkAmount = 5000;
        public List<SubProperties_Celes_DeepDrill_ProcessingMode> processingModeRecipes;
    }
    public class SubProperties_Celes_DeepDrill_ProcessingMode
    {
        public ThingDef product;
        public int count;
        public int workAmount;
    }
}
