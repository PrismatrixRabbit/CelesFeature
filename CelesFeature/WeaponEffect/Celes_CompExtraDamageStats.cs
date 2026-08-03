using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace CelesFeature
{
    public class Celes_CompProperties_ExtraDamageStats : CompProperties
    {
        public Celes_CompProperties_ExtraDamageStats()
        {
            compClass = typeof(Celes_CompExtraDamageStats);
        }
        // [事实] 单模式武器入口——ModExtension 挂在 DamageDef，通过弹头 Def 获取
        public override IEnumerable<StatDrawEntry> SpecialDisplayStats(StatRequest req)
        {
            ThingDef weaponDef = req.HasThing ? req.Thing.def : req.Def as ThingDef;
            if (weaponDef == null)
                yield break;
            VerbProperties verb = weaponDef.Verbs?.FirstOrDefault(v => v.isPrimary);
            DamageDef damageDef = verb?.defaultProjectile?.projectile?.damageDef;
            var ext = damageDef?.GetModExtension<Celes_DamageExtension_ExtraDamages>();
            if (ext?.extraDamages.NullOrEmpty() ?? true)
                yield break;
            yield return BuildStatDrawEntry(ext.extraDamages, StatCategoryDefOf.Weapon_Ranged, 5450);
        }
        // [事实] 供功能一 1D Postfix 复用——让双模式武器无需 CompProperties 也能生成 Stats
        // 参数: damageDef = 当前模式 Verb 对应的 DamageDef
        public static StatDrawEntry BuildFromDamageDef(DamageDef damageDef)
        {
            var ext = damageDef?.GetModExtension<Celes_DamageExtension_ExtraDamages>();
            if (ext?.extraDamages.NullOrEmpty() ?? true)
                return null;
            return BuildStatDrawEntry(ext.extraDamages, StatCategoryDefOf.Weapon_Ranged, 5450);
        }
        // [事实] 内部：list → StatDrawEntry（单条合并）
        private static StatDrawEntry BuildStatDrawEntry(
            List<Celes_ExtraDamageEntry> extraDamages, StatCategoryDef cat, int priority)
        {
            return new StatDrawEntry(
                cat,
                "Celes_Keyed_ExtraDamageStatLabel".Translate(),
                TotalValue(extraDamages),
                BuildDescription(extraDamages),
                priority
            );
        }
        private static string TotalValue(List<Celes_ExtraDamageEntry> extraDamages)
        {
            float total = 0f;
            for (int i = 0; i < extraDamages.Count; i++)
                total += extraDamages[i].amount * extraDamages[i].chance;
            return total.ToString("F0");
        }
        public static string BuildDescription(List<Celes_ExtraDamageEntry> extraDamages)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Celes_Keyed_ExtraDamageStatDesc".Translate());
            for (int i = 0; i < extraDamages.Count; i++)
            {
                Celes_ExtraDamageEntry ed = extraDamages[i];
                sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine("Celes_Keyed_ExtraDamageStatHeader".Translate(ed.def.label));
                sb.AppendLine();
                sb.AppendLine("  " + "Celes_Keyed_ExtraDamageStatValue".Translate(ed.amount));
                sb.AppendLine();
                sb.AppendLine("  " + "Celes_Keyed_ExtraDamageStatPenetration"
                    .Translate(ed.AdjustedArmorPenetration().ToStringPercent()));
                sb.AppendLine();
                if (ed.chance >= 1f)
                    sb.AppendLine("  " + "Celes_Keyed_ExtraDamageStatGuaranteed".Translate());
                else
                    sb.AppendLine("  " + "Celes_Keyed_ExtraDamageStatChance"
                        .Translate(ed.chance.ToStringPercent()));
            }
            return sb.ToString().TrimEndNewlines();
        }
    }
    // Comp 壳保留
    public class Celes_CompExtraDamageStats : ThingComp
    {
    }
}