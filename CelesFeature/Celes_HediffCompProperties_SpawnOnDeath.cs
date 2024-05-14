using RimWorld;
using Verse;

namespace CelesFeature
{
    public class Celes_HediffCompProperties_SpawnOnDeath : HediffCompProperties
    {
        public ThingDef spawnThing;

        public bool spawnPawn;

        public ThingDef spawnPawnDef;

        public int spawnCount;

        public Celes_HediffCompProperties_SpawnOnDeath()
        {
            compClass = typeof(Celes_HediffComp_SpawnOnDeath);
        }
    }
}