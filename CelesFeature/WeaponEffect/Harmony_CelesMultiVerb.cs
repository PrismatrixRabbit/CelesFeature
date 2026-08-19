using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    [StaticConstructorOnStartup]
    public static class Harmony_CelesMultiVerb
    {
        // [事实] 参照 AzWeaponLib verbPriorities（Patch_ThingDef 反编译段）
        private static readonly HashSet<int> verbPriorities = new HashSet<int>
        {
            3555, // WarmupTime
            5500, // Damage
            5450, // ExtraDamage
            5400, // ArmorPenetration
            5410, // BuildingDamageFactor
            5430, // BuildingDamageFactorPassable
            5420, // BuildingDamageFactorImpassable
            5402, // StoppingPower
            5391, // BurstShotCount
            5395, // BurstShotFireRate
            5390, // Range
        };
        // ── 1B: Transpiler ──
        static Harmony_CelesMultiVerb()
        {
            Harmony harmony = new Harmony("CelesFeature.MultiVerb");
            // 2026-08-15 鲁棒性修正：Transpiler → Prefix——Transpiler 修改 IL 使其他 Mod 的 Transpiler 基础改变（多 Mod 顺序组合必然敏感）；
            //   Prefix 不碰 IL，其他 Mod 的 Transpiler/Postfix 不受影响（多 Prefix 短路链敏感度远低于 IL 组合）
            harmony.Patch(
                AccessTools.PropertyGetter(typeof(VerbTracker), nameof(VerbTracker.PrimaryVerb)),
                prefix: new HarmonyMethod(typeof(Harmony_CelesMultiVerb), nameof(Prefix_PrimaryVerb))
            );
            // [事实] 1D: Postfix ThingDef.SpecialDisplayStats
            harmony.Patch(
                AccessTools.Method(typeof(ThingDef), nameof(ThingDef.SpecialDisplayStats)),
                postfix: new HarmonyMethod(typeof(Harmony_CelesMultiVerb), nameof(Postfix_SpecialDisplayStats))
            );
            // [事实] AzWeaponLib Patch ②：拦截 CreateVerbTargetCommand（private 方法，需传参类型）
            harmony.Patch(
                AccessTools.Method(typeof(VerbTracker), "CreateVerbTargetCommand",
                    new[] { typeof(Thing), typeof(Verb) }),
                postfix: new HarmonyMethod(typeof(Harmony_CelesMultiVerb), nameof(Postfix_CreateVerbTargetCommand))
            );
        }
        // Prefix 短路（2026-08-15 替代 Transpiler）：多武器时返回我们指定的 verb，否则走原 getter
        public static bool Prefix_PrimaryVerb(VerbTracker __instance, ref Verb __result)
        {
            Verb verb = GetMultiVerbPrimaryVerb(__instance);
            if (verb == null) return true;   // 走原逻辑
            __result = verb;
            return false;
        }
        private static Verb GetMultiVerbPrimaryVerb(VerbTracker tracker)
        {
            if (tracker.directOwner is CompEquippable ce)
            {
                var comp = ce.parent.TryGetComp<Celes_CompMultiVerb>();
                if (comp != null)
                {
                    var verbs = tracker.AllVerbs;
                    int idx = comp.verbIndex;
                    if (idx >= 0 && idx < verbs.Count)
                        return verbs[idx];
                }
            }
            return null;
        }
        // ── 1D: Stats Postfix ──
        public static IEnumerable<StatDrawEntry> Postfix_SpecialDisplayStats(
            IEnumerable<StatDrawEntry> entries, ThingDef __instance, StatRequest req)
        {
            List<StatDrawEntry> list = new List<StatDrawEntry>(entries);
            List<VerbProperties> verbs = __instance.Verbs;
            if (verbs == null || verbs.Count != 2)
                return list;
            Celes_CompProperties_MultiVerb compProps = __instance.GetCompProperties<Celes_CompProperties_MultiVerb>();
            if (compProps == null || compProps.verbInfos == null || compProps.verbInfos.Count < 2)
                return list;
            VerbProperties verbA = verbs[0];
            VerbProperties verbB = verbs[1];
            ProjectileProperties projA = verbA.defaultProjectile?.projectile;
            ProjectileProperties projB = verbB.defaultProjectile?.projectile;
            // [事实] ThingDef.md 第2103行: Pawn→PawnCombat, 其他→Weapon_Ranged
            StatCategoryDef cat = (__instance.category == ThingCategory.Pawn)
                ? StatCategoryDefOf.PawnCombat
                : StatCategoryDefOf.Weapon_Ranged;
            // ① 移除原版 Verb 相关条目
            list.RemoveAll(e => e.category == cat
                && verbPriorities.Contains(e.DisplayPriorityWithinCategory));
            // ② 计算所有属性值
            float warmupA = verbA.warmupTime;
            float warmupB = verbB.warmupTime;
            float dmgA = projA?.GetDamageAmount(req.Thing, null) ?? 0f;
            float dmgB = projB?.GetDamageAmount(req.Thing, null) ?? 0f;
            float extA = ExtraDamageTotal(projA);
            float extB = ExtraDamageTotal(projB);
            float armorA = projA?.GetArmorPenetration(req.Thing, null) ?? 0f;
            float armorB = projB?.GetArmorPenetration(req.Thing, null) ?? 0f;
            float spA = projA?.stoppingPower ?? 0f;
            float spB = projB?.stoppingPower ?? 0f;
            if (req.HasThing && req.Thing.TryGetComp(out CompUniqueWeapon uw))
            {
                spA += uw.TraitsListForReading.Sum(t => t.additionalStoppingPower);
                spB += uw.TraitsListForReading.Sum(t => t.additionalStoppingPower);
            }
            float rangeA = verbA.range;
            float rangeB = verbB.range;
            int burstA = verbA.burstShotCount;
            int burstB = verbB.burstShotCount;
            float rateA = burstA > 1
                ? (verbA.ticksBetweenBurstShots > 0
                    ? 60f / verbA.ticksBetweenBurstShots.TicksToSeconds()
                    : float.PositiveInfinity)
                : 0f;
            float rateB = burstB > 1
                ? (verbB.ticksBetweenBurstShots > 0
                    ? 60f / verbB.ticksBetweenBurstShots.TicksToSeconds()
                    : float.PositiveInfinity)
                : 0f;
            float bldA = projA?.damageDef?.buildingDamageFactor ?? 1f;
            float bldB = projB?.damageDef?.buildingDamageFactor ?? 1f;
            float bldPassA = projA?.damageDef?.buildingDamageFactorPassable ?? 1f;
            float bldPassB = projB?.damageDef?.buildingDamageFactorPassable ?? 1f;
            float bldImpA = projA?.damageDef?.buildingDamageFactorImpassable ?? 1f;
            float bldImpB = projB?.damageDef?.buildingDamageFactorImpassable ?? 1f;
                        // ③ 构建模式 A / B 的条目列表
            List<StatDrawEntry> entriesA = new List<StatDrawEntry>();
            List<StatDrawEntry> entriesB = new List<StatDrawEntry>();

            int priA = 5509;
            int priB = 5489;

            // 伤害 — 始终 2 份
            entriesA.Add(DamageEntry(cat, dmgA, projA, req, priA--));
            entriesB.Add(DamageEntry(cat, dmgB, projB, req, priB--));

            // 额外伤害 — 0/非0 规则
            if (extA > 0f || extB > 0f)
            {
                if (extA > 0f)
                {
                    var entryA = ExtraDamageEntry(cat, projA, extA, priA);
                    if (entryA != null) entriesA.Add(entryA);
                    priA--;
                }
                if (extB > 0f)
                {
                    var entryB = ExtraDamageEntry(cat, projB, extB, priB);
                    if (entryB != null) entriesB.Add(entryB);
                    priB--;
                }
            }

            // 建筑伤害 — 1.0/非 1.0 规则
            // [事实] 5410/5430/5420 各有独立的翻译 Key（ThingDef.md:2148-2158）
            if (!Mathf.Approximately(bldA, 1f) || !Mathf.Approximately(bldB, 1f))
            {
                if (!Mathf.Approximately(bldA, 1f))
                    entriesA.Add(BuildingDmgEntry(cat, "BuildingDamageFactor".Translate(),
                        "BuildingDamageFactorExplanation".Translate(), bldA, priA--));
                if (!Mathf.Approximately(bldB, 1f))
                    entriesB.Add(BuildingDmgEntry(cat, "BuildingDamageFactor".Translate(),
                        "BuildingDamageFactorExplanation".Translate(), bldB, priB--));
            }
            if (!Mathf.Approximately(bldPassA, 1f) || !Mathf.Approximately(bldPassB, 1f))
            {
                if (!Mathf.Approximately(bldPassA, 1f))
                    entriesA.Add(BuildingDmgEntry(cat, "BuildingDamageFactorPassable".Translate(),
                        "BuildingDamageFactorPassableExplanation".Translate(), bldPassA, priA--));
                if (!Mathf.Approximately(bldPassB, 1f))
                    entriesB.Add(BuildingDmgEntry(cat, "BuildingDamageFactorPassable".Translate(),
                        "BuildingDamageFactorPassableExplanation".Translate(), bldPassB, priB--));
            }
            if (!Mathf.Approximately(bldImpA, 1f) || !Mathf.Approximately(bldImpB, 1f))
            {
                if (!Mathf.Approximately(bldImpA, 1f))
                    entriesA.Add(BuildingDmgEntry(cat, "BuildingDamageFactorImpassable".Translate(),
                        "BuildingDamageFactorImpassableExplanation".Translate(), bldImpA, priA--));
                if (!Mathf.Approximately(bldImpB, 1f))
                    entriesB.Add(BuildingDmgEntry(cat, "BuildingDamageFactorImpassable".Translate(),
                        "BuildingDamageFactorImpassableExplanation".Translate(), bldImpB, priB--));
            }

            // 抑止能力 — 条件 1/2
            if (!Mathf.Approximately(spA, spB))
            {
                entriesA.Add(StoppingPowerEntry(cat, spA, req, priA--));
                entriesB.Add(StoppingPowerEntry(cat, spB, req, priB--));
            }

            // 护甲穿透 — 条件 1/2
            if (!Mathf.Approximately(armorA, armorB))
            {
                entriesA.Add(ArmorPenEntry(cat, projA, armorA, req, priA--));
                entriesB.Add(ArmorPenEntry(cat, projB, armorB, req, priB--));
            }

            // Burst 捆绑规则（射速在前、连射次数在后）
            if (burstA > 1 || burstB > 1)
            {
                if (burstA > 1)
                {
                    entriesA.Add(BurstRateEntry(cat, verbA, rateA, req, priA--));
                    entriesA.Add(BurstCountEntry(cat, verbA, burstA, req, priA--));
                }
                if (burstB > 1)
                {
                    entriesB.Add(BurstRateEntry(cat, verbB, rateB, req, priB--));
                    entriesB.Add(BurstCountEntry(cat, verbB, burstB, req, priB--));
                }
            }

            // 射程 — 条件 1/2
            if (!Mathf.Approximately(rangeA, rangeB))
            {
                entriesA.Add(RangeEntry(cat, rangeA, req, priA--));
                entriesB.Add(RangeEntry(cat, rangeB, req, priB--));
            }

            // 瞄准时间 — 条件 1/2
            if (!Mathf.Approximately(warmupA, warmupB))
            {
                entriesA.Add(WarmupEntry(cat, warmupA, req, priA--));
                entriesB.Add(WarmupEntry(cat, warmupB, req, priB--));
            }
            // ④ 拼接：模式 A 标题 → A 条目 → 模式 B 标题 → B 条目
            // [事实] A 标题 5510 > A 条目 5509~ > B 标题 5490 > B 条目 5489~
            if (entriesA.Count > 0 || entriesB.Count > 0)
            {
                list.Add(new StatDrawEntry(cat,
                    "----- " + compProps.verbInfos[0].defaultLabel + " -----",
                    "", compProps.verbInfos[0].defaultDesc, 5510));
                list.AddRange(entriesA);
                list.Add(new StatDrawEntry(cat,
                    "----- " + compProps.verbInfos[1].defaultLabel + " -----",
                    "", compProps.verbInfos[1].defaultDesc, 5490));
                list.AddRange(entriesB);
                list.Add(new StatDrawEntry(cat,
                    "----------",
                    "", "", 5389));
            }
            return list;
        }
        // ── 值计算辅助方法 ──
        private static float ExtraDamageTotal(ProjectileProperties proj)
        {
            var ext = proj?.damageDef?.GetModExtension<Celes_DamageExtension_ExtraDamages>();
            if (ext?.extraDamages == null) return 0f;
            float total = 0f;
            foreach (Celes_ExtraDamageEntry ed in ext.extraDamages)
                total += ed.amount * ed.chance;
            return total;
        }
        // ── StatDrawEntry 工厂方法 ──
        // [事实] 所有 description 构建逻辑参照 ThingDef.md 对应行号

        // [事实] ThingDef.md:2129-2133
        private static StatDrawEntry DamageEntry(StatCategoryDef cat, float val,
            ProjectileProperties proj, StatRequest req, int priority)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Stat_Thing_Damage_Desc".Translate());
            sb.AppendLine();
            float finalVal = proj?.GetDamageAmount(req.Thing, sb) ?? val;
            return new StatDrawEntry(cat, "Damage".Translate(), finalVal.ToString("F0"),
                sb.ToString(), priority);
        }

        // [事实] 复用功能三 BuildDescription 逻辑
        private static StatDrawEntry ExtraDamageEntry(StatCategoryDef cat,
            ProjectileProperties proj, float val, int priority)
        {
            var ext = proj?.damageDef?.GetModExtension<Celes_DamageExtension_ExtraDamages>();
            if (ext?.extraDamages == null)
                return null;
            string description = Celes_CompProperties_ExtraDamageStats.BuildDescription(ext.extraDamages);
            return new StatDrawEntry(cat, "Celes_Keyed_ExtraDamageStatLabel".Translate(),
                val.ToString("F0"), description, priority);
        }

        // [事实] ThingDef.md:2136-2143
        private static StatDrawEntry ArmorPenEntry(StatCategoryDef cat,
            ProjectileProperties proj, float val, StatRequest req, int priority)
        {
            StringBuilder sb = new StringBuilder();
            float finalVal = proj?.GetArmorPenetration(req.Thing, sb) ?? val;
            TaggedString desc = "ArmorPenetrationExplanation".Translate();
            if (sb.Length > 0)
                desc += "\n\n" + sb;
            return new StatDrawEntry(cat, "ArmorPenetration".Translate(),
                finalVal.ToStringPercent(), desc, priority);
        }

        // [事实] ThingDef.md:2180-2230
        private static StatDrawEntry StoppingPowerEntry(StatCategoryDef cat, float val,
            StatRequest req, int priority)
        {
            StringBuilder sb = new StringBuilder("StoppingPowerExplanation".Translate());
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("StatsReport_BaseValue".Translate() + ": " + val.ToString("F1"));
            if (req.HasThing && req.Thing.TryGetComp(out CompUniqueWeapon uw))
            {
                foreach (WeaponTraitDef t in uw.TraitsListForReading)
                {
                    if (!Mathf.Approximately(t.additionalStoppingPower, 0f))
                        sb.AppendLine("    " + t.LabelCap + ": " + t.additionalStoppingPower);
                }
            }
            sb.AppendLine();
            sb.AppendLine("StatsReport_FinalValue".Translate() + ": " + val.ToString("F1"));
            return new StatDrawEntry(cat, "StoppingPower".Translate(),
                val.ToString("F1"), sb.ToString(), priority);
        }

        // [事实] ThingDef.md:2242-2272
        private static StatDrawEntry RangeEntry(StatCategoryDef cat, float val,
            StatRequest req, int priority)
        {
            StringBuilder sb = new StringBuilder("Stat_Thing_Weapon_Range_Desc".Translate());
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("StatsReport_BaseValue".Translate() + ": " + val.ToString("F0"));
            if (req.HasThing)
            {
                float mult = req.Thing.GetStatValue(StatDefOf.RangedWeapon_RangeMultiplier);
                val *= mult;
                if (!Mathf.Approximately(mult, 1f))
                {
                    sb.AppendLine();
                    sb.AppendLine("Stat_Thing_Weapon_Range_Multiplier".Translate() + ": x" + mult.ToStringPercent());
                    sb.Append(StatUtility.GetOffsetsAndFactorsFor(StatDefOf.RangedWeapon_RangeMultiplier, req.Thing));
                }
            }
            sb.AppendLine();
            sb.AppendLine("StatsReport_FinalValue".Translate() + ": " + val.ToString("F0"));
            return new StatDrawEntry(cat, "Range".Translate(),
                val.ToString("F0"), sb.ToString(), priority);
        }

        // [事实] ThingDef.md:2105-2124
        private static StatDrawEntry WarmupEntry(StatCategoryDef cat, float val,
            StatRequest req, int priority)
        {
            StringBuilder sb = new StringBuilder("Stat_Thing_Weapon_RangedWarmupTime_Desc".Translate());
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("StatsReport_BaseValue".Translate() + ": " + val.ToString("0.##")
                + " " + "LetterSecond".Translate());
            if (req.HasThing)
            {
                float mult = req.Thing.GetStatValue(StatDefOf.RangedWeapon_WarmupMultiplier);
                val *= mult;
                if (!Mathf.Approximately(mult, 1f))
                {
                    sb.AppendLine();
                    sb.AppendLine("Stat_Thing_Weapon_WarmupTime_Multiplier".Translate() + ": x" + mult.ToStringPercent());
                    sb.Append(StatUtility.GetOffsetsAndFactorsFor(StatDefOf.RangedWeapon_WarmupMultiplier, req.Thing));
                }
            }
            sb.AppendLine();
            sb.AppendLine("StatsReport_FinalValue".Translate() + ": " + val.ToString("0.##")
                + " " + "LetterSecond".Translate());
            return new StatDrawEntry(cat, "RangedWarmupTime".Translate(),
                val.ToString("0.##") + " " + "LetterSecond".Translate(), sb.ToString(), priority);
        }

        // [事实] ThingDef.md:2170-2225
        private static StatDrawEntry BurstCountEntry(StatCategoryDef cat, VerbProperties verb,
            int val, StatRequest req, int priority)
        {
            StringBuilder sb = new StringBuilder("Stat_Thing_Weapon_BurstShotCount_Desc".Translate());
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("StatsReport_BaseValue".Translate() + ": " + val.ToString());
            int finalVal = val;
            if (req.HasThing && req.Thing.TryGetComp(out CompUniqueWeapon uw))
            {
                foreach (WeaponTraitDef t in uw.TraitsListForReading)
                {
                    if (!Mathf.Approximately(t.burstShotCountMultiplier, 1f))
                    {
                        finalVal = (int)(finalVal * t.burstShotCountMultiplier);
                        sb.AppendLine("    " + t.LabelCap + ": " + (t.burstShotCountMultiplier - 1f).ToStringPercent());
                    }
                }
            }
            sb.AppendLine();
            sb.AppendLine("StatsReport_FinalValue".Translate() + ": " + Mathf.CeilToInt(finalVal).ToString());
            return new StatDrawEntry(cat, "BurstShotCount".Translate(),
                Mathf.CeilToInt(finalVal).ToString(), sb.ToString(), priority);
        }

        // [事实] ThingDef.md:2175-2228
        private static StatDrawEntry BurstRateEntry(StatCategoryDef cat, VerbProperties verb,
            float val, StatRequest req, int priority)
        {
            StringBuilder sb = new StringBuilder("Stat_Thing_Weapon_BurstShotFireRate_Desc".Translate());
            sb.AppendLine();
            sb.AppendLine();
            string valStrA = float.IsInfinity(val) ? "Infinity" : val.ToString("0.##");
            sb.AppendLine("StatsReport_BaseValue".Translate() + ": " + valStrA + " rpm");
            float finalVal = val;
            if (req.HasThing && req.Thing.TryGetComp(out CompUniqueWeapon uw))
            {
                foreach (WeaponTraitDef t in uw.TraitsListForReading)
                {
                    if (!Mathf.Approximately(t.burstShotSpeedMultiplier, 1f))
                    {
                        finalVal /= t.burstShotSpeedMultiplier;
                        sb.AppendLine("    " + t.LabelCap + ": " + (1f / t.burstShotSpeedMultiplier).ToStringPercent());
                    }
                }
            }
            string valStrB = float.IsInfinity(finalVal) ? "Infinity" : finalVal.ToString("0.##");
            sb.AppendLine();
            sb.AppendLine("StatsReport_FinalValue".Translate() + ": " + valStrB + " rpm");
            return new StatDrawEntry(cat, "BurstShotFireRate".Translate(),
                valStrB + " rpm", sb.ToString(), priority);
        }

        // [事实] ThingDef.md:2148-2158
        private static StatDrawEntry BuildingDmgEntry(StatCategoryDef cat, string label,
            string explanationKey, float val, int priority)
        {
            return new StatDrawEntry(cat, label,
                val.ToStringPercent(), explanationKey.Translate(), priority);
        }
        
        // ── 1E: CreateVerbTargetCommand Postfix ──
        // [事实] 参照 AzWeaponLib Patch_VerbTracker.Postfix_CreateVerbTargetCommand
        public static void Postfix_CreateVerbTargetCommand(
            Thing ownerThing, Verb verb, VerbTracker __instance, ref Command_VerbTarget __result)
        {
            if (!(__instance.directOwner is CompEquippable ce))
                return;
            var comp = ce.parent.TryGetComp<Celes_CompMultiVerb>();
            if (comp == null)
                return;
            if (verb != __instance.AllVerbs[comp.verbIndex])
            {
                __result = new Command_VerbTargetInvisible
                {
                    defaultDesc = ownerThing.LabelCap + ": " + ownerThing.def.description.CapitalizeFirst(),
                    verb = verb,
                    drawRadius = false
                };
            }
        }
        // [事实] 参照 AzWeaponLib Command_VerbTargetInvisible
        private class Command_VerbTargetInvisible : Command_VerbTarget
        {
            public override bool Visible => false;
            public override bool GroupsWith(Gizmo other) => false;
            public override void GizmoUpdateOnMouseover() { }
        }
    }
}