using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace CelesFeature
{
    public static class Celes_Utility
    {
        public static bool IsIdle(this Thing thing)
        {
            if (thing is Building_Celes_Core core)
            {
                if (core.compLinker.linkedThings.Count < 1 || core.compLinker.linkedThings.All(x => x.IsIdle()))
                {
                    return true;
                }
            }
            if (thing is Building_Celes_DeepDrill deepDrill)
            {
                if (!deepDrill.IsActive())
                {
                    return true;
                }
            }
            if (thing is Celes_Building_AutomaticIndustry automaticIndustry)
            {
                if (!automaticIndustry.IsActive())
                {
                    return true;
                }
            }
            return false;
        }

        public static List<Building_Celes_Transformer> AllPlayerTransformers
        {
            get
            {
                allPlayerTransformers.Clear();
                if (Current.ProgramState != 0)
                {
                    List<Map> maps = Find.Maps;
                    foreach (Map map in maps)
                    {
                        foreach (Thing thing in map.spawnedThings.ToList().FindAll(x => x.GetType() == typeof(Building_Celes_Transformer)))
                        {
                            if (thing is Building_Celes_Transformer transformer)
                            {
                                if (transformer.Faction != null && transformer.Faction == Faction.OfPlayer)
                                {
                                    allPlayerTransformers.Add(transformer);
                                }
                            }
                        }
                    }
                }

                return allPlayerTransformers;
            }
        }
        private static List<Building_Celes_Transformer> allPlayerTransformers = new List<Building_Celes_Transformer>();
    }

    [RimWorld.DefOf]
    public static class Celes_DefOf_Job
    {
        public static JobDef Celes_Job_Refuel;
        public static JobDef Celes_Job_LoadTransformer;
        public static JobDef Celes_Job_DoAutomaticBill;
    }
    [RimWorld.DefOf]
    public static class Celes_DefOf_Hediff
    {
        public static HediffDef Celes_Hediff_NanoCrystallised;
    }
    [RimWorld.DefOf]
    public static class Celes_DefOf_BodyPart
    {
        public static BodyPartDef Foot;
    }
}
