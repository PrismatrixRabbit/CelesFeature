using RimWorld;
using UnityEngine;
using Verse;

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
            if (compProperties.thingsCanBeCore == null)
            {
                return;
            }

            Vector3 a = GenThing.TrueCenter(myPos, myRot, myDef.size, myDef.Altitude);
            for (int i = 0; i < compProperties.thingsCanBeCore.Count; i++)
            {
                foreach (Thing item in map.listerThings.ThingsOfDef(compProperties.thingsCanBeCore[i]))
                {
                    if (CanPotentiallyLinkTo(item.def, item.Position, item.Rotation, myDef, myPos, myRot))
                    {
                        GenDraw.DrawLineBetween(a, item.TrueCenter());
                    }
                }
            }
        }

        public bool CanPotentiallyLinkTo(ThingDef facilityDef, IntVec3 facilityPos, Rot4 facilityRot, ThingDef myDef, IntVec3 myPos, Rot4 myRot)
        {
            CompProperties_Celes_Linker compProperties = facilityDef.GetCompProperties<CompProperties_Celes_Linker>();

            Vector3 a = GenThing.TrueCenter(myPos, myRot, myDef.size, myDef.Altitude);
            Vector3 b = GenThing.TrueCenter(facilityPos, facilityRot, facilityDef.size, facilityDef.Altitude);
            float num = Vector3.Distance(a, b);
            if (num > compProperties.maxLinkableDistance)
            {
                return false;
            }

            return true;
        }
    }
}
