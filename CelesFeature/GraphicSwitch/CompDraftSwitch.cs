using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace CelesFeature
{
    public class CompDraftSwitch : ThingComp
    {
        public CompProperties_DraftSwitch Props => this.props as CompProperties_DraftSwitch;
        private Apparel Apparel => this.parent as Apparel;
        public bool cachedDraftState = false;
        public List<Apparel> _ApparelCache = new List<Apparel>();
        private Apparel ApparelExtra;

        private Pawn wearer => Apparel?.Wearer;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref cachedDraftState, "cachedDraftState", false);

            Scribe_Collections.Look(ref _ApparelCache, "apparelCache", LookMode.Deep);

            if (ApparelExtra != null)
            {
                Scribe_References.Look(ref ApparelExtra, "apparelExtra");
            }
        }


        public override void CompTick()
        {
            base.CompTick();
            if (Apparel?.Wearer == null) return;
            var pawn = Apparel.Wearer;
            if (pawn.Drafted != cachedDraftState)
            {
                //此处extra指额外的替换服装
                cachedDraftState = pawn.Drafted;
                if (cachedDraftState)
                {
                    PutOffApparels();
                    Apparel.Wearer.apparel.Lock(Apparel);
                    PutOnApparelExtra();
                }
                else
                {
                    Apparel.Wearer.apparel.Unlock(Apparel);
                    PutOffApparelExtra();
                    PutOnApparels();
                }
            }
        }

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            cachedDraftState = pawn.Drafted;
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            cachedDraftState = false;
            DropAllApparels(pawn.Map, pawn.Position);
        }

        public override void Notify_WearerDied()
        {
            base.Notify_WearerDied();
            cachedDraftState = false;

            Apparel.Wearer.apparel.Unlock(Apparel);
            PutOffApparelExtra();
            PutOnApparels();
        }
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            cachedDraftState = false;
            DropAllApparels(previousMap, parent.Position);
        }


        public void PutOffApparels()
        {
            var wearer = Apparel?.Wearer;
            if (wearer == null) return;

            // 获取需要保留的层列表（如果为空则默认不保留任何层）
            var removeLayers = Props.removeLayers ?? new List<ApparelLayerDef>();

            var toRemove = wearer.apparel.WornApparel
                .Where(a =>
                {
                    if (a == Apparel) return false; // 自己不脱
                    if (a.def.HasModExtension<DefModExtension_TaggedApparel>()) return false; // 特殊标记的不脱
                                                                                              // 如果衣物的任意层不在保留层中，则脱下
                    bool shouldRemove = true;
                    foreach (var layer in a.def.apparel.layers)
                    {
                        if (!removeLayers.Contains(layer))
                        {
                            shouldRemove = false;
                            break;
                        }
                    }
                    return shouldRemove;
                })
                .ToList();

            foreach (var a in toRemove)
            {
                if (!_ApparelCache.Contains(a))
                    _ApparelCache.Add(a);
                wearer.apparel.Remove(a);
            }
        }


        public void PutOffApparelExtra()
        {
            if (ApparelExtra != null && ApparelExtra.Wearer != null)
            {
                var wearer = ApparelExtra.Wearer;
                wearer.apparel.Remove(ApparelExtra);
                ApparelExtra = null;
            }
        }

        public void PutOnApparels()
        {
            if (Apparel?.Wearer == null) return;

            for (int i = _ApparelCache.Count - 1; i >= 0; i--)
            {
                var a = _ApparelCache[i];
                if (a == null)
                {
                    _ApparelCache.RemoveAt(i);
                    continue;
                }

                if (Apparel.Wearer.apparel.CanWearWithoutDroppingAnything(a.def))
                {
                    Apparel.Wearer.apparel.Wear(a);
                }
                else
                {
                    if (!Apparel.Wearer.apparel.TryMoveToInventory(a))
                    {
                        GenDrop.TryDropSpawn(a, Apparel.Wearer.Position, Apparel.Wearer.Map, ThingPlaceMode.Near, out _, null);
                    }
                }
                _ApparelCache.RemoveAt(i);
            }
        }

        public void PutOnApparelExtra()
        {
            if (Props.switchApperal != null)
            {
                var apparel_switch = ThingMaker.MakeThing(this.Props.switchApperal, Apparel.Stuff??Props.defaultStuff??ThingDefOf.Cloth);
                apparel_switch.HitPoints = (int)(((float)Apparel.HitPoints / Apparel.MaxHitPoints) * apparel_switch.MaxHitPoints);
                var quality = apparel_switch.TryGetComp<CompQuality>();
                if (quality != null)
                {
                    quality.SetQuality(Apparel.TryGetComp<CompQuality>()?.Quality ?? QualityCategory.Normal, null);
                }
                ApparelExtra = apparel_switch as Apparel;
                Apparel.Wearer.apparel.Wear(ApparelExtra,true,true);
            }
        }
        public void DropAllApparels(Map map, IntVec3 pos)
        {
            if (map == null || !pos.IsValid) return;

            foreach (var a in _ApparelCache)
            {
                if (a == null) continue;

                if (a.Wearer != null)
                    a.Wearer.apparel.Remove(a);

                if (!GenPlace.TryPlaceThing(a, pos, map, ThingPlaceMode.Near))
                    GenDrop.TryDropSpawn(a, pos, map, ThingPlaceMode.Near, out _, null);
            }
            _ApparelCache.Clear();
        }
    }
}
