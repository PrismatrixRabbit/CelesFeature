using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    public class Building_Celes_DeepDrill : Building_Celes_Terminal
    {
        public Comp_Celes_DeepDrill compDeepDrill;
        public int chosenMode = 0;
        public int chosenProcessingModeProduct = 0;
        public float progress = 0f;
        public float yieldPct = 0f;
        public bool active = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<int>(ref chosenMode, "Celes_SaveKey_int_Building_DeepDrill_chosenMode");
            Scribe_Values.Look<int>(ref chosenProcessingModeProduct, "Celes_SaveKey_int_Building_DeepDrill_chosenProcessingModeProduct");
            Scribe_Values.Look<float>(ref progress, "Celes_SaveKey_float_Building_DeepDrill_progress");
            Scribe_Values.Look<float>(ref yieldPct, "Celes_SaveKey_float_Building_DeepDrill_yieldPct");
            Scribe_Values.Look<bool>(ref active, "Celes_SaveKey_bool_Building_DeepDrill_active");
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            compDeepDrill = GetComp<Comp_Celes_DeepDrill>();
        }

        public bool IsActive()
        {
            if (!compPowerTrader.PowerOn)
            {
                return false;
            }
            if (!active)
            {
                return false;
            }
            return true;
        }

        protected override void Tick()
        {
            base.Tick();
            if (!IsActive())
            {
                return;
            }
            else
            {
                progress += 1f * GetEfficiency();
                yieldPct += 1f * 2 / 10000f;
                this.CheckShouldProduce();
            }
        }
        private void CheckShouldProduce()
        {
            if (chosenMode == 0)
            {
                if (progress > compDeepDrill.Props.drillingWorkAmount)
                {
                    SpawnDrillingModeProduct(yieldPct);
                    progress = 0f;
                    yieldPct = 0f;
                }
            }
            if (chosenMode == 1)
            {
                if (progress > compDeepDrill.Props.processingModeRecipes[chosenProcessingModeProduct].workAmount)
                {
                    SpawnProcessingModeProduct();
                    progress = 0f;
                    yieldPct = 0f;
                }
            }
        }

        public void SpawnDrillingModeProduct(float yieldPct)
        {
            ThingDef resDef;
            int countPresent;
            IntVec3 cell;
            bool nextResource = GetNextResource(out resDef, out countPresent, out cell);
            if (resDef == null)
            {
                return;
            }

            int num = Mathf.Min(countPresent, resDef.deepCountPerPortion);
            if (nextResource)
            {
                this.Map.deepResourceGrid.SetAt(cell, resDef, countPresent - num);
            }

            int stackCount = Mathf.Max(1, GenMath.RoundRandom((float)num * yieldPct));
            Thing thing = ThingMaker.MakeThing(resDef);
            thing.stackCount = stackCount;
            GenPlace.TryPlaceThing(thing, this.InteractionCell, this.Map, ThingPlaceMode.Near, null, (IntVec3 p) => p != this.Position && p != this.InteractionCell);

            if (!nextResource || ValuableResourcesPresent())
            {
                return;
            }

            if (DeepDrillUtility.GetBaseResource(this.Map, this.Position) == null)
            {
                Messages.Message("DeepDrillExhaustedNoFallback".Translate(), this, MessageTypeDefOf.TaskCompletion);
                return;
            }

            Messages.Message("DeepDrillExhausted".Translate(Find.ActiveLanguageWorker.Pluralize(DeepDrillUtility.GetBaseResource(this.Map, this.Position).label)), this, MessageTypeDefOf.TaskCompletion);
            for (int i = 0; i < 21; i++)
            {
                IntVec3 c = cell + GenRadial.RadialPattern[i];
                if (c.InBounds(this.Map))
                {
                    Building_Celes_DeepDrill otherDrill = c.GetThingList(this.Map).First(x => x.GetType() == typeof(Building_Celes_DeepDrill)) as Building_Celes_DeepDrill;
                    if (otherDrill != null && !otherDrill.ValuableResourcesPresent())
                    {
                        CompFlickable flickComp = otherDrill.GetComp<CompFlickable>();
                        flickComp.DoFlick();
                    }
                }
            }
        }
        private bool GetNextResource(out ThingDef resDef, out int countPresent, out IntVec3 cell)
        {
            return DeepDrillUtility.GetNextResource(this.Position, this.Map, out resDef, out countPresent, out cell);
        }
        public bool ValuableResourcesPresent()
        {
            ThingDef resDef;
            int countPresent;
            IntVec3 cell;
            return GetNextResource(out resDef, out countPresent, out cell);
        }
        public void SpawnProcessingModeProduct()
        {
            Thing thing = ThingMaker.MakeThing(compDeepDrill.Props.processingModeRecipes[chosenProcessingModeProduct].product);
            thing.stackCount = compDeepDrill.Props.processingModeRecipes[chosenProcessingModeProduct].count;
            GenPlace.TryPlaceThing(thing, this.InteractionCell, this.Map, ThingPlaceMode.Near, null, (IntVec3 p) => p != this.Position && p != this.InteractionCell);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
            yield return new Command_Action
            {
                defaultLabel = (active ? "Celes_Keyed_Active".Translate() : "Celes_Keyed_Inactive".Translate()),
                icon = (active ? TexCommand.ForbidOff : TexCommand.ForbidOn),
                action = delegate ()
                {
                    if (active)
                    {
                        active = false;
                    }
                    else
                    {
                        active = true;
                    }
                }
            };
            yield return new Command_Action
            {
                defaultLabel = "Celes_Keyed_ChangeMode".Translate() + (chosenMode == 0 ? "Celes_Keyed_DrillingMode".Translate() : "Celes_Keyed_ProcessingMode".Translate()),
                icon = this.def.uiIcon,
                action = delegate ()
                {
                    List<FloatMenuOption> list = new List<FloatMenuOption>();
                    FloatMenuOption drillingMode = new FloatMenuOption("Celes_Keyed_DrillingMode".Translate(), delegate
                    {
                        this.chosenMode = 0;
                    });
                    list.Add(drillingMode);
                    FloatMenuOption transformingMode = new FloatMenuOption("Celes_Keyed_ProcessingMode".Translate(), delegate
                    {
                        this.chosenMode = 1;
                    });
                    list.Add(transformingMode);
                    Find.WindowStack.Add(new FloatMenu(list));
                }
            };
            if (chosenMode == 1)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Celes_Keyed_ChooseProduct".Translate() + compDeepDrill.Props.processingModeRecipes[chosenProcessingModeProduct].product.label.Translate(),
                    icon = ContentFinder<Texture2D>.Get(compDeepDrill.Props.processingModeRecipes[chosenProcessingModeProduct].product.graphicData.texPath, true),
                    action = delegate ()
                    {
                        List<FloatMenuOption> list = new List<FloatMenuOption>();
                        foreach (SubProperties_Celes_DeepDrill_ProcessingMode product in compDeepDrill.Props.processingModeRecipes)
                        {
                            FloatMenuOption productOption = new FloatMenuOption(product.product.label, delegate
                            {
                                this.chosenProcessingModeProduct = compDeepDrill.Props.processingModeRecipes.IndexOf(product);
                                this.progress = 0f;
                            }, MenuOptionPriority.Default, null, null, 29f, (Rect rect) => Widgets.InfoCardButton(rect.x + 5f, rect.y + (rect.height - 24f) / 2f, product.product));
                            list.Add(productOption);
                        }
                        Find.WindowStack.Add(new FloatMenu(list));
                    }
                };
            }
            yield break;
        }
    }
}
