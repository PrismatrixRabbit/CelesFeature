using System.Collections.Generic;
using Verse;

namespace CelesFeature
{
    // ═══ R-2：7 日窗口追踪（差值结算数据——边缘走出者的回归/损失判定载体）═══
    // 生命周期：初始结算时创建 → 事件驱动差值（仅跨节点） → deadline 静默销毁（不发 letter）
    // lastTierIndex 追踪阶梯函数当前位置——每次差值结算后更新
    public class CelesFD_PendingSettlement : IExposable
    {
        public string supportDefName;
        public List<string> pendingPawnIds = new List<string>();   // 走边缘的 pawn ID
        public List<string> returnedIds = new List<string>();       // 已回归的 pawn ID（翻案——计入存活权重）
        public float baseline;                                     // 完整度分母快照
        public float teleportedWeight;                             // 即时结算分子快照（传送成功者）
        public Dictionary<string, float> pawnWeights = new Dictionary<string, float>();   // per-pawn 权重（差值重算用）
        public int lastTierIndex;                                  // 阶梯函数当前位置（跨节点判定基准）
        public int lastFameApplied;                                // 累计已结算声望（差值计算用）
        public int lastCreditApplied;                              // 累计已结算信用额（差值计算用）
        public long deadlineTick;                                  // quest 结束 + 7×60000
        public int recallRefundCredit;                             // 提前召回退款（letter 末行——0=非召回）

        public CelesFD_SupportDef Def =>
            supportDefName.NullOrEmpty() ? null : DefDatabase<CelesFD_SupportDef>.GetNamedSilentFail(supportDefName);

        // 当前完整度 = (已传送权重 + 边缘已回归权重) / baseline
        public float CurrentCompleteness(List<string> returnedIds)
        {
            if (baseline <= 0f) return 0f;
            float sum = teleportedWeight;
            foreach (string id in returnedIds)
                if (pawnWeights.TryGetValue(id, out float w)) sum += w;
            return sum / baseline;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref supportDefName, "supportDefName", null);
            Scribe_Collections.Look(ref pendingPawnIds, "pendingPawnIds", LookMode.Value);
            Scribe_Collections.Look(ref returnedIds, "returnedIds", LookMode.Value);
            Scribe_Values.Look(ref baseline, "baseline", 0f);
            Scribe_Values.Look(ref teleportedWeight, "teleportedWeight", 0f);
            Scribe_Collections.Look(ref pawnWeights, "pawnWeights", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref lastTierIndex, "lastTierIndex", 0);
            Scribe_Values.Look(ref lastFameApplied, "lastFameApplied", 0);
            Scribe_Values.Look(ref lastCreditApplied, "lastCreditApplied", 0);
            Scribe_Values.Look(ref deadlineTick, "deadlineTick", 0L);
            Scribe_Values.Look(ref recallRefundCredit, "recallRefundCredit", 0);
        }
    }
}
