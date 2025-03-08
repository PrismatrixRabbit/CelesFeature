using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace CelesFeature
{
    public class Celes_RecipeModExtension : DefModExtension
    {
        public List<HediffDef> AllowWhenHediff;
        public List<HediffDef> DisallowWhenHediff;
        public bool useRace=false;
        public List<ThingDef> race=null;
        public bool addHediff;
        public bool removeHediff;
        public HediffDef hediffToAdd;
        public HediffDef hediffToRemove;
    }
    public class Celes_RecipeSurgery:Recipe_Surgery
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part=null)
        {
            return IsValidNow(thing, part);
        }
        public bool IsValidNow(Thing thing, BodyPartRecord part = null, bool ignoreBills = false)
        {
            Celes_RecipeModExtension ext = recipe.GetModExtension<Celes_RecipeModExtension>();
            if (ext.AllowWhenHediff == null || ext.DisallowWhenHediff == null) return false;
            if (!base.AvailableOnNow(thing, part))
            {
                return false;
            }
            if (!(thing is Pawn pawn))
            {
                return false;
            }
            if (!recipe.HasModExtension<Celes_RecipeModExtension>())
            {
                return false;
            }
            
            if (!ignoreBills && pawn.BillStack.Bills.Any((Bill b) => b.recipe == recipe))
            {
                return false;
            }
            
            foreach (HediffDef def in ext.DisallowWhenHediff)
            {
                if (pawn.health.hediffSet.HasHediff(def))
                {
                    return false;
                }
            }
            foreach(HediffDef def in ext.AllowWhenHediff)
            {
                if (!pawn.health.hediffSet.hediffs.Any((Hediff h) => h.def == def))
                {
                    return false;
                }
            }
            
            if (ext.useRace && (!ext.race?.Contains(pawn.def) ?? false))
            {
                return false;
            }
            
            if (pawn.health.hediffSet.hediffs.Any((Hediff x) => !recipe.CompatibleWithHediff(x.def)))
            {
                return false;
            }
            return true;
        }
        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (billDoer != null)
            {
                if (CheckSurgeryFail(billDoer, pawn, ingredients, part, bill))
                {
                    return;
                }
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
            }
            Celes_RecipeModExtension ext = recipe.GetModExtension<Celes_RecipeModExtension>();
            if (ext.addHediff)
            {
                pawn.health.AddHediff(ext.hediffToAdd, part);
            }

            if (ext.removeHediff)
            {
                pawn.health.RemoveHediff(pawn.health.hediffSet.GetFirstHediffOfDef(ext.hediffToRemove));
            }
            OnSurgerySuccess(pawn, part, billDoer, ingredients, bill);
            if (IsViolationOnPawn(pawn, part, Faction.OfPlayerSilentFail))
            {
                ReportViolation(pawn, billDoer, pawn.HomeFaction, -70);
            }
        }
    }
}