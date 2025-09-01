using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    public class PlaceWorker_Celes_Linker : PlaceWorker
    {
        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
        {
            Map currentMap = Find.CurrentMap;
            DrawLinesToPotentialThingsToLinkTo(def, center, rot, currentMap);
        }

        public void DrawLinesToPotentialThingsToLinkTo(ThingDef myDef, IntVec3 myPos, Rot4 myRot, Map map)
        {
            CompProperties_Celes_Linker compProperties = myDef.GetCompProperties<CompProperties_Celes_Linker>();

            Vector3 a = GenThing.TrueCenter(myPos, myRot, myDef.size, myDef.Altitude);
            GenDraw.DrawRadiusRing(a.ToIntVec3(), compProperties.maxLinkableDistance);

            if (compProperties.thingsToLink == null)
            {
                return;
            }

            for (int i = 0; i < compProperties.thingsToLink.Count; i++)
            {
                foreach (Thing item in map.listerThings.ThingsOfDef(compProperties.thingsToLink[i].thingDef))
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
            CompProperties_Celes_Linker compProperties = myDef.GetCompProperties<CompProperties_Celes_Linker>();

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
