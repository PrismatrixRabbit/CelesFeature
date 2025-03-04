using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace CelesFeature
{
	[StaticConstructorOnStartup]
	public static class PatchMain
	{
		static PatchMain()
		{
			Harmony harmony = new Harmony("Celes_Patch");
			harmony.PatchAll(Assembly.GetExecutingAssembly());
			
		}
	}
}
