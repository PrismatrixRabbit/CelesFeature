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
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Celes_PostApplyDamage_DoubleToNext
    {
        
        public static void Postfix(Pawn __instance, DamageInfo dinfo, float totalDamageDealt)
        {
            Harmony.DEBUG = true;
            FileLog.Reset();
            FileLog.Log("初始化段");
            if(__instance==null || __instance.Dead)return;
            if(dinfo.Def!=Celes_DamageDefOf.Celes_Particle && dinfo.Def!=Celes_DamageDefOf.Celes_MassiveParticle)return;
            if(dinfo.HitPart==null)return;
            if (dinfo.Def.GetModExtension<Celes_DamageExtension_Multiplier>()
                is Celes_DamageExtension_Multiplier ext)
            {
                FileLog.Log("作用段");
                float damageAmount = totalDamageDealt * ext.multiplier;
                DamageInfo extraDamage = new DamageInfo(dinfo);
                extraDamage.SetAmount(damageAmount);
                extraDamage.Def = Celes_DamageDefOf.Burn;
                __instance.TakeDamage(extraDamage);
            }
            
        }
    }
}