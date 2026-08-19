using System.Collections.Generic;
using Verse;

namespace CelesFeature
{
    // 出售物流费用计算（§5.4 运费公式 + §5.6 税金；M5c 显示与 M7 扣款共用）
    public static class CelesFD_ShippingUtility
    {
        private const float ShippingFlatMin = 50f;
        private const float ShippingFlatMax = 2000f;
        private const float BoundaryMid = 7702.76f;   // 区间上界定稿（v4.2，原 7702.46 不一致消除）

        // 运费公式（§5.4 定稿）：V = 购物车白银总价；W = 购物车总重量
        //   V+5W <= 250 → 50；250 < V+5W < 7702.76 → V<250 ? 50 : 50+(V-250)×(0.015+2100×(V+5W)/((V+5W)²+2500²))；>= 7702.76 → 2000
        public static float CalcShippingCost(float totalValue, float totalMass)
        {
            if (totalValue <= 0f && totalMass <= 0f) return 0f;   // 无项目 → 无运费（用户裁决：空车显示 0，非公式最低档 50）
            float vw = totalValue + 5f * totalMass;
            if (vw <= 250f) return ShippingFlatMin;
            if (vw >= BoundaryMid) return ShippingFlatMax;
            if (totalValue < 250f) return ShippingFlatMin;
            return ShippingFlatMin + (totalValue - 250f) * (0.015f + 2100f * vw / (vw * vw + 2500f * 2500f));
        }

        // 购物车白银总价（Σ MarketValue × amount——运费 V 输入，计算值不显示，§6.4 v4.2）
        public static float CalcTotalValue(List<CelesFD_Order> cart)
        {
            float v = 0f;
            if (cart == null) return v;
            foreach (CelesFD_Order o in cart)
            {
                ThingDef td = o.FirstThingDef;   // v2：出售单候选集价值按字母序首物计算（与 icon 同规则；M7 发货物裁决挂账）
                if (td != null) v += td.BaseMarketValue * o.amount;
            }
            return v;
        }

        // 购物车总重量（Σ Mass × amount——运费 W 输入 + UI 显示字段，§6.4）
        public static float CalcTotalMass(List<CelesFD_Order> cart)
        {
            float w = 0f;
            if (cart == null) return w;
            foreach (CelesFD_Order o in cart)
            {
                ThingDef td = o.FirstThingDef;   // v2：同 CalcTotalValue 规则
                if (td != null) w += td.BaseMass * o.amount;
            }
            return w;
        }

        // 税金（§5.6：固定极低比率象征性扣除——比率占位，实现期 Def 可配置）
        // 出境税 0.1% / 管理税 0.5% / 边缘世界报务税 0.3% / 交易系统使用税 0.1% —— 合计 1.0%
        public const float TotalTaxRate = 0.01f;
        public static readonly (string key, float rate)[] TaxItems =
        {
            ("CelesFD_Keyed_TaxExit", 0.001f),
            ("CelesFD_Keyed_TaxAdmin", 0.005f),
            ("CelesFD_Keyed_TaxRadio", 0.003f),
            ("CelesFD_Keyed_TaxSystem", 0.001f)
        };

        public static float CalcTax(float totalCredit)
            => totalCredit * TotalTaxRate;
    }
}
