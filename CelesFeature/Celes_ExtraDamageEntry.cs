using System.Collections.Generic;
using Verse;
using System.Reflection;
using HarmonyLib;

namespace CelesFeature
{
    public class Celes_ExtraDamageEntry
    {
        public DamageDef def;
        public float amount;
        public float armorPenetration = -1f;
        public float chance = 1f;
        public float AdjustedArmorPenetration()
        {
            if (armorPenetration < 0f)
                return amount * 0.015f;
            return armorPenetration;
        }
    }
    
    public class Celes_DamageExtension_ExtraDamages : DefModExtension
    {
        public List<Celes_ExtraDamageEntry> extraDamages;
    }
    
    [StaticConstructorOnStartup]
    public static class Harmony_CelesExtraDamage
    {
        static Harmony_CelesExtraDamage()
        {
            Harmony harmony = new Harmony("CelesFeature.ExtraDamage");
            // [事实] DamageWorker_AddInjury.FinalizeAndAddInjury 是伤害最终结算入口
            var target = AccessTools.Method(
                AccessTools.TypeByName("Verse.DamageWorker_AddInjury"),
                "FinalizeAndAddInjury",
                new[] { typeof(Pawn), typeof(Hediff_Injury), typeof(DamageInfo), typeof(DamageWorker.DamageResult) });
            harmony.Patch(target,
                postfix: new HarmonyMethod(typeof(Harmony_CelesExtraDamage), nameof(Postfix_FinalizeAndAddInjury)));
        }
        public static void Postfix_FinalizeAndAddInjury(
            Pawn pawn, Hediff_Injury injury, DamageInfo dinfo, DamageWorker.DamageResult result)
        {
            if (pawn == null || pawn.Dead || injury?.Part == null)
                return;
            var ext = dinfo.Def?.GetModExtension<Celes_DamageExtension_ExtraDamages>();
            if (ext?.extraDamages == null)
                return;
            foreach (Celes_ExtraDamageEntry entry in ext.extraDamages)
            {
                if (!Rand.Chance(entry.chance))
                    continue;
                // [事实] DamageInfo(struct) 复制构造函数（DamageInfo.md:181-208）
                DamageInfo extraDamage = new DamageInfo(dinfo);
                extraDamage.SetAmount(entry.amount);
                extraDamage.Def = entry.def;
                extraDamage.SetHitPart(injury.Part);
                pawn.TakeDamage(extraDamage);
            }
        }
    }
}