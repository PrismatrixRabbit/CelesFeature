using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace CelesFeature
{
    public class Celes_RecipeInstallImplant : Recipe_Surgery
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            return IsValidNow(thing, part);
        }

        public bool IsValidNow(Thing thing, BodyPartRecord part = null, bool ignoreBills = false)
        {
            Celes_RecipeModExtension ext = recipe.GetModExtension<Celes_RecipeModExtension>();
            if (!recipe.HasModExtension<Celes_RecipeModExtension>())
            {
                return false;
            }
            if (!(thing is Pawn pawn))
            {
                return false;
            }
            if(!pawn.health.hediffSet.HasHediff(ext.PrerequisiteHediff))
            {
                return false;
            }
            if ((recipe.genderPrerequisite ?? pawn.gender) != pawn.gender)
            {
                return false;
            }
            if (recipe.mustBeFertile && pawn.Sterile())
            {
                return false;
            }
            if (!recipe.allowedForQuestLodgers && pawn.IsQuestLodger())
            {
                return false;
            }
            if (recipe.minAllowedAge > 0 && pawn.ageTracker.AgeBiologicalYears < recipe.minAllowedAge)
            {
                return false;
            }
            if (recipe.developmentalStageFilter.HasValue && !recipe.developmentalStageFilter.Value.Has(pawn.DevelopmentalStage))
            {
                return false;
            }
            if (recipe.humanlikeOnly && !pawn.RaceProps.Humanlike)
            {
                return false;
            }
            if (ModsConfig.AnomalyActive)
            {
                if (recipe.mutantBlacklist != null && pawn.IsMutant && recipe.mutantBlacklist.Contains(pawn.mutant.Def))
                {
                    return false;
                }
                if (recipe.mutantPrerequisite != null && (!pawn.IsMutant || !recipe.mutantPrerequisite.Contains(pawn.mutant.Def)))
                {
                    return false;
                }
            }
            return true;
        }
        
        public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn, RecipeDef recipe)
        {
            return MedicalRecipesUtility.GetFixedPartsToApplyOn(recipe, pawn, delegate(BodyPartRecord record)
            {
                if (!pawn.health.hediffSet.GetNotMissingParts().Contains(record))
                {
                    return false;
                }
                if (pawn.health.hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(record))
                {
                    return false;
                }
                return !pawn.health.hediffSet.hediffs.Any((Hediff x) => x.Part == record && (x.def == recipe.addsHediff || !recipe.CompatibleWithHediff(x.def)));
            });
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
            pawn.health.AddHediff(recipe.addsHediff, part);
        }
    }
}