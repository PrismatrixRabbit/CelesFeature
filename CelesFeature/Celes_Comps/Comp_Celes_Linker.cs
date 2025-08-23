using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    public class Comp_Celes_Linker : ThingComp
    {
        public List<Thing> linkedThings = new List<Thing>();
        private int tickRare = 0;

        public override void PostExposeData()
        {
            Scribe_Collections.Look<Thing>(ref linkedThings, "Celes_SaveKey_List_Comp_Linker_linkedThings", LookMode.Reference);
            Scribe_Values.Look<int>(ref tickRare, "Celes_SaveKey_int_Comp_Linker_tickRare");
        }

        public CompProperties_Celes_Linker Props => (CompProperties_Celes_Linker)props;

        public override void CompTick()
        {
            tickRare++;
            if (tickRare >= 250)
            {
                TickRare();
                tickRare = 0;
            }
        }

        public void TickRare()
        {
            this.LinkThings();
        }
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            this.LinkThings();
        }
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            linkedThings.Clear();
        }
        public override void PostDrawExtraSelectionOverlays()
        {
            for (int i = 0; i < linkedThings.Count; i++)
            {
                if (IsActive(linkedThings[i]))
                {
                    GenDraw.DrawLineBetween(parent.TrueCenter(), linkedThings[i].TrueCenter());
                }
                else
                {
                    GenDraw.DrawLineBetween(parent.TrueCenter(), linkedThings[i].TrueCenter(), MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent, new Color(1f, 0.5f, 0.5f)));
                }
            }
        }
        public void LinkThings()
        {
            linkedThings.Clear();
            if (IsActive(parent))
            {
                foreach (ThingDef linkable in Props.thingsToLink)
                {
                    foreach (Thing item in parent.Map.listerThings.ThingsOfDef(linkable))
                    {
                        if (CanBeLinked(item, this.parent))
                        {
                            linkedThings.Add(item);
                            if (parent is Building_Celes_Core core1 && item is Building_Celes_Terminal terminal1)
                            {
                                terminal1.core = core1;
                            }
                            if (parent is Building_Celes_Core core2 && item is Celes_Building_AutomaticIndustry terminal2)
                            {
                                terminal2.core = core2;
                            }
                        }
                    }
                }
            }
        }

        public bool CanBeLinked(Thing target, Thing thisThing)
        {
            if (linkedThings.Count >= Props.maxLinkableCount)
            {
                return false;
            }
            Vector3 a = GenThing.TrueCenter(thisThing.Position, thisThing.Rotation, thisThing.def.size, thisThing.def.Altitude);
            Vector3 b = GenThing.TrueCenter(target.Position, target.Rotation, target.def.size, target.def.Altitude);
            float num = Vector3.Distance(a, b);
            if (num > Props.maxLinkableDistance)
            {
                return false;
            }
            if (thisThing is Building_Celes_Core)
            {
                foreach (Thing otherCore in thisThing.Map.listerThings.AllThings.FindAll(thing => thing != thisThing && thing is Building_Celes_Core))
                {
                    if (otherCore.TryGetComp<Comp_Celes_Linker>().linkedThings.Contains(target))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool IsActive(Thing thing)
        {
            if (thing is Building_Celes_Core core)
            {
                if (!core.IsActive())
                {
                    return false;
                }
            }
            else
            {
                if (thing.TryGetComp<CompPowerTrader>() != null)
                {
                    if (!thing.TryGetComp<CompPowerTrader>().PowerOn)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
