using HarmonyLib;
using RimWorld;
using Verse;
namespace CelesFeature
{
    public class Celes_DamageExtension_Multiplier : DefModExtension
    {
        public float multiplier = 0.5f; 
    }
    [HarmonyPatch(typeof(DamageWorker), nameof(DamageWorker.Apply))]
    public static class Celes_PostApplyDamage_DoubleToNext
    {
        private static readonly string appliedDamageFlag = "ExtraDamageApplied";

        [HarmonyPostfix]
        public static void ApplyExtraDamage(DamageInfo dinfo, Thing target, DamageWorker __instance, DamageWorker.DamageResult __result)
        {
            if (target is not Pawn pawn || dinfo.Amount <= 0 || (dinfo.Def != Celes_DamageDefOf.Celes_Particle &&
                dinfo.Def != Celes_DamageDefOf.Celes_MassiveParticle))
            {
                return;
            }

            if (dinfo.Def.GetModExtension<Celes_DamageExtension_Multiplier>()
                is Celes_DamageExtension_Multiplier ext)
            {
                float extraDamage = dinfo.Amount * ext.multiplier;
                DamageInfo newDinfo = new DamageInfo(dinfo);
                newDinfo.SetAmount(extraDamage);
                newDinfo.Def = Celes_DamageDefOf.Burn;
                pawn.TakeDamage(newDinfo);
            }
        }
    }
}