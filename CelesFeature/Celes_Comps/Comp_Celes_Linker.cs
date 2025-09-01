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

        public List<Comp_Celes_ModifierLinker> ModifierList
        {
            get
            {
                List<Comp_Celes_ModifierLinker> list = new List<Comp_Celes_ModifierLinker>();
                foreach (Thing thing in linkedThings.FindAll(x => x.TryGetComp<Comp_Celes_ModifierLinker>() != null))
                {
                    list.Add(thing.TryGetComp<Comp_Celes_ModifierLinker>());
                }
                return list;
            }
        }
        public int MaxLinkableCount
        {
            get
            {
                int num = Props.maxLinkableCount;
                if (ModifierList.Count > 0)
                {
                    foreach (Comp_Celes_ModifierLinker comp in ModifierList)
                    {
                        num += comp.Props.extraLinkableCount;
                    }
                }
                return num;
            }
        }
        public float MaxLinkableDistance
        {
            get
            {
                float num = Props.maxLinkableDistance;
                if (ModifierList.Count > 0)
                {
                    foreach (Comp_Celes_ModifierLinker comp in ModifierList)
                    {
                        num += comp.Props.extraLinkableDistance;
                    }
                }
                return num;
            }
        }

        public int LinkedCount
        {
            get
            {
                return linkedThings.FindAll(x => (x.TryGetComp<Comp_Celes_Terminal>() == null) || x.TryGetComp<Comp_Celes_Terminal>().Props.consumeCapacity).Count;
            }
        }
        public bool ReachedMaxLinkableCount
        {
            get
            {
                return LinkedCount >= MaxLinkableCount;
            }
        }
        public bool OverMaxLinkableCount
        {
            get
            {
                return LinkedCount > MaxLinkableCount;
            }
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
            this.LinkThings();
        }
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            this.LinkThings();
        }
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            foreach (Thing thing in linkedThings)
            {
                if (thing is Building_Celes_Terminal terminal1)
                {
                    terminal1.core = null;
                }
                if (thing is Celes_Building_AutomaticIndustry terminal2)
                {
                    terminal2.core = null;
                }
                if (thing.TryGetComp<Comp_Celes_ModifierLinker>() != null && thing.TryGetComp<Comp_Celes_ModifierLinker>().linkedThings.Contains(parent))
                {
                    thing.TryGetComp<Comp_Celes_ModifierLinker>().linkedThings.Remove(parent);
                }
            }
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
            GenDraw.DrawRadiusRing(parent.Position, MaxLinkableDistance);
        }
        public void LinkThings()
        {
            foreach (Thing thing in linkedThings)
            {
                if (!Props.thingsToLink.Any(x => x.thingDef == thing.def))
                {
                    linkedThings.Remove(thing);
                }
                if (thing != null && (thing.DestroyedOrNull() || !thing.Spawned))
                {
                    linkedThings.Remove(thing);
                }
            }
            if (IsActive(parent))
            {
                foreach (SubProperties_Celes_Linker linkable in Props.thingsToLink)
                {
                    foreach (Thing item in parent.Map.listerThings.ThingsOfDef(linkable.thingDef))
                    {
                        if (!TryAddToLinked(linkable, item))
                        {
                            TryRemoveFromLinked(linkable, item);
                        }
                    }
                }
            }
            else
            {
                linkedThings.Clear();
            }
        }

        public bool TryAddToLinked(SubProperties_Celes_Linker linkable, Thing target)
        {
            if (CanBeLinked(linkable, target, parent))
            {
                if (linkable.countLimit < 1 || linkedThings.FindAll(x => x.def == linkable.thingDef).Count < linkable.countLimit)
                {
                    if (!linkedThings.Contains(target))
                    {
                        linkedThings.Add(target);
                    }
                    if (parent is Building_Celes_Core core1 && target is Building_Celes_Terminal terminal1)
                    {
                        terminal1.core = core1;
                    }
                    if (parent is Building_Celes_Core core2 && target is Celes_Building_AutomaticIndustry terminal2)
                    {
                        terminal2.core = core2;
                    }
                    if (target.TryGetComp<Comp_Celes_ModifierLinker>() != null && !target.TryGetComp<Comp_Celes_ModifierLinker>().linkedThings.Contains(parent))
                    {
                        target.TryGetComp<Comp_Celes_ModifierLinker>().linkedThings.Add(parent);
                    }
                    return true;
                }
            }
            return false;
        }
        public void TryRemoveFromLinked(SubProperties_Celes_Linker linkable, Thing target)
        {
            if (!linkedThings.Contains(target))
            {
                if (target is Building_Celes_Terminal terminal1 && terminal1.core == parent)
                {
                    terminal1.core = null;
                }
                if (target is Celes_Building_AutomaticIndustry terminal2 && terminal2.core == parent)
                {
                    terminal2.core = null;
                }
                if (target.TryGetComp<Comp_Celes_ModifierLinker>() != null && target.TryGetComp<Comp_Celes_ModifierLinker>().linkedThings.Contains(parent))
                {
                    target.TryGetComp<Comp_Celes_ModifierLinker>().linkedThings.Remove(parent);
                }
                return;
            }
            if (CanBeRemove(linkable, target, parent))
            {
                linkedThings.Remove(target);
                if (target is Building_Celes_Terminal terminal1 && terminal1.core == parent)
                {
                    terminal1.core = null;
                }
                if (target is Celes_Building_AutomaticIndustry terminal2 && terminal2.core == parent)
                {
                    terminal2.core = null;
                }
                if (target.TryGetComp<Comp_Celes_ModifierLinker>() != null && target.TryGetComp<Comp_Celes_ModifierLinker>().linkedThings.Contains(parent))
                {
                    target.TryGetComp<Comp_Celes_ModifierLinker>().linkedThings.Remove(parent);
                }
            }
        }

        public bool CanBeLinked(SubProperties_Celes_Linker linkable, Thing target, Thing thisThing)
        {
            if (ReachedMaxLinkableCount)
            {
                if (target.TryGetComp<Comp_Celes_Terminal>() != null)
                {
                    if (target.TryGetComp<Comp_Celes_Terminal>().Props.consumeCapacity)
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
            if (linkable.countLimit > 0)
            {
                if (linkedThings.FindAll(x => x.def == linkable.thingDef).Count >= linkable.countLimit)
                {
                    return false;
                }
            }

            Vector3 a = GenThing.TrueCenter(thisThing.Position, thisThing.Rotation, thisThing.def.size, thisThing.def.Altitude);
            Vector3 b = GenThing.TrueCenter(target.Position, target.Rotation, target.def.size, target.def.Altitude);
            float num = Vector3.Distance(a, b);
            if (num > MaxLinkableDistance)
            {
                return false;
            }
            Comp_Celes_ModifierLinker compModifier = target.TryGetComp<Comp_Celes_ModifierLinker>();
            if (compModifier != null && compModifier.Props.maxLinkableCount > 1)
            {
                if (compModifier.linkedThings.Count >= compModifier.Props.maxLinkableCount)
                {
                    return false;
                }
            }
            else
            {
                foreach (Thing otherLinker in thisThing.Map.listerThings.AllThings.FindAll(thing => thing != thisThing && thing.TryGetComp<Comp_Celes_Linker>() != null))
                {
                    if (otherLinker.TryGetComp<Comp_Celes_Linker>().linkedThings.Contains(target))
                    {
                        return false;
                    }
                }
            }
            return true;
        }
        public bool CanBeRemove(SubProperties_Celes_Linker linkable, Thing target, Thing thisThing)
        {
            if (OverMaxLinkableCount)
            {
                if (target.TryGetComp<Comp_Celes_Terminal>() != null)
                {
                    if (target.TryGetComp<Comp_Celes_Terminal>().Props.consumeCapacity)
                    {
                        return true;
                    }
                }
                else
                {
                    return true;
                }
            }
            if (linkable.countLimit > 0)
            {
                if (linkedThings.FindAll(x => x.def == linkable.thingDef).Count > linkable.countLimit)
                {
                    return true;
                }
            }

            Vector3 a = GenThing.TrueCenter(thisThing.Position, thisThing.Rotation, thisThing.def.size, thisThing.def.Altitude);
            Vector3 b = GenThing.TrueCenter(target.Position, target.Rotation, target.def.size, target.def.Altitude);
            float num = Vector3.Distance(a, b);
            if (num > MaxLinkableDistance)
            {
                return true;
            }
            Comp_Celes_ModifierLinker compModifier = target.TryGetComp<Comp_Celes_ModifierLinker>();
            if (compModifier != null && compModifier.Props.maxLinkableCount > 1)
            {
                if (compModifier.linkedThings.Count > compModifier.Props.maxLinkableCount)
                {
                    return true;
                }
            }
            else
            {
                List<Thing> otherLinkers = thisThing.Map.listerThings.AllThings.FindAll(thing => thing != thisThing && thing.TryGetComp<Comp_Celes_Linker>() != null);
                if (otherLinkers.Count > 0)
                {
                    foreach (Thing otherLinker in otherLinkers)
                    {
                        if (otherLinker.TryGetComp<Comp_Celes_Linker>().linkedThings.Contains(target))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
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
