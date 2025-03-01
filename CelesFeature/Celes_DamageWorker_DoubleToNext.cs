using System;
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
    [HarmonyPatch]
    public static class Celes_DamageWorker_DoubleToNext
    {
        static MethodBase TargetMethod()
        {
            Type targetType = AccessTools.TypeByName("Verse.DamageWorker_AddInjury");
            return AccessTools.Method(targetType, "FinalizeAndAddInjury", new[] { typeof(Pawn)
                , typeof(Hediff_Injury),typeof(DamageInfo),typeof(DamageWorker.DamageResult) });
        }
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, Hediff_Injury injury, DamageInfo dinfo, DamageWorker.DamageResult result)
        {
            Harmony.DEBUG = true;
            FileLog.Reset();
            FileLog.Log("初始化段");
            if(pawn==null || pawn.Dead)
            {
                FileLog.Log("人死或不存在");
                return;
            }
            if(dinfo.Def!=Celes_DamageDefOf.Celes_Particle && dinfo.Def!=Celes_DamageDefOf.Celes_MassiveParticle)
            {
                FileLog.Log("非星铃伤害类型");
                return;
            }
            if(injury.Part==null)
            {
                FileLog.Log("部件无效");
                return;
            }
            if (dinfo.Def.GetModExtension<Celes_DamageExtension_Multiplier>()
                is Celes_DamageExtension_Multiplier ext)
            {
                FileLog.Log("作用段");
                float damageAmount = dinfo.Amount * ext.multiplier;
                DamageInfo extraDamage = new DamageInfo(dinfo);
                extraDamage.SetAmount(damageAmount);
                extraDamage.SetHitPart(injury.Part);
                extraDamage.Def = Celes_DamageDefOf.Burn;
                pawn.TakeDamage(extraDamage);
            }
            
        }
    }
}