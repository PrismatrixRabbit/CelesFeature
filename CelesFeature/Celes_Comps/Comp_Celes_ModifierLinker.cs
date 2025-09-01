using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    public class Comp_Celes_ModifierLinker : ThingComp
    {
        public List<Thing> linkedThings = new List<Thing>();
        private int tickRare = 0;
        public CompProperties_Celes_ModifierLinker Props => (CompProperties_Celes_ModifierLinker)props;

        public override void PostExposeData()
        {
            Scribe_Collections.Look<Thing>(ref linkedThings, "Celes_SaveKey_List_Comp_ModifierLinker_linkedThings", LookMode.Reference);
            Scribe_Values.Look<int>(ref tickRare, "Celes_SaveKey_int_Comp_ModifierLinker_tickRare");
        }
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            foreach (Thing thing in linkedThings)
            {
                if (thing.TryGetComp<Comp_Celes_Linker>() != null && thing.TryGetComp<Comp_Celes_Linker>().linkedThings.Contains(parent))
                {
                    thing.TryGetComp<Comp_Celes_Linker>().linkedThings.Remove(parent);
                }
            }
            linkedThings.Clear();
        }
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
            if (linkedThings.Count > Props.maxLinkableCount)
            {
                linkedThings.Remove(linkedThings.RandomElement());
            }
            foreach (Thing thing in linkedThings)
            {
                if (thing != null && (thing.DestroyedOrNull() || !thing.Spawned))
                {
                    linkedThings.Remove(thing);
                }
            }
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            foreach (Thing thing in linkedThings)
            {
                GenDraw.DrawLineBetween(parent.TrueCenter(), thing.TrueCenter());
            }
        }
    }
}
