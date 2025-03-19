using RimWorld;
using Verse;

namespace CelesFeature
{

    public class Celes_CompProperties_MutiFunctionalShield : CompProperties
    {
        public float energyOnReset = 0.2f;
        public int startingTicksToReset = 3200;

        public string texturePath = "Other/ShieldBubble";
        public float minDrawSize = 1.2f;
        public float maxDrawSize = 1.55f;

        public float energyLossEMPFactor = 0.033f;
        public float energyLossExplosiveFactor = 0.033f;
        public float energyLossDefaultFactor = 0.033f;

        public bool blocksRangedWeapons = false;
        
        public Celes_CompProperties_MutiFunctionalShield()
        {
            compClass = typeof(Celes_Comp_MutiFunctionalShield);
        }
    }
}