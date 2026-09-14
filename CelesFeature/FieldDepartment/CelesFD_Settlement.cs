using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // ═══ R-2：结算体系（从 GameComponent 分离——避免大文件 sed 损坏）═══
    // SettlePersonnel：初始结算（SupportLeave 调用）
    // DifferentialSettleIfCrossed：差值结算（Harmony 事件触发——仅跨节点）
    // TickPendingSettlements：7 日窗口轮询（deadline 静默销毁）
    // SendSettlementLetter / SendUpdateLetter：letter 发送
    public static class CelesFD_Settlement
    {
        // 档位判定（严格大于——从高到低首个 C > threshold；全不中→兜底档）
        public static int ResolveTierIndex(CelesFD_SupportDef def, float completeness)
        {
            for (int i = 0; i < def.completenessTiers.Count - 1; i++)
                if (completeness > def.completenessTiers[i].threshold) return i;
            return def.completenessTiers.Count - 1;
        }

        // 阶段 label（含端点特例）
        public static string StageLabel(CelesFD_SupportDef def, float completeness)
        {
            if (completeness >= 1f) return "CelesFD_Keyed_StageIntact".Translate();
            if (completeness <= 0f) return "CelesFD_Keyed_StageMissing".Translate();
            switch (ResolveTierIndex(def, completeness))
            {
                case 0: return "CelesFD_Keyed_Stage0".Translate();
                case 1: return "CelesFD_Keyed_Stage1".Translate();
                case 2: return "CelesFD_Keyed_Stage2".Translate();
                default: return "CelesFD_Keyed_Stage3".Translate();
            }
        }

        private static string StageDesc(float completeness)
        {
            if (completeness >= 1f) return "";
            if (completeness <= 0f) return "CelesFD_Keyed_StageDescMissing".Translate();
            if (completeness > 0.8f) return "CelesFD_Keyed_StageDesc0".Translate();
            if (completeness > 0.6f) return "CelesFD_Keyed_StageDesc1".Translate();
            if (completeness > 0.4f) return "CelesFD_Keyed_StageDesc2".Translate();
            return "CelesFD_Keyed_StageDesc3".Translate();
        }

        // 初始结算（SupportLeave 调用——含 letter 发送；isNoTarget = 无降落点结算的开场句变体）
        public static void SettlePersonnel(CelesFD_GameComponent gc, CelesFD_SupportDef def,
            float teleportedWeight, List<Pawn> edgePawns,
            Dictionary<string, float> pawnWeights, float baseline,
            bool isEarlyRecall, int recallRefundCredit, bool isWiped = false, bool isNoTarget = false)
        {
            // Bug B 修复：初始结算只计已确认回家的传送者——edge pawn 在 7 日窗口内逐步翻案
            // （此前把 edgeWeight 计入分子导致"未归者也被算作完整返回"）
            float completeness = teleportedWeight / Mathf.Max(baseline, 1f);
            int tierIndex = ResolveTierIndex(def, completeness);
            CelesFD_CompletenessTier tier = def.completenessTiers[tierIndex];
            int fame = tier.fameOutput;
            gc.ModifyFame(fame);
            int deduction = Mathf.RoundToInt(def.insuranceCost * tier.multiplier);
            int refund = Mathf.Max(0, def.insuranceCost - deduction);
            int penalty = Mathf.Max(0, deduction - def.insuranceCost);
            if (refund > 0) gc.ModifyCredit(refund);
            if (penalty > 0) gc.ModifyCredit(-penalty);
            SendSettlementLetter(def, completeness, fame, refund, penalty, isEarlyRecall, recallRefundCredit, isWiped, isNoTarget);
            Log.Message("[CelesFD] Initial settle: " + def.defName + " completeness=" + completeness.ToString("F3")
                + " tier=" + tierIndex + " fame=" + fame + " refund=" + refund + " penalty=" + penalty);
            if (edgePawns.Count > 0)
            {
                var ps = new CelesFD_PendingSettlement
                {
                    supportDefName = def.defName,
                    baseline = baseline,
                    teleportedWeight = teleportedWeight,
                    pawnWeights = new Dictionary<string, float>(pawnWeights),
                    lastTierIndex = tierIndex,
                    lastFameApplied = fame,
                    lastCreditApplied = refund - penalty,
                    deadlineTick = Find.TickManager.TicksGame + 7L * 60000L,
                    recallRefundCredit = recallRefundCredit
                };
                foreach (Pawn ep in edgePawns)
                    ps.pendingPawnIds.Add(ep.thingIDNumber.ToString());
                gc.PendingSettlements.Add(ps);
            }
        }

        // 差值结算（Harmony 事件触发——仅跨节点才回馈+letter；returnee=null 为静默）
        // oldC / newC = 回归前后完整度（Letter 3 精确阶段 label 数据源——修7）
        public static void DifferentialSettleIfCrossed(CelesFD_GameComponent gc, CelesFD_PendingSettlement ps,
            string returneeLabel, float oldCompleteness, float newCompleteness)
        {
            CelesFD_SupportDef def = ps.Def;
            if (def == null) { gc.PendingSettlements.Remove(ps); return; }
            int newTierIndex = ResolveTierIndex(def, newCompleteness);
            if (newTierIndex == ps.lastTierIndex)
            {
                if (returneeLabel != null) SendUpdateLetter(def, ps.returnedIds.Count, def, oldCompleteness, newCompleteness, 0, 0, false);
                return;
            }
            CelesFD_CompletenessTier newTier = def.completenessTiers[newTierIndex];
            CelesFD_CompletenessTier oldTier = def.completenessTiers[ps.lastTierIndex];
            int fameDiff = newTier.fameOutput - oldTier.fameOutput;
            int oldDed = Mathf.RoundToInt(def.insuranceCost * oldTier.multiplier);
            int newDed = Mathf.RoundToInt(def.insuranceCost * newTier.multiplier);
            int creditDiff = (Mathf.Max(0, def.insuranceCost - newDed) - Mathf.Max(0, def.insuranceCost - oldDed))
                           - (Mathf.Max(0, newDed - def.insuranceCost) - Mathf.Max(0, oldDed - def.insuranceCost));
            if (fameDiff != 0) gc.ModifyFame(fameDiff);
            if (creditDiff != 0) gc.ModifyCredit(creditDiff);
            if (returneeLabel != null)
                SendUpdateLetter(def, ps.returnedIds.Count, def, oldCompleteness, newCompleteness, fameDiff, creditDiff, true);
            ps.lastTierIndex = newTierIndex;
            ps.lastFameApplied += fameDiff;
            ps.lastCreditApplied += creditDiff;
            Log.Message("[CelesFD] Diff settle: " + def.defName + " completeness " + oldCompleteness.ToString("F3") + "→" + newCompleteness.ToString("F3")
                + " tier→" + newTierIndex + " fame_diff=" + fameDiff + " credit_diff=" + creditDiff);
        }

        // 7 日窗口轮询
        public static void TickPendingSettlements(CelesFD_GameComponent gc)
        {
            long now = Find.TickManager.TicksGame;
            for (int i = gc.PendingSettlements.Count - 1; i >= 0; i--)
            {
                CelesFD_PendingSettlement ps = gc.PendingSettlements[i];
                if (now < ps.deadlineTick) continue;
                CelesFD_SupportDef def = ps.Def;
                if (def != null)
                {
                    float completeness = ps.CurrentCompleteness(ps.returnedIds);
                    int finalTier = ResolveTierIndex(def, completeness);
                    if (finalTier != ps.lastTierIndex)
                        DifferentialSettleIfCrossed(gc, ps, null, 0f, completeness);   // 静默——oldC 仅 letter 用，此处不 letter
                }
                gc.PendingSettlements.RemoveAt(i);
                Log.Message("[CelesFD] PendingSettlement expired: " + ps.supportDefName);
            }
        }

        // ═══ Letter 发送 ═══

        private static void SendSettlementLetter(CelesFD_SupportDef def, float completeness,
            int fame, int refund, int penalty, bool isEarlyRecall, int recallRefund, bool isWiped = false, bool isNoTarget = false)
        {
            var sb = new System.Text.StringBuilder();
            if (isWiped)
                sb.AppendLine("CelesFD_Keyed_SettleWiped".Translate(def.LabelCap));
            else if (isNoTarget)   // 无降落点开场句须在 isEarlyRecall 前判（该场景两标志同为 true）
                sb.AppendLine("CelesFD_Keyed_SettleNoTarget".Translate(def.LabelCap));
            else if (isEarlyRecall)
                sb.AppendLine("CelesFD_Keyed_SettleRecalled".Translate(def.LabelCap));
            else
                sb.AppendLine("CelesFD_Keyed_SettleExpired".Translate(def.LabelCap));
            sb.AppendLine("CelesFD_Keyed_SettleStatus".Translate(StageLabel(def, completeness)));
            string desc = StageDesc(completeness);
            if (!desc.NullOrEmpty()) sb.AppendLine(desc);
            if (fame > 0) sb.Append("CelesFD_Keyed_SettleFamePos".Translate(fame));
            else if (fame < 0) sb.Append("CelesFD_Keyed_SettleFameNeg".Translate(-fame));
            if (penalty > 0) sb.AppendLine("CelesFD_Keyed_SettleInsNeg".Translate(penalty));
            else if (refund > 0) sb.AppendLine("CelesFD_Keyed_SettleInsPos".Translate(refund));
            else sb.AppendLine("CelesFD_Keyed_SettleInsFull".Translate());
            if (isEarlyRecall && recallRefund > 0)
                sb.AppendLine("CelesFD_Keyed_SettleRecallRefund".Translate(recallRefund));
            Find.LetterStack.ReceiveLetter(
                def.LabelCap + " " + "CelesFD_Keyed_SettleBriefTitle".Translate(),
                sb.ToString().TrimEnd('\n'), LetterDefOf.NeutralEvent);
        }

        private static void SendUpdateLetter(CelesFD_SupportDef def, int returnCount,
            CelesFD_SupportDef defForLabel, float oldCompleteness, float newCompleteness,
            int fameDiff, int creditDiff, bool tierCrossed)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("CelesFD_Keyed_UpdateReturn".Translate(def.LabelCap, returnCount));
            if (tierCrossed)
            {
                sb.AppendLine();
                sb.Append("CelesFD_Keyed_UpdateTier".Translate(
                    StageLabel(defForLabel, oldCompleteness),
                    StageLabel(defForLabel, newCompleteness)));
                sb.AppendLine();
                sb.Append("CelesFD_Keyed_UpdateSettle".Translate(fameDiff, creditDiff));
            }
            Find.LetterStack.ReceiveLetter(
                def.LabelCap + " " + "CelesFD_Keyed_UpdateTitle".Translate(),
                sb.ToString().TrimEnd('\n'), LetterDefOf.NeutralEvent);
        }
    }
}
