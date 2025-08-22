using RimWorld;
using System.Collections.Generic;
using Verse;

namespace CelesFeature
{
    public class CompProperties_Celes_Transformer : CompProperties
    {
        public CompProperties_Celes_Transformer()
        {
            this.compClass = typeof(Comp_Celes_Transformer);
        }

        public List<SubProperties_Celes_Transformer> transformingModeRecipes;
        public int RockModeMaxCapacity = 5;
        public float maxBodySize = 3.0f;
        public int powerOffEjectTime = 15000;
        public HediffDef transformedHediff;
        public ThoughtDef transformedThought;
    }
    public class SubProperties_Celes_Transformer
    {
        public ThingDef product;
        public float safeModeOutputFactor = 0.2f;
        public int rockModeOutput;
        public int nonHumanoidBioOutput;
        public int humanoidBioOutputPerTime;
    }
}
