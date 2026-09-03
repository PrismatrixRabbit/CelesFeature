using System.Linq;
using RimWorld;
using Verse;

namespace CelesFeature
{
    // ═══ 开局任务链：剧本分组判定（2026-08-25） ═══
    // 原版剧本 = ScenarioDef（defName 化）——运行时 Find.Scenario 是某 ScenarioDef.scenario 实例（Root_Play.cs:91 实证）
    // 原版无"当前剧本 defName"现成 API → DefDatabase 反查（ScenarioDef.cs 实证：scenario 字段 + PostLoad 设 Category=FromDef）
    public static class CelesFD_ScenarioUtility
    {
        // 分组：0=else 1=经典(迫降) 2=失落的部落 3=探险家组 4=星铃（Celes_Scenarios）
        public const int GroupOther = 0;
        public const int GroupCrashlanded = 1;
        public const int GroupTribal = 2;
        public const int GroupExplorer = 3;
        public const int GroupCelestia = 4;

        private static string cachedDefName;   // 判定一次缓存（剧本开局固定，无需重复遍历）

        public static string GetCurrentDefName()
        {
            if (cachedDefName != null || Find.Scenario == null) return cachedDefName;
            ScenarioDef sd = DefDatabase<ScenarioDef>.AllDefsListForReading
                .FirstOrDefault(x => x.scenario == Find.Scenario);
            cachedDefName = sd?.defName;   // 缓存 null（CustomLocal/Workshop 剧本）——无 defName 判定
            return cachedDefName;
        }

        public static int GetScenarioGroup()
        {
            switch (GetCurrentDefName())
            {
                case "Crashlanded": return GroupCrashlanded;
                case "LostTribe": return GroupTribal;
                case "TheRichExplorer":
                case "NakedBrutality":
                case "Mechanitor": return GroupExplorer;
                case "Celes_Scenarios": return GroupCelestia;
                default: return GroupOther;
            }
        }

        // 剧本分组 → 道歉信文本 IncidentDef（CelesFD_ApologyLetter=else 默认 / CelesFD_ApologyText_*——原版 letterText 字段承载，defInjected 翻译）
        public static string ApologyTextDefNameFor(int group)
        {
            switch (group)
            {
                case GroupCrashlanded: return "CelesFD_ApologyText_Classic";
                case GroupTribal: return "CelesFD_ApologyText_Tribal";
                case GroupExplorer: return "CelesFD_ApologyText_Explorer";
                case GroupCelestia: return "CelesFD_ApologyText_Celestia";
                default: return "CelesFD_ApologyLetter";
            }
        }
    }
}
