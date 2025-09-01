using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace CelesFeature
{
    public class Celes_JobDriver_DoAutomaticBill : JobDriver
    {
        public float workLeft;

        public int billStartTick;

        public int ticksSpentDoingRecipeWork;

        public const PathEndMode GotoIngredientPathEndMode = PathEndMode.ClosestTouch;

        public const TargetIndex BillGiverInd = TargetIndex.A;

        public const TargetIndex IngredientInd = TargetIndex.B;

        public const TargetIndex IngredientPlaceCellInd = TargetIndex.C;

        public IBillGiver BillGiver => (job.GetTarget(TargetIndex.A).Thing as IBillGiver) ?? throw new InvalidOperationException("DoBill on non-Billgiver.");

        public bool AnyIngredientsQueued => !job.GetTargetQueue(TargetIndex.B).NullOrEmpty();

        public override string GetReport()
        {
            if (job.RecipeDef != null)
            {
                return ReportStringProcessed(job.RecipeDef.jobString);
            }

            return base.GetReport();
        }

        public override bool IsContinuation(Job j)
        {
            return j.bill == job.bill;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref workLeft, "workLeft", 0f);
            Scribe_Values.Look(ref billStartTick, "billStartTick", 0);
            Scribe_Values.Look(ref ticksSpentDoingRecipeWork, "ticksSpentDoingRecipeWork", 0);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Thing thing = job.GetTarget(TargetIndex.A).Thing;
            if (!pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, -1, null, errorOnFailed))
            {
                return false;
            }

            if (thing != null && thing.def.hasInteractionCell && !pawn.ReserveSittableOrSpot(thing.InteractionCell, job, errorOnFailed))
            {
                return false;
            }

            pawn.ReserveAsManyAsPossible(job.GetTargetQueue(TargetIndex.B), job);
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddEndCondition(delegate
            {
                Thing thing = GetActor().jobs.curJob.GetTarget(TargetIndex.A).Thing;
                Celes_Bill_AutomaticIndustry billauto = (Celes_Bill_AutomaticIndustry)job.bill;
                if (billauto.suspended || billauto.paused)
                {
                    return !(billauto.suspended || billauto.paused) ? JobCondition.Ongoing : JobCondition.Incompletable;
                }
                return (!(thing is Building) || thing.Spawned) ? JobCondition.Ongoing : JobCondition.Incompletable;
            });
            this.FailOnBurningImmobile(TargetIndex.A);
            this.FailOn(delegate
            {
                if (job.GetTarget(TargetIndex.A).Thing is IBillGiver billGiver)
                {
                    if (job.bill.DeletedOrDereferenced)
                    {
                        return true;
                    }

                    if (!billGiver.CurrentlyUsableForBills())
                    {
                        return true;
                    }
                }

                return false;
            });
            Toil gotoBillGiver = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            Toil toil = ToilMaker.MakeToil("MakeNewToils");
            toil.initAction = delegate
            {
                if (job.targetQueueB != null && job.targetQueueB.Count == 1 && job.targetQueueB[0].Thing is UnfinishedThing unfinishedThing)
                {
                    unfinishedThing.BoundBill = (Bill_ProductionWithUft)job.bill;
                }

                job.bill.Notify_DoBillStarted(pawn);
            };
            yield return toil;
            yield return Toils_Jump.JumpIf(gotoBillGiver, () => job.GetTargetQueue(TargetIndex.B).NullOrEmpty());
            foreach (Toil item in CollectIngredientsToils(TargetIndex.B, TargetIndex.A, TargetIndex.C, subtractNumTakenFromJobCount: false, failIfStackCountLessThanJobCount: true, BillGiver is Celes_Building_AutomaticIndustry))
            {
                yield return item;
            }

            yield return gotoBillGiver;
            yield return Toils_Recipe.MakeUnfinishedThingIfNeeded();
            yield return DoAutomaticIndustryWork().FailOnDespawnedNullOrForbiddenPlacedThings(TargetIndex.A).FailOnCannotTouch(TargetIndex.A, PathEndMode.InteractionCell);
            yield return Toils_Recipe.CheckIfRecipeCanFinishNow();
            yield return EjectProducts();
        }

        public static Toil EjectProducts()
        {
            Toil toil = ToilMaker.MakeToil("FinishRecipeAndStartStoringProduct");
            toil.initAction = delegate
            {
                Thing thing = toil.actor.jobs.curJob.GetTarget(TargetIndex.A).Thing;
                Celes_Building_AutomaticIndustry building = thing as Celes_Building_AutomaticIndustry;
                building.ActiveBill.Notify_IterationCompleted();
                building.EjectContents();
            };
            return toil;
        }
        public static Toil DoAutomaticIndustryWork()
        {
            Toil toil = ToilMaker.MakeToil("DoRecipeWork");
            toil.initAction = delegate
            {
                Pawn actor3 = toil.actor;
                Job curJob3 = actor3.jobs.curJob;
                Celes_JobDriver_DoAutomaticBill jobDriver_DoBill2 = (Celes_JobDriver_DoAutomaticBill)actor3.jobs.curDriver;
                Thing thing3 = curJob3.GetTarget(TargetIndex.B).Thing;
                UnfinishedThing unfinishedThing2 = thing3 as UnfinishedThing;
                _ = curJob3.GetTarget(TargetIndex.A).Thing.def.building;
                if (unfinishedThing2 != null && unfinishedThing2.Initialized)
                {
                    jobDriver_DoBill2.workLeft = unfinishedThing2.workLeft;
                }
                else
                {
                    jobDriver_DoBill2.workLeft = curJob3.bill.GetWorkAmount(thing3);
                    if (unfinishedThing2 != null)
                    {
                        if (unfinishedThing2.debugCompleted)
                        {
                            unfinishedThing2.workLeft = (jobDriver_DoBill2.workLeft = 0f);
                        }
                        else
                        {
                            unfinishedThing2.workLeft = jobDriver_DoBill2.workLeft;
                        }
                    }
                }

                jobDriver_DoBill2.billStartTick = Find.TickManager.TicksGame;
                jobDriver_DoBill2.ticksSpentDoingRecipeWork = 0;
                curJob3.bill.Notify_BillWorkStarted(actor3);
            };
            toil.tickAction = delegate
            {
                Pawn actor3 = toil.actor;
                Thing thing3 = actor3.jobs.curJob.GetTarget(TargetIndex.B).Thing;
                if (thing3 is UnfinishedThing && thing3.Destroyed)
                {
                    actor3.jobs.EndCurrentJob(JobCondition.Incompletable);
                }
                else if (toil.actor.CurJob.GetTarget(TargetIndex.A).Thing is IBillGiverWithTickAction billGiverWithTickAction)
                {
                    billGiverWithTickAction.UsedThisTick();
                }
            };
            toil.tickIntervalAction = delegate (int delta)
            {
                Pawn actor2 = toil.actor;
                Job curJob2 = actor2.jobs.curJob;
                Celes_JobDriver_DoAutomaticBill jobDriver_DoBill = (Celes_JobDriver_DoAutomaticBill)actor2.jobs.curDriver;
                UnfinishedThing unfinishedThing = curJob2.GetTarget(TargetIndex.B).Thing as UnfinishedThing;
                if (unfinishedThing != null && unfinishedThing.Destroyed)
                {
                    actor2.jobs.EndCurrentJob(JobCondition.Incompletable);
                }
                else
                {
                    jobDriver_DoBill.ticksSpentDoingRecipeWork += delta;
                    curJob2.bill.Notify_PawnDidWork(actor2);
                    if (curJob2.RecipeDef.workSkill != null && curJob2.RecipeDef.UsesUnfinishedThing && actor2.skills != null)
                    {
                        actor2.skills.Learn(curJob2.RecipeDef.workSkill, 0.1f * curJob2.RecipeDef.workSkillLearnFactor * (float)delta);
                    }

                    float num2 = ((curJob2.RecipeDef.workSpeedStat == null) ? 1f : actor2.GetStatValue(curJob2.RecipeDef.workSpeedStat));
                    if (curJob2.RecipeDef.workTableSpeedStat != null && jobDriver_DoBill.BillGiver is Building_WorkTable thing2)
                    {
                        num2 *= thing2.GetStatValue(curJob2.RecipeDef.workTableSpeedStat);
                    }

                    if (DebugSettings.fastCrafting)
                    {
                        num2 *= 30f;
                    }

                    jobDriver_DoBill.workLeft -= num2 * (float)delta;
                    if (unfinishedThing != null)
                    {
                        if (unfinishedThing.debugCompleted)
                        {
                            unfinishedThing.workLeft = (jobDriver_DoBill.workLeft = 0f);
                        }
                        else
                        {
                            unfinishedThing.workLeft = jobDriver_DoBill.workLeft;
                        }
                    }

                    actor2.GainComfortFromCellIfPossible(delta, chairsOnly: true);
                    if (jobDriver_DoBill.workLeft <= 0f)
                    {
                        curJob2.bill.Notify_BillWorkFinished(actor2);
                        jobDriver_DoBill.ReadyForNextToil();
                    }
                    else if (curJob2.bill.recipe.UsesUnfinishedThing && Find.TickManager.TicksGame - jobDriver_DoBill.billStartTick >= 3000 && actor2.IsHashIntervalTick(1000, delta))
                    {
                        actor2.jobs.CheckForJobOverride();
                    }
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Never;
            toil.WithEffect(() => toil.actor.CurJob.bill.recipe.effectWorking, TargetIndex.A);
            toil.PlaySustainerOrSound(() => toil.actor.CurJob.bill.recipe.soundWorking);
            toil.WithProgressBar(TargetIndex.A, delegate
            {
                Pawn actor = toil.actor;
                Job curJob = actor.CurJob;
                Thing thing = curJob.GetTarget(TargetIndex.B).Thing;
                float workLeft = ((Celes_JobDriver_DoAutomaticBill)actor.jobs.curDriver).workLeft;
                float num = ((curJob.bill is Celes_Bill_AutomaticIndustry bill_Mech && bill_Mech.State == FormingState.Formed) ? 300f : curJob.bill.recipe.WorkAmountTotal(thing));
                return 1f - workLeft / num;
            });
            toil.FailOn((Func<bool>)delegate
            {
                RecipeDef recipeDef = toil.actor.CurJob.RecipeDef;
                if (recipeDef != null && recipeDef.interruptIfIngredientIsRotting)
                {
                    LocalTargetInfo target = toil.actor.CurJob.GetTarget(TargetIndex.B);
                    if (target.HasThing && (int)target.Thing.GetRotStage() > 0)
                    {
                        return true;
                    }
                }
                Celes_Bill_AutomaticIndustry billauto = (Celes_Bill_AutomaticIndustry)toil.actor.CurJob.bill;
                if (billauto.suspended || billauto.paused)
                {
                    if (toil.actor.CurJob.bill.billStack.billGiver is Celes_Building_AutomaticIndustry)
                    {
                        Celes_Building_AutomaticIndustry worktable = (Celes_Building_AutomaticIndustry)toil.actor.CurJob.bill.billStack.billGiver;
                        worktable.EjectContents();
                        return (billauto.suspended || billauto.paused);
                    }
                }
                return (billauto.suspended || billauto.paused);
            });
            toil.activeSkill = () => toil.actor.CurJob.bill.recipe.workSkill;
            return toil;
        }

        public static IEnumerable<Toil> CollectIngredientsToils(TargetIndex ingredientInd, TargetIndex billGiverInd, TargetIndex ingredientPlaceCellInd, bool subtractNumTakenFromJobCount = false, bool failIfStackCountLessThanJobCount = true, bool placeInBillGiver = false)
        {
            Toil extract = Toils_JobTransforms.ExtractNextTargetFromQueue(ingredientInd);
            yield return extract;
            Toil jumpIfHaveTargetInQueue = Toils_Jump.JumpIfHaveTargetInQueue(ingredientInd, extract);
            yield return JumpIfTargetInsideBillGiver(jumpIfHaveTargetInQueue, ingredientInd, billGiverInd);
            Toil getToHaulTarget = Toils_Goto.GotoThing(ingredientInd, PathEndMode.ClosestTouch).FailOnDespawnedNullOrForbidden(ingredientInd).FailOnSomeonePhysicallyInteracting(ingredientInd);
            yield return getToHaulTarget;
            yield return Toils_Haul.StartCarryThing(ingredientInd, putRemainderInQueue: true, subtractNumTakenFromJobCount, failIfStackCountLessThanJobCount, reserve: false);
            yield return JumpToCollectNextIntoHandsForBill(getToHaulTarget, TargetIndex.B);
            yield return Toils_Goto.GotoThing(billGiverInd, PathEndMode.InteractionCell).FailOnDestroyedOrNull(ingredientInd);
            if (!placeInBillGiver)
            {
                Toil findPlaceTarget = Toils_JobTransforms.SetTargetToIngredientPlaceCell(billGiverInd, ingredientInd, ingredientPlaceCellInd);
                yield return findPlaceTarget;
                yield return Toils_Haul.PlaceHauledThingInCell(ingredientPlaceCellInd, findPlaceTarget, storageMode: false);
                Toil physReserveToil = ToilMaker.MakeToil("CollectIngredientsToils");
                physReserveToil.initAction = delegate
                {
                    physReserveToil.actor.Map.physicalInteractionReservationManager.Reserve(physReserveToil.actor, physReserveToil.actor.CurJob, physReserveToil.actor.CurJob.GetTarget(ingredientInd));
                };
                yield return physReserveToil;
            }
            else
            {
                yield return Celes_JobDriver_DoAutomaticBill.DepositHauledThingInContainer(billGiverInd, ingredientInd);
            }

            yield return jumpIfHaveTargetInQueue;
        }
        public static Toil DepositHauledThingInContainer(TargetIndex containerInd, TargetIndex reserveForContainerInd, Action onDeposited = null)
        {
            Toil toil = ToilMaker.MakeToil("DepositHauledThingInContainer");
            toil.initAction = delegate
            {
                Pawn actor = toil.actor;
                Job curJob = actor.jobs.curJob;
                if (actor.carryTracker.CarriedThing == null)
                {
                    Log.Error(string.Concat(actor, " tried to place hauled thing in container but is not hauling anything."));
                }
                else
                {
                    Thing thing = curJob.GetTarget(containerInd).Thing;
                    ThingOwner thingOwner = thing.TryGetInnerInteractableThingOwner();
                    if (thingOwner != null)
                    {
                        int num = actor.carryTracker.CarriedThing.stackCount;
                        if (thing is IHaulEnroute haulEnroute)
                        {
                            ThingDef def = actor.carryTracker.CarriedThing.def;
                            num = Mathf.Min(haulEnroute.GetSpaceRemainingWithEnroute(def, actor), num);
                            if (reserveForContainerInd != 0)
                            {
                                Thing thing2 = curJob.GetTarget(reserveForContainerInd).Thing;
                                if (!thing2.DestroyedOrNull() && thing2 != haulEnroute && thing2 is IHaulEnroute enroute)
                                {
                                    int spaceRemainingWithEnroute = enroute.GetSpaceRemainingWithEnroute(def, actor);
                                    num = Mathf.Min(num, actor.carryTracker.CarriedThing.stackCount - spaceRemainingWithEnroute);
                                }
                            }
                        }

                        Thing carriedThing = actor.carryTracker.CarriedThing;
                        int num2 = actor.carryTracker.innerContainer.TryTransferToContainer(carriedThing, thingOwner, num);
                        if (num2 != 0)
                        {
                            if (thing is IHaulEnroute container)
                            {
                                thing.Map.enrouteManager.ReleaseFor(container, actor);
                            }

                            if (thing is INotifyHauledTo notifyHauledTo)
                            {
                                notifyHauledTo.Notify_HauledTo(actor, carriedThing, num2);
                            }

                            if (thing is ThingWithComps thingWithComps)
                            {
                                foreach (ThingComp allComp in thingWithComps.AllComps)
                                {
                                    if (allComp is INotifyHauledTo notifyHauledTo2)
                                    {
                                        notifyHauledTo2.Notify_HauledTo(actor, carriedThing, num2);
                                    }
                                }
                            }

                            if (curJob.def == Celes_DefOf_Job.Celes_Job_DoAutomaticBill)
                            {
                                HaulAIUtility.UpdateJobWithPlacedThings(curJob, carriedThing, num2);
                            }

                            onDeposited?.Invoke();
                        }
                    }
                    else if (curJob.GetTarget(containerInd).Thing.def.Minifiable)
                    {
                        actor.carryTracker.innerContainer.ClearAndDestroyContents();
                    }
                    else
                    {
                        Log.Error("Could not deposit hauled thing in container: " + curJob.GetTarget(containerInd).Thing);
                    }
                }
            };
            return toil;
        }

        private static Toil JumpIfTargetInsideBillGiver(Toil jumpToil, TargetIndex ingredient, TargetIndex billGiver)
        {
            Toil toil = ToilMaker.MakeToil("JumpIfTargetInsideBillGiver");
            toil.initAction = delegate
            {
                Thing thing = toil.actor.CurJob.GetTarget(billGiver).Thing;
                if (thing != null && thing.Spawned)
                {
                    Thing thing2 = toil.actor.jobs.curJob.GetTarget(ingredient).Thing;
                    if (thing2 != null)
                    {
                        ThingOwner thingOwner = thing.TryGetInnerInteractableThingOwner();
                        if (thingOwner != null && thingOwner.Contains(thing2))
                        {
                            HaulAIUtility.UpdateJobWithPlacedThings(toil.actor.jobs.curJob, thing2, thing2.stackCount);
                            toil.actor.jobs.curDriver.JumpToToil(jumpToil);
                        }
                    }
                }
            };
            return toil;
        }

        public static Toil JumpToCollectNextIntoHandsForBill(Toil gotoGetTargetToil, TargetIndex ind)
        {
            Toil toil = ToilMaker.MakeToil("JumpToCollectNextIntoHandsForBill");
            toil.initAction = delegate
            {
                Pawn actor = toil.actor;
                if (actor.carryTracker.CarriedThing == null)
                {
                    Log.Error(string.Concat("JumpToAlsoCollectTargetInQueue run on ", actor, " who is not carrying something."));
                }
                else if (!actor.carryTracker.Full)
                {
                    Job curJob = actor.jobs.curJob;
                    List<LocalTargetInfo> targetQueue = curJob.GetTargetQueue(ind);
                    if (!targetQueue.NullOrEmpty())
                    {
                        for (int i = 0; i < targetQueue.Count; i++)
                        {
                            if (GenAI.CanUseItemForWork(actor, targetQueue[i].Thing) && targetQueue[i].Thing.CanStackWith(actor.carryTracker.CarriedThing) && !((float)(actor.Position - targetQueue[i].Thing.Position).LengthHorizontalSquared > 64f))
                            {
                                int num = ((actor.carryTracker.CarriedThing != null) ? actor.carryTracker.CarriedThing.stackCount : 0);
                                int a = curJob.countQueue[i];
                                a = Mathf.Min(a, targetQueue[i].Thing.def.stackLimit - num);
                                a = Mathf.Min(a, actor.carryTracker.AvailableStackSpace(targetQueue[i].Thing.def));
                                if (a > 0)
                                {
                                    curJob.count = a;
                                    curJob.SetTarget(ind, targetQueue[i].Thing);
                                    curJob.countQueue[i] -= a;
                                    if (curJob.countQueue[i] <= 0)
                                    {
                                        curJob.countQueue.RemoveAt(i);
                                        targetQueue.RemoveAt(i);
                                    }

                                    actor.jobs.curDriver.JumpToToil(gotoGetTargetToil);
                                    break;
                                }
                            }
                        }
                    }
                }
            };
            return toil;
        }
    }
}
