using Verse;

namespace CelesFeature
{
        public class Celes_CompProperties_Spawner : CompProperties
        {
            public ThingDef thingToSpawn;

            public int spawnCount = 1;

            public int spawnInterval = 1;

            public int spawnMaxAdjacent = -1;

            public bool spawnForbidden;

            public bool requiresPower;

            public bool writeTimeLeftToSpawn;

            public bool showMessageIfOwned;

            public string saveKeysPrefix;

            public bool inheritFaction;

            public bool needFuel;

            public Celes_CompProperties_Spawner()
            {
                compClass = typeof(Celes_CompSpawner);
            }
        }

    }