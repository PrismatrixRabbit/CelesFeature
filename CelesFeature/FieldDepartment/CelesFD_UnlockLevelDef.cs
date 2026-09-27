using System.Collections.Generic;
using Verse;

namespace CelesFeature
{
    // 槽位嵌套配置类（v4.1 定稿：类别×方向，收购/出售独立互不挤占）
    public class CelesFD_SlotConfig
    {
        public int buy;     // 收购槽位
        public int sell;    // 出售槽位
    }

    // 开放等级 Def（M4 §3.5；description 直接使用 Def 基类字段——避免 CS0108 遮蔽）
    public class CelesFD_UnlockLevelDef : Def
    {
        public string title;
        public int fameRequire;
        public int tradeRequire;
        public bool isBase;                    // 基础等级（最低保底，Phase 2 首项兜底）
        public CelesFD_SlotConfig openSlots;       // 开放市场槽位（buy/sell）
        public CelesFD_SlotConfig internalSlots;   // 内部市场槽位
        public CelesFD_SlotConfig preciousSlots;   // 珍贵固有槽位
        public float marketPreciousChance;     // 珍贵随机概率（每轮 -0.35，挤占对应方向开放槽）
        public int maxTradeOrder;              // 最大承接订单量（M3.5 用）
        public int weaponQuotaPerQuadrum = 4; // 每象武备数量上限（W-3 N3——零级 4 / 一级 8；XML 可调）
        public bool sendLevelUpMessage = false;     // 首次到达该级是否发升级信（等级化适配 2026-09-27；默认 true=漏配安全方向：发送+回退通用文案）
        public string levelUpMessageTitle;         // 升级信标题（空 → 回退 CelesFD_Keyed_LevelUpTitle）
        public string levelUpMessageDesc;          // 升级信内容（空 → 回退 CelesFD_Keyed_LevelUpDesc）
    }

    // 开放等级顺序配置（Phase 2 遍历判定用，isBase 项兜底——2026-09-27 接线）
    public class CelesFD_UnlockLevelConfigDef : Def
    {
        public List<string> unlockLevel;       // 依次填入 CelesFD_UnlockLevelDef defName（从低到高）

        // 兜底接线配套校验（2026-09-27）：Phase2Level 从高到低扫描、命中 isBase 即兜底——
        // 须恰好一个 isBase 且位于首位，否则中途错位兜底（其下等级永不匹配）
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;
            if (unlockLevel == null || unlockLevel.Count == 0)
            {
                yield return "unlockLevel 列表为空";
                yield break;
            }
            int baseCount = 0, baseIndex = -1;
            for (int i = 0; i < unlockLevel.Count; i++)
            {
                CelesFD_UnlockLevelDef def = DefDatabase<CelesFD_UnlockLevelDef>.GetNamedSilentFail(unlockLevel[i]);
                if (def == null)
                {
                    yield return $"unlockLevel[{i}] = {unlockLevel[i]} 无法解析为 CelesFD_UnlockLevelDef";
                    continue;
                }
                if (def.isBase) { baseCount++; baseIndex = i; }
            }
            if (baseCount == 0) yield return "缺少 isBase=true 的保底等级（Phase2Level 兜底依赖）";
            else if (baseCount > 1) yield return $"存在 {baseCount} 个 isBase=true 等级（应恰好 1 个）";
            else if (baseIndex != 0) yield return $"isBase 等级 {unlockLevel[baseIndex]} 须位于列表首位（当前索引 {baseIndex}——从高到低扫描会中途错位兜底）";
        }
    }
}
