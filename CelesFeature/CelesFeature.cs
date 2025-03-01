using HarmonyLib;
using Verse;

namespace CelesFeature;

public class CelesFeature : Mod
    {
        public static Harmony harmony;

        public CelesFeature(ModContentPack content) : base(content)
        {
            harmony = new Harmony("team.acs.celestiarace");
            harmony.PatchAll();
        }
    }