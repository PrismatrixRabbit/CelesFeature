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
    }

    // 开放等级顺序配置（Phase 2 遍历判定用，首项强制兜底）
    public class CelesFD_UnlockLevelConfigDef : Def
    {
        public List<string> unlockLevel;       // 依次填入 CelesFD_UnlockLevelDef defName（从低到高）
    }
}
