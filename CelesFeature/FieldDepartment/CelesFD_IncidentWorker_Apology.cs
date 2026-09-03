using RimWorld;
using Verse;

namespace CelesFeature
{
    // ═══ 道歉信事件（开局任务链事件 2，2026-08-25） ═══
    // 触发方 = GameComponent 3700 比较式定时（非叙事者随机；baseChance=0 防随机池）
    // 链路：剧本差分 letter（IncidentDef.letterText 承载——原版字段 [MustTranslate] + defInjected 翻译）
    //      → 直接解锁科技（FinishProject 静默；科技无前置 → 只完成本科技，ResearchManager.cs:405-414 实证）
    //      → 置位状态（对话树根树切换 + 2 象过期起算）
    public class CelesFD_IncidentWorker_Apology : IncidentWorker
    {
        public const string DecryptFreqProjectDefName = "Celes_CelestiaFreqDecrypt";

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            // 1. 剧本分组 → 文本 IncidentDef（CelesFD_ApologyLetter=else / CelesFD_ApologyText_Classic/_Tribal/_Explorer/_Celestia）
            int group = CelesFD_ScenarioUtility.GetScenarioGroup();
            string textDefName = CelesFD_ScenarioUtility.ApologyTextDefNameFor(group);
            IncidentDef textDef = DefDatabase<IncidentDef>.GetNamedSilentFail(textDefName);
            string label = textDef?.letterLabel ?? def.letterLabel;
            string text = textDef?.letterText ?? def.letterText;
            if (text.NullOrEmpty())
            {
                Log.Error($"[CelesFD] Apology letter text missing on '{textDefName}'");
                return false;
            }

            // 2. letter：无转至重载（LetterStack.cs:50 实证——LetterMaker.MakeLetter 无 lookTargets；道歉信无地图目标）
            Find.LetterStack.ReceiveLetter(label, text, def.letterDef);

            // 3. 直接完成科技（静默 false/false——道歉信 letter 已承载说明，避免双弹窗；原版 ScenPart_StartingResearch.cs:57 同款）
            //    科技无前置 → FinishProject 递归链为空，仅完成本科技（ResearchManager.cs:405-414 实证）
            ResearchProjectDef proj = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(DecryptFreqProjectDefName);
            if (proj != null && !proj.IsFinished)
                Find.ResearchManager.FinishProject(proj, doCompletionDialog: false, researcher: null, doCompletionLetter: false);

            // 4. 置位状态（对话树根树切换 + 2 象过期起算；剧本分组供 B 树差分）
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc != null)
            {
                gc.ApologyTriggered = true;
                gc.ApologyTriggerTick = Find.TickManager.TicksGame;
                gc.ApologyScenario = group;
            }
            Log.Message($"[CelesFD] Apology letter sent (scenario group {group}, research finished)");
            return true;
        }
    }
}
