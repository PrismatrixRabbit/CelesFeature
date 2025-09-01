using RimWorld;
using RimWorld.Planet;
using System.Linq;
using UnityEngine;
using Verse;
using static HarmonyLib.Code;
using static UnityEngine.GraphicsBuffer;

namespace CelesFeature
{
    public class PlaceWorker_Celes_Terminal : PlaceWorker
    {
        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
        {
            Map currentMap = Find.CurrentMap;
            DrawLinesToPotentialThingsToLinkTo(def, center, rot, currentMap);
        }

        public void DrawLinesToPotentialThingsToLinkTo(ThingDef myDef, IntVec3 myPos, Rot4 myRot, Map map)
        {
            CompProperties_Celes_Terminal compProperties = myDef.GetCompProperties<CompProperties_Celes_Terminal>();
            if (compProperties.thingsWillLinkThis == null)
            {
                return;
            }

            Vector3 a = GenThing.TrueCenter(myPos, myRot, myDef.size, myDef.Altitude);
            for (int i = 0; i < compProperties.thingsWillLinkThis.Count; i++)
            {
                foreach (Thing item in map.listerThings.ThingsOfDef(compProperties.thingsWillLinkThis[i]))
                {
                    if (InRange(item, item.Position, item.Rotation, myDef, myPos, myRot))
                    {
                        if (CanPotentiallyLinkTo(item, myDef))
                        {
                            GenDraw.DrawLineBetween(a, item.TrueCenter());
                        }
                        else
                        {
                            GenDraw.DrawLineBetween(a, item.TrueCenter(), MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent, new Color(1f, 0.5f, 0.5f)));
                        }
                    }
                    Comp_Celes_Linker comp = item.TryGetComp<Comp_Celes_Linker>();
                    GenDraw.DrawRadiusRing(item.Position, comp.MaxLinkableDistance);
                }
            }
            CompProperties_Celes_Linker compLinkerProps = myDef.GetCompProperties<CompProperties_Celes_Linker>();
            if (compLinkerProps != null)
            {
                GenDraw.DrawRadiusRing(a.ToIntVec3(), compLinkerProps.maxLinkableDistance);
            }
        }

        public bool CanPotentiallyLinkTo(Thing facility, ThingDef myDef)
        {
            Comp_Celes_Linker compLinker = facility.TryGetComp<Comp_Celes_Linker>();
            if (compLinker.ReachedMaxLinkableCount)
            {
                if (myDef.GetCompProperties<CompProperties_Celes_Terminal>() != null)
                {
                    if (myDef.GetCompProperties<CompProperties_Celes_Terminal>().consumeCapacity)
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
            SubProperties_Celes_Linker linkable = compLinker.Props.thingsToLink.First(x => x.thingDef == myDef);
            if (linkable.countLimit > 0)
            {
                if (compLinker.linkedThings.FindAll(x => x.def == linkable.thingDef).Count >= linkable.countLimit)
                {
                    return false;
                }
            }

            return true;
        }
        public bool InRange(Thing facility, IntVec3 facilityPos, Rot4 facilityRot, ThingDef myDef, IntVec3 myPos, Rot4 myRot)
        {
            Comp_Celes_Linker comp = facility.TryGetComp<Comp_Celes_Linker>();

            Vector3 a = GenThing.TrueCenter(myPos, myRot, myDef.size, myDef.Altitude);
            Vector3 b = GenThing.TrueCenter(facilityPos, facilityRot, facility.def.size, facility.def.Altitude);
            float num = Vector3.Distance(a, b);
            if (num > comp.MaxLinkableDistance)
            {
                return false;
            }

            return true;
        }
    }
}
