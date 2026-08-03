using HarmonyLib;
using System.Reflection;
using Verse;

namespace CelesFeature
{
	[StaticConstructorOnStartup]
	public static class PatchMain
	{
		static PatchMain()
		{
			Harmony harmony = new Harmony("CelesFeature");
			harmony.PatchAll(Assembly.GetExecutingAssembly());
		}
	}
}
