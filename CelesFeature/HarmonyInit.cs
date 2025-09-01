using HarmonyLib;
using Verse;

namespace CelesFeature
{
    [StaticConstructorOnStartup]
    public static class HarmonyInit
    {
        static HarmonyInit()
        {
            new Harmony("team.acs.celestiarace.sec.assemblies").PatchAll();
        }
    }
}
