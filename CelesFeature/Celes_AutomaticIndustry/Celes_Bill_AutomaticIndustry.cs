using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    [HarmonyPatch(typeof(BillUtility), "MakeNewBill")]
    public class Celes_Bill_AutomaticIndustry_Patch
    {
        private static bool Prefix(ref RecipeDef recipe, ref Precept_ThingStyle precept, ref Bill __result)
        {
            if (recipe.recipeUsers != null)
            {
                if (recipe.recipeUsers.ContainsAny((ThingDef t) => t.GetType() == typeof(Celes_Building_AutomaticIndustry)))
                {
                    __result = new Celes_Bill_AutomaticIndustry(recipe, precept);
                    return false;
                }
            }
            return true;
        }
    }

    public class Celes_Bill_AutomaticIndustry : Bill_Production
    {
        public const int CompleteBillTicks = 300;

        public float formingTicks;

        protected int startedTick;

        private int processingCycles;

        protected FormingState state;

        private List<IngredientCount> ingredients = new List<IngredientCount>();

        public FormingState State => state;

        public Celes_Building_AutomaticIndustry WorkTable => (Celes_Building_AutomaticIndustry)billStack.billGiver;

        public override bool CanFinishNow => state == FormingState.Formed;

        protected override string StatusString
        {
            get
            {
                switch (State)
                {
                    case FormingState.Gathering:
                        return "";
                    case FormingState.Preparing:
                        return "";
                    case FormingState.Forming:
                        return "Celes_Keyed_ProductionCycle_Manufacturing".Translate();
                    case FormingState.Formed:
                        return "";
                }

                return null;
            }
        }
        protected override float StatusLineMinHeight => 20f;
        private List<IngredientCount> CurrentBillIngredients
        {
            get
            {
                if (ingredients.Count == 0)
                {
                    this.MakeIngredientsListInProcessingOrder(ingredients);
                }

                return ingredients;
            }
        }

        public Celes_Bill_AutomaticIndustry()
        {
        }

        public Celes_Bill_AutomaticIndustry(RecipeDef recipe, Precept_ThingStyle precept = null)
            : base(recipe, precept)
        {
        }

        protected override Color BaseColor
        {
            get
            {
                if (suspended || paused)
                {
                    return base.BaseColor;
                }

                return Color.white;
            }
        }

        protected override Window GetBillDialog()
        {
            return new Celes_Dialog_AutomaticIndustry(this, ((Thing)billStack.billGiver).Position);
        }

        public virtual Thing CreateProducts()
        {
            ThingDef thingdef = recipe.products.First().thingDef;
            Thing thing = ThingMaker.MakeThing(thingdef);
            thing.stackCount = recipe.products.First().count;
            return thing;
        }

        public void ForceCompleteAllCycles()
        {
            processingCycles = recipe.gestationCycles;
            formingTicks = 0f;
        }

        public override bool ShouldDoNow()
        {
            if (!base.ShouldDoNow())
            {
                paused = true;
                return false;
            }
            else
            {
                paused = false;
            }

            if (paused == true)
            {
                return false;
            }

            if (state == FormingState.Forming)
            {
                return false;
            }

            return true;
        }

        public override bool PawnAllowedToStartAnew(Pawn p)
        {
            if (!base.PawnAllowedToStartAnew(p))
            {
                return false;
            }

            if (WorkTable.ActiveBill != null)
            {
                if (WorkTable.ActiveBill.State != 0)
                {
                    return false;
                }
            }

            return true;
        }

        public override void Notify_DoBillStarted(Pawn billDoer)
        {
            base.Notify_DoBillStarted(billDoer);
            WorkTable.ActiveBill = this;
            startedTick = Find.TickManager.TicksGame;
        }

        public override void Notify_BillWorkFinished(Pawn billDoer)
        {
            base.Notify_BillWorkFinished(billDoer);
            switch (state)
            {
                case FormingState.Gathering:
                    state = FormingState.Forming;
                    formingTicks = recipe.formingTicks;
                    WorkTable.Notify_StartForming(billDoer);
                    break;
                case FormingState.Preparing:
                    formingTicks = recipe.formingTicks;
                    state = FormingState.Forming;
                    break;
                case FormingState.Forming:
                case FormingState.Formed:
                    break;
            }
        }

        public virtual void Reset()
        {
            ingredients.Clear();
            state = FormingState.Gathering;
        }

        public void Notify_IterationCompleted()
        {
            if (repeatMode == BillRepeatModeDefOf.RepeatCount)
            {
                if (repeatCount > 0)
                {
                    repeatCount--;
                }

                if (repeatCount == 0)
                {
                    Messages.Message("MessageBillComplete".Translate(LabelCap), (Thing)billStack.billGiver, MessageTypeDefOf.TaskCompletion);
                    if (WorkTable.ActiveBill == this)
                    {
                        WorkTable.ActiveBill = null;
                    }
                }
            }
            Reset();
        }

        public override float GetWorkAmount(Thing thing = null)
        {
            if (state == FormingState.Formed)
            {
                return 300f;
            }

            return base.GetWorkAmount(thing);
        }

        public virtual void BillTick()
        {
            if (suspended || state != FormingState.Forming)
            {
                return;
            }
            if (formingTicks > 0f)
            {
                formingTicks -= 1f * WorkTable.GetEfficiency();
            }
            if (formingTicks <= 0f)
            {
                processingCycles++;
                if (processingCycles >= recipe.gestationCycles)
                {
                    state = FormingState.Formed;
                    WorkTable.Notify_FormingCompleted();
                    Notify_IterationCompleted();
                }
                else
                {
                    formingTicks = recipe.formingTicks;
                    state = FormingState.Preparing;
                }
            }
        }

        public void AppendCurrentIngredientCount(StringBuilder sb)
        {
            foreach (IngredientCount currentBillIngredient in CurrentBillIngredients)
            {
                if (currentBillIngredient != null && currentBillIngredient.IsFixedIngredient)
                {
                    TaggedString labelCap = currentBillIngredient.FixedIngredient.LabelCap;
                    int num = WorkTable.innerContainer.TotalStackCountOfDef(currentBillIngredient.FixedIngredient);
                    labelCap += $" {num} / {currentBillIngredient.CountRequiredOfFor(currentBillIngredient.FixedIngredient, recipe, this)}";
                    sb.AppendLine(labelCap);
                }
            }
        }

        public virtual void AppendInspectionData(StringBuilder sb)
        {
            switch (State)
            {
                case FormingState.Gathering:
                    AppendCurrentIngredientCount(sb);
                    break;
                case FormingState.Preparing:
                    sb.AppendLine("Celes_Keyed_ProductionCycle_Producing".Translate(recipe.ProducedThingDef.LabelCap));
                    sb.AppendLine("Celes_Keyed_CurrentProductionCycle".Translate() + ": " + ((int)(formingTicks * 1f)).ToStringTicksToPeriod());
                    sb.AppendLine(string.Concat(string.Concat("Celes_Keyed_RemainingProcessingCycles".Translate() + ": ", (recipe.gestationCycles - processingCycles).ToString(), " (") + "OfLower".Translate() + " ", recipe.gestationCycles.ToString(), ")"));
                    break;
                case FormingState.Forming:
                    sb.AppendLine("Celes_Keyed_ProductionCycle_Producing".Translate(recipe.ProducedThingDef.LabelCap));
                    sb.AppendLine("Celes_Keyed_CurrentProductionCycle".Translate() + ": " + ((int)(formingTicks * 1f)).ToStringTicksToPeriod());
                    break;
                case FormingState.Formed:
                    break;
            }
            if (State == FormingState.Forming || State == FormingState.Preparing)
            {
                sb.AppendTagged("FinishesIn".Translate() + ": " + ((int)formingTicks).ToStringTicksToPeriod());
            }

            if (State == FormingState.Formed)
            {
                sb.AppendLine("Finished".Translate());
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref formingTicks, "Celes_SaveKey_float_Bill_AutomaticIndustry_formingTicks", 0f);
            Scribe_Values.Look(ref state, "Celes_SaveKey_FormingState_Bill_AutomaticIndustry_state", FormingState.Gathering);
            Scribe_Values.Look(ref startedTick, "Celes_SaveKey_int_Bill_AutomaticIndustry_startedTick", 0);
            Scribe_Values.Look(ref processingCycles, "Celes_SaveKey_int_Bill_AutomaticIndustry_processingCycles", 0);
        }
    }
}
