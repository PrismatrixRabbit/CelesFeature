using RimWorld;
using Verse;

namespace CelesFeature
{
        public class Celes_CompProperties_Spawner : HediffCompProperties
        {
            public ThingDef thingToSpawn;

            public int spawnCount = 1;

            public IntRange spawnIntervalRange = new IntRange(100, 100);

            public int spawnMaxAdjacent = -1;

            public bool spawnForbidden;

            public bool requiresPower;

            public bool writeTimeLeftToSpawn;

            public bool showMessageIfOwned;

            public string saveKeysPrefix;

            public bool inheritFaction;

            public Celes_CompProperties_Spawner()
            {
                compClass = typeof(Celes_HediffCompSpawner);
            }
        }

    }