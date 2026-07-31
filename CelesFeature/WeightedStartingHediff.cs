using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace CelesFeature
{
    
    public class WeightedStartingHediff
    {
        public HediffDef def;
        public float? severity;   // null = 使用 def 自带的 initialSeverity
        public float weight = 1f; // 权重，越大越容易被选中。<= 0 视为无效，自动排除
        public string part;       // BodyPartDef 的 defName，如 "Brain" / "LeftEye"。留空 = 全身

        public bool HasHediff(Pawn pawn)
        {
            return pawn.health.hediffSet.HasHediff(def);
        }
    }
    
    public class StartingHediffWeightedGroup
    {
        public List<WeightedStartingHediff> entries;
    }
    
    public class PawnKindStartingHediffExtension : DefModExtension
    {
        public List<StartingHediffWeightedGroup> startingHediffGroups;
    }
    
    [HarmonyPatch(typeof(PawnGenerator), "GenerateInitialHediffs")]
    public static class Harmony_GenerateInitialHediffs_Postfix
    {
        private static void Postfix(Pawn pawn, PawnGenerationRequest request) // [事实] 目标方法签名来自反编译
        {
            if (request.AllowedDevelopmentalStages.Newborn())
                return;

            PawnKindStartingHediffExtension ext =
                pawn.kindDef.GetModExtension<PawnKindStartingHediffExtension>();
            if (ext?.startingHediffGroups.NullOrEmpty() ?? true)
                return;

            foreach (StartingHediffWeightedGroup group in ext.startingHediffGroups)
            {
                if (group?.entries.NullOrEmpty() ?? true)
                    continue;

                // 过滤：排除 weight <= 0 + 排除已存在的 Hediff
                List<WeightedStartingHediff> candidates = group.entries
                    .Where(e => e.weight > 0f && !e.HasHediff(pawn))
                    .ToList();

                if (candidates.Count == 0)
                    continue; // 本组全部已存在或无效，静默跳过此组

                // 按权重随机选一
                WeightedStartingHediff chosen = candidates.RandomElementByWeight(e => e.weight);

                // 解析部位
                BodyPartRecord partRecord = ResolvePart(pawn, chosen);

                // 施加
                Hediff hediff = HediffMaker.MakeHediff(chosen.def, pawn, partRecord);
                if (chosen.severity.HasValue)
                {
                    hediff.Severity = chosen.severity.Value;
                }
                pawn.health.AddHediff(hediff, partRecord);
            }
        }

        private static BodyPartRecord ResolvePart(Pawn pawn, WeightedStartingHediff entry)
        {
            if (entry.part.NullOrEmpty())
                return null;

            BodyPartDef partDef = DefDatabase<BodyPartDef>.GetNamed(entry.part);
            if (partDef == null)
            {
                Log.Warning($"[CelesFeature] 找不到 BodyPartDef '{entry.part}'（Hediff={entry.def.defName}），回退为全身施加。");
                return null;
            }

            List<BodyPartRecord> parts = pawn.RaceProps.body
                .GetPartsWithDef(partDef)
                ?.Where(p => !pawn.health.hediffSet.PartIsMissing(p))
                .ToList();

            if (parts == null || parts.Count == 0)
            {
                Log.Warning($"[CelesFeature] {pawn} 无可用部位 '{entry.part}'（Hediff={entry.def.defName}），回退为全身施加。");
                return null;
            }

            return parts.RandomElement();
        }
    }
    
}