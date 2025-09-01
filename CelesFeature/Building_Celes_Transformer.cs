using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    public class Building_Celes_Transformer : Building_Celes_Terminal
    {
        public ThingOwner<Thing> innerContainer = new ThingOwner<Thing>();
        public Pawn wantedPawn;
        public Comp_Celes_Transformer compTransformer;
        public int chosenTransformingMode = 0;
        public int chosenTransformingModeProduct = 0;
        public int expectedFinishTick = 0;
        public int nextRewardTick = 0;
        public int runningTick = 0;
        public int ejectTick = 0;
        public bool active = false;
        public bool killMode = false;
        public bool autoLoad = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref innerContainer, "SEC_SaveKey_ThingOwner_Building_Transformer_innerContainer", this);
            Scribe_References.Look(ref wantedPawn, "SEC_SaveKey_Pawn_Building_Transformer_wantedPawn");
            Scribe_Values.Look<int>(ref chosenTransformingMode, "Celes_SaveKey_int_Building_Transformer_chosenTransformingMode");
            Scribe_Values.Look<int>(ref chosenTransformingModeProduct, "Celes_SaveKey_int_Building_Transformer_chosenTransformingModeProduct");
            Scribe_Values.Look<int>(ref expectedFinishTick, "Celes_SaveKey_int_Building_Transformer_expectedFinishTick");
            Scribe_Values.Look<int>(ref nextRewardTick, "Celes_SaveKey_int_Building_Transformer_nextRewardTick");
            Scribe_Values.Look<int>(ref runningTick, "Celes_SaveKey_int_Building_Transformer_runningTick");
            Scribe_Values.Look<int>(ref ejectTick, "Celes_SaveKey_int_Building_Transformer_ejectTick");
            Scribe_Values.Look<bool>(ref active, "Celes_SaveKey_bool_Building_Transformer_active");
            Scribe_Values.Look<bool>(ref killMode, "Celes_SaveKey_bool_Building_Transformer_killMode");
            Scribe_Values.Look<bool>(ref autoLoad, "Celes_SaveKey_bool_Building_Transformer_autoLoad");
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            compTransformer = GetComp<Comp_Celes_Transformer>();
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
            if (innerContainer.Count < 1)
            {
                return false;
            }
            return true;
        }

        public void Initialize(int finishTick, bool isHuman)
        {
            expectedFinishTick = finishTick;
            runningTick = 0;
            if (isHuman)
            {
                nextRewardTick = (expectedFinishTick / 3);
            }
            else
            {
                nextRewardTick = 0;
            }
        }

        public bool NeedToBeLoaded()
        {
            if (chosenTransformingMode == 0 && autoLoad)
            {
                if (innerContainer.Count < compTransformer.Props.RockModeMaxCapacity)
                {
                    return true;
                }
            }
            if (chosenTransformingMode == 1 && wantedPawn != null)
            {
                if (innerContainer.Count < 1)
                {
                    return true;
                }
            }    
            return false;
        }
        public bool DangerPowerOff()
        {
            return (chosenTransformingMode == 1 && !compPowerTrader.PowerOn && innerContainer.Count > 0);
        }
        public override void ExtraTickRare()
        {
            base.ExtraTickRare();
            if (DangerPowerOff())
            {
                ejectTick += 250;
                if (ejectTick >= compTransformer.Props.powerOffEjectTime)
                {
                    expectedFinishTick = 0;
                    nextRewardTick = 0;
                    runningTick = 0;
                    if (innerContainer.First() is Pawn pawn)
                    {
                        if (!pawn.RaceProps.Humanlike)
                        {
                            if (killMode)
                            {
                                pawn.Kill(null);
                            }
                        }
                        else
                        {
                            Hediff hediff = HediffMaker.MakeHediff(compTransformer.Props.transformedHediff, pawn);
                            hediff.Severity = 1.0f;
                            pawn.health.AddHediff(hediff);
                            if (pawn.needs != null && pawn.needs.mood != null)
                            {
                                pawn.needs.mood.thoughts.memories.TryGainMemory((Thought_Memory)ThoughtMaker.MakeThought(compTransformer.Props.transformedThought));
                            }
                        }
                    }
                    innerContainer.TryDropAll(this.InteractionCell, this.Map, ThingPlaceMode.Near);
                }
            }
            else
            {
                ejectTick = 0;
            }
            if (IsActive())
            {
                runningTick += (int)(250f * GetEfficiency());
                if (nextRewardTick > 0 && runningTick < expectedFinishTick)
                {
                    if (runningTick >= nextRewardTick)
                    {
                        GetReward();
                    }
                }
                if (runningTick >= expectedFinishTick)
                {
                    GetFinalReward();
                }
            }
        }
        public void GetReward()
        {
            if (runningTick < expectedFinishTick)
            {
                nextRewardTick += (expectedFinishTick / 3);
            }
            SpawnProduct();
            if (chosenTransformingMode == 1)
            {
                if (killMode)
                {
                    if (innerContainer.First() is Pawn pawn && pawn.RaceProps.Humanlike)
                    {
                        List<BodyPartRecord> allBodyParts = pawn.health.hediffSet.GetNotMissingParts().ToList();
                        Hediff hediff = HediffMaker.MakeHediff(Celes_DefOf_Hediff.Celes_Hediff_NanoCrystallised, pawn);
                        if (allBodyParts.Any(x => x.def == BodyPartDefOf.Hand))
                        {
                            foreach (BodyPartRecord hand in allBodyParts.FindAll(x => x.def == BodyPartDefOf.Hand))
                            {
                                pawn.health.AddHediff(hediff, hand);
                            }
                        }
                        else
                        {
                            if (allBodyParts.Any(x => x.def == BodyPartDefOf.Arm))
                            {
                                foreach (BodyPartRecord arm in allBodyParts.FindAll(x => x.def == BodyPartDefOf.Arm))
                                {
                                    pawn.health.AddHediff(hediff, arm);
                                }
                            }
                        }
                        if (allBodyParts.Any(x => x.def == Celes_DefOf_BodyPart.Foot))
                        {
                            foreach (BodyPartRecord foot in allBodyParts.FindAll(x => x.def == Celes_DefOf_BodyPart.Foot))
                            {
                                pawn.health.AddHediff(hediff, foot);
                            }
                        }
                        else
                        {
                            if (allBodyParts.Any(x => x.def == BodyPartDefOf.Leg))
                            {
                                foreach (BodyPartRecord leg in allBodyParts.FindAll(x => x.def == BodyPartDefOf.Leg))
                                {
                                    pawn.health.AddHediff(hediff, leg);
                                }
                            }
                        }
                    }
                }
            }
        }
        public void GetFinalReward()
        {
            expectedFinishTick = 0;
            nextRewardTick = 0;
            runningTick = 0;
            SpawnProduct();
            if (chosenTransformingMode == 0)
            {
                innerContainer.Remove(innerContainer.First());
                if (innerContainer.Count > 0)
                {
                    Initialize(60000, false);
                }
            }
            if (chosenTransformingMode == 1)
            {
                if (killMode)
                {
                    if (innerContainer.First() is Pawn pawn)
                    {
                        if (!pawn.RaceProps.Humanlike)
                        {
                            pawn.Kill(null);
                        }
                        else
                        {
                            BodyPartRecord torso = pawn.health.hediffSet.GetBodyPartRecord(BodyPartDefOf.Torso);
                            if (pawn.health.hediffSet.HasBodyPart(torso))
                            {
                                Hediff hediff = HediffMaker.MakeHediff(Celes_DefOf_Hediff.Celes_Hediff_NanoCrystallised, pawn);
                                pawn.health.AddHediff(hediff, torso);
                            }
                        }
                    }
                    innerContainer.TryDropAll(this.InteractionCell, this.Map, ThingPlaceMode.Near);
                }
                else
                {
                    if (innerContainer.First() is Pawn pawn)
                    {
                        if (pawn.RaceProps.Humanlike)
                        {
                            Initialize(600000, true);
                        }
                        else
                        {
                            Initialize(60000, false);
                        }
                    }
                    else
                    {
                        innerContainer.TryDropAll(this.InteractionCell, this.Map, ThingPlaceMode.Near);
                    }
                }
            }
        }
        public void SpawnProduct()
        {
            SubProperties_Celes_Transformer product = compTransformer.Props.transformingModeRecipes[chosenTransformingModeProduct];
            if (chosenTransformingMode == 0)
            {
                Thing thing = ThingMaker.MakeThing(product.product);
                thing.stackCount = product.rockModeOutput;
                GenPlace.TryPlaceThing(thing, this.InteractionCell, this.Map, ThingPlaceMode.Near, null, (IntVec3 p) => p != this.Position && p != this.InteractionCell);
            }
            if (chosenTransformingMode == 1)
            {
                if (innerContainer.First() is Pawn pawn)
                {
                    if (pawn.RaceProps.Humanlike)
                    {
                        Thing thing = ThingMaker.MakeThing(product.product);
                        thing.stackCount = (int)(killMode ? product.humanoidBioOutputPerTime : product.humanoidBioOutputPerTime * compTransformer.Props.transformingModeRecipes[chosenTransformingModeProduct].safeModeOutputFactor);
                        GenPlace.TryPlaceThing(thing, this.InteractionCell, this.Map, ThingPlaceMode.Near, null, (IntVec3 p) => p != this.Position && p != this.InteractionCell);
                    }
                    else
                    {
                        Thing thing = ThingMaker.MakeThing(product.product);
                        thing.stackCount = (int)(killMode ? product.nonHumanoidBioOutput : product.nonHumanoidBioOutput * compTransformer.Props.transformingModeRecipes[chosenTransformingModeProduct].safeModeOutputFactor);
                        GenPlace.TryPlaceThing(thing, this.InteractionCell, this.Map, ThingPlaceMode.Near, null, (IntVec3 p) => p != this.Position && p != this.InteractionCell);
                    }
                }
            }
        }

        public bool UnlockedKillMode()
        {
            if (compTransformer.Props.killModeTechLock && !compTransformer.Props.techUnlockKillMode.IsFinished)
            {
                return false;
            }
            return true;
        }

        public override string GetInspectString()
        {
            string text = "";
            if (active)
            {
                text += "\n" + "Celes_Keyed_Active".Translate();
            }
            else
            {
                text += "\n" + "Celes_Keyed_Inactive".Translate();
            }
            if (chosenTransformingMode == 0)
            {
                text += "\n" + "Celes_Keyed_RockLoaded".Translate(innerContainer.Count, compTransformer.Props.RockModeMaxCapacity);
            }
            if (chosenTransformingMode == 1)
            {
                if (innerContainer.Count > 0)
                {
                    text += "\n" + "Celes_Keyed_CreatureLoaded".Translate();
                }
                else
                {
                    if (wantedPawn != null)
                    {
                        text += "\n" + "Celes_Keyed_WaitingForCreature".Translate();
                    }
                    else
                    {
                        text += "\n" + "Celes_Keyed_NoChosenCreature".Translate();
                    }
                }
            }
            if (IsActive())
            {
                text += "\n" + "Celes_Keyed_ProcessingTimeLeft".Translate((expectedFinishTick - runningTick).ToStringTicksToPeriod());
            }
            if (DangerPowerOff())
            {
                text += "\n" + "Celes_Keyed_TimeToEject".Translate((compTransformer.Props.powerOffEjectTime - ejectTick).ToStringTicksToPeriod());
            }
            return base.GetInspectString() + text;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
            yield return new Command_Action
            {
                defaultLabel = (chosenTransformingMode == 0 ? "Celes_Keyed_RockMode".Translate() : "Celes_Keyed_BiologicalMode".Translate()),
                icon = (chosenTransformingMode == 0 ? ContentFinder<Texture2D>.Get("Celes/UI/Icons/StoneChunks") : ContentFinder<Texture2D>.Get("UI/Commands/DropCarriedPawn")),
                action = delegate ()
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("Celes_Keyed_ConfirmChangeTransformingMode".Translate(), delegate
                    {
                        if (chosenTransformingMode == 0)
                        {
                            chosenTransformingMode = 1;
                        }
                        else
                        {
                            chosenTransformingMode = 0;
                            if (innerContainer.Count > 0 && innerContainer.First() is Pawn pawn)
                            {
                                if (!pawn.RaceProps.Humanlike)
                                {
                                    if (killMode)
                                    {
                                        pawn.Kill(null);
                                    }
                                }
                                else
                                {
                                    Hediff hediff = HediffMaker.MakeHediff(compTransformer.Props.transformedHediff, pawn);
                                    hediff.Severity = 1.0f;
                                    pawn.health.AddHediff(hediff);
                                    if (pawn.needs != null && pawn.needs.mood != null)
                                    {
                                        pawn.needs.mood.thoughts.memories.TryGainMemory((Thought_Memory)ThoughtMaker.MakeThought(compTransformer.Props.transformedThought));
                                    }
                                }
                            }
                        }
                        innerContainer.TryDropAll(this.InteractionCell, this.Map, ThingPlaceMode.Near);
                    }, destructive: true));
                }
            };
            if (innerContainer.Count > 0)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Celes_Keyed_ClearTransformer".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Buttons/Drop"),
                    action = delegate ()
                    {
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("Celes_Keyed_ConfirmClearTransformer".Translate(), delegate
                        {
                            if (innerContainer.First() is Pawn pawn)
                            {
                                if (!pawn.RaceProps.Humanlike)
                                {
                                    if (killMode)
                                    {
                                        pawn.Kill(null);
                                    }
                                }
                                else
                                {
                                    Hediff hediff = HediffMaker.MakeHediff(compTransformer.Props.transformedHediff, pawn);
                                    hediff.Severity = 1.0f;
                                    pawn.health.AddHediff(hediff);
                                    if (pawn.needs != null && pawn.needs.mood != null)
                                    {
                                        pawn.needs.mood.thoughts.memories.TryGainMemory((Thought_Memory)ThoughtMaker.MakeThought(compTransformer.Props.transformedThought));
                                    }
                                }
                            }
                            innerContainer.TryDropAll(this.InteractionCell, this.Map, ThingPlaceMode.Near);
                        }, destructive: true));
                    }
                };
            }
            if (chosenTransformingMode == 1)
            {
                if (innerContainer.Count < 1)
                {
                    Command_Action command_Action2 = new Command_Action();
                    command_Action2.defaultLabel = "InsertPerson".Translate() + "...";
                    command_Action2.defaultDesc = "Celes_Keyed_InsertPersonTransformerDesc".Translate(def.label);
                    command_Action2.icon = ContentFinder<Texture2D>.Get("UI/Commands/LoadTransporter");
                    command_Action2.action = delegate
                    {
                        List<FloatMenuOption> list = new List<FloatMenuOption>();
                        IReadOnlyList<Pawn> allPawnsSpawned = base.Map.mapPawns.AllPawnsSpawned;
                        for (int j = 0; j < allPawnsSpawned.Count; j++)
                        {
                            Pawn pawn = allPawnsSpawned[j];
                            AcceptanceReport acceptanceReport = CanAcceptPawn(pawn);
                            if (!acceptanceReport.Accepted)
                            {
                                if (!acceptanceReport.Reason.NullOrEmpty())
                                {
                                    list.Add(new FloatMenuOption(pawn.LabelShortCap + ": " + acceptanceReport.Reason, null, pawn, Color.white));
                                }
                            }
                            else
                            {
                                list.Add(new FloatMenuOption(pawn.LabelShortCap, delegate
                                {
                                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("Celes_Keyed_ConfirmProcessPawn".Translate(pawn.Named("PAWN")), delegate
                                    {
                                        wantedPawn = pawn;
                                    }, destructive: true));
                                }, pawn, Color.white));
                            }
                        }

                        if (!list.Any())
                        {
                            list.Add(new FloatMenuOption("Celes_Keyed_NoTransportablePawns".Translate(), null));
                        }

                        Find.WindowStack.Add(new FloatMenu(list));
                    };

                    yield return command_Action2;
                }
                else
                {
                    if (innerContainer.First() is Pawn pawn && pawn.RaceProps.Humanlike)
                    {
                        yield return new Command_Action
                        {
                            defaultLabel = "Celes_Keyed_SelectInnerPawn".Translate(),
                            icon = ContentFinder<Texture2D>.Get("UI/Designators/Open"),
                            action = delegate ()
                            {
                                Find.Selector.Select(innerContainer.First());
                            }
                        };
                    }
                }
                if (UnlockedKillMode())
                {
                    yield return new Command_Action
                    {
                        defaultLabel = (killMode ? "Celes_Keyed_KillMode".Translate() : "Celes_Keyed_SafeMode".Translate()),
                        icon = (killMode ? ContentFinder<Texture2D>.Get("Things/Mote/SpeechSymbols/Breakup") : ContentFinder<Texture2D>.Get("Things/Mote/Heart")),
                        action = delegate ()
                        {
                            if (killMode)
                            {
                                killMode = false;
                            }
                            else
                            {
                                killMode = true;
                            }
                        }
                    };
                }
            }
            yield return new Command_Action
            {
                defaultLabel = "Celes_Keyed_ChooseProduct".Translate() + compTransformer.Props.transformingModeRecipes[chosenTransformingModeProduct].product.label.Translate(),
                icon = compTransformer.Props.transformingModeRecipes[chosenTransformingModeProduct].product.uiIcon,
                action = delegate ()
                {
                    List<FloatMenuOption> list = new List<FloatMenuOption>();
                    foreach (SubProperties_Celes_Transformer product in compTransformer.Props.transformingModeRecipes)
                    {
                        FloatMenuOption productOption = new FloatMenuOption(product.product.label, delegate
                        {
                            this.chosenTransformingModeProduct = compTransformer.Props.transformingModeRecipes.IndexOf(product);
                        }, product.product.uiIcon, Color.white, MenuOptionPriority.Default, null, null, 29f, (Rect rect) => Widgets.InfoCardButton(rect.x + 5f, rect.y + (rect.height - 24f) / 2f, product.product));
                        list.Add(productOption);
                    }
                    Find.WindowStack.Add(new FloatMenu(list));
                }
            };
            if (chosenTransformingMode == 0)
            {
                yield return new Command_Action
                {
                    defaultLabel = (autoLoad ? "Celes_Keyed_AutoLoadAllowed".Translate() : "Celes_Keyed_AutoLoadDisallowed".Translate()),
                    icon = (autoLoad ? TexCommand.ForbidOff : TexCommand.ForbidOn),
                    action = delegate ()
                    {
                        if (autoLoad)
                        {
                            autoLoad = false;
                        }
                        else
                        {
                            autoLoad = true;
                        }
                    }
                };
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
            yield break;
        }

        public AcceptanceReport CanAcceptPawn(Pawn selPawn)
        {
            if (!selPawn.RaceProps.Humanlike)
            {
                if (!selPawn.RaceProps.IsFlesh)
                {
                    return false;
                }
                if (selPawn.RaceProps.IsFlesh)
                {
                    if (selPawn.Faction != Faction.OfPlayer)
                    {
                        return false;
                    }
                }
            }
            else
            {
                if (!selPawn.IsColonist && !selPawn.IsSlaveOfColony && !selPawn.IsPrisonerOfColony)
                {
                    return false;
                }
            }

            if (selPawn.BodySize > compTransformer.Props.maxBodySize)
            {
                return "Celes_Keyed_BodySizeTooBig".Translate();
            }

            if (!active)
            {
                return "Celes_Keyed_Inactive".Translate();
            }

            if (!compPowerTrader.PowerOn)
            {
                return "CannotUseNoPower".Translate();
            }

            return true;
        }
    }
}
