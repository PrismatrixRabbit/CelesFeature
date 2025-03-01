using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
namespace CelesFeature
{
    public class Celes_DamageExtension_Multiplier : DefModExtension
    {
        public float multiplier = 0.5f; 
    }
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.PostApplyDamage))]
    public static class Celes_PostApplyDamage_DoubleToNext
    {
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(Pawn_HealthTracker), "pawn");

        public static void Postfix(Pawn_HealthTracker __instance, DamageInfo dinfo, float totalDamageDealt)
        {
            Pawn pawn=PawnField.GetValue(__instance) as Pawn;
            if(pawn==null || pawn.Dead)return;
            if(dinfo.Def!=Celes_DamageDefOf.Celes_Particle && dinfo.Def!=Celes_DamageDefOf.Celes_MassiveParticle)return;
            if(dinfo.HitPart==null)return;

            if (dinfo.Def.GetModExtension<Celes_DamageExtension_Multiplier>()
                is Celes_DamageExtension_Multiplier ext)
            {
                float damageAmount = totalDamageDealt * ext.multiplier;
                DamageInfo extraDamage = new DamageInfo(dinfo);
                extraDamage.SetAmount(damageAmount);
                extraDamage.Def = Celes_DamageDefOf.Burn;
                pawn.TakeDamage(extraDamage);
            }
            
        }
    }
}