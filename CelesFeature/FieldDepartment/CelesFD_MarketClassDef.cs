using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Verse;

namespace CelesFeature
{
    // 类别轴：风险/收益递增（条带色：开灰/内蓝/珍金/彩粉/突红）
    public enum CelesFD_MarketCategory
    {
        Open,        // 开放市场
        Internal,    // 内部市场
        Precious,    // 珍贵
        EasterEgg,   // 彩蛋
        Urgent       // 突发（模块 D，预留）
    }

    // 形态轴（v4.5：无合规校验，纯 XML 填写自由——设计层与视觉区分，钢铁也可作散货）
    public enum CelesFD_MarketForm
    {
        Scattered,   // 散货（角标：左上三角）
        Bulk         // 批发（角标：左上三角）
    }

    // 订单项模板：一个 entry = 一份订单的候选抽取项（v4.4：保底每抽样 entry 生成一份独立订单）
    public class CelesFD_MarketEntry
    {
        public ThingFilter filter;                 // 物品源：原版 ThingFilter 内嵌（thingDefs/categories/stuffCategoriesToAllow/allowedQualities/allowedHitPointsPercents/disallowedThingDefs）
        public IntRange thingAmount;               // 需求数量区间（形态轴数量自由填写，v4.5 无四界校验）
        public float thingWeight = 1f;             // 抽取权重（Def 级按 validLevelWeightFactor，entry 级按 thingWeight）
        // 计价（entry 级，分币种共存 / 同类互斥，§3.2）
        public float? thingPriceCredit;            // 信用额单价
        public float? thingPriceKey;               // 密钥单价
        public float? orderTotalPriceCredit;       // 信用额总价
        public float? orderTotalPriceKey;          // 密钥总价
        public int thingMaxRepeat = 1;
        public float thingExtraFameFactor = 1f;
        public string label;                       // 订单项显示名（{插值变量} 支持，M5d 接入）
        public string description;                 // 风味描述（{RULEPACK:defName} 预留）
    }

    // 市场订单模板 Def（M4 §3.1）
    public class CelesFD_MarketClassDef : Def
    {
        public string title;
        public CelesFD_MarketCategory category;
        public CelesFD_MarketForm form;
        public bool isBuy;                         // true=星铃收购（玩家交付得报酬）| false=星铃出售（玩家支付得物资）
        public bool isGuaranteed;                  // true=保底订单（v4.4：每次刷新抽样生成多份独立订单，重置刷新）
        public IntRange? guaranteedItemCount;      // 保底专属：每次刷新生成的订单份数（缺省 2~3）
        public bool isLockable = true;             // 可否锁定豁免一次刷新（isGuaranteed 时忽略，Warning）
        public bool isSpecial;                     // 未来剧情特殊订单预留接口（B5，当前无实例）
        public int fameReward;                     // 整单声望（fame = round(fameReward × entry factor)，§3.2）
        public List<string> validLevel;            // 开放等级（UnlockLevelDef defName；保底豁免：isGuaranteed 时留空=全等级含 0 级）
        public float validLevelWeightFactor = 1f;
        public List<CelesFD_MarketEntry> includeThings;
        public List<CelesFD_VarOperationDef> prerequisiteConditions;  // 彩蛋前置判定（复用 VarOperationDef Equal/Gte）
        public string letterDef;                   // 彩蛋专属 letter DefName（null=默认彩蛋通知）
        public int orderDurationInQuadrums = 1;    // 履约期限（象，默认 1 象=1 个刷新周期）
        public bool haveSpecialRequire;
        public int marketSpecialFameRequire;
        public int marketSpecialTradeRequire;

        // M2 修复（v4.7）：ThingFilter 需显式 ResolveReferences 填充 allowedDefs（ThingFilter.cs:301）——否则 AllowedThingDefs 恒空（仿原版 RecipeDef）
        public override void ResolveReferences()
        {
            base.ResolveReferences();
            if (includeThings != null)
            {
                foreach (CelesFD_MarketEntry e in includeThings)
                {
                    if (e.filter != null)
                        e.filter.ResolveReferences();
                }
            }
        }

        // filter 配置字段反射（ThingFilter.cs:43,46 private；ResolveReferences 不清空，:301-335 实证——ConfigErrors 阶段可读）
        private static readonly System.Reflection.FieldInfo filterThingDefsField =
            AccessTools.Field(typeof(ThingFilter), "thingDefs");
        private static readonly System.Reflection.FieldInfo filterCategoriesField =
            AccessTools.Field(typeof(ThingFilter), "categories");

        public override IEnumerable<string> ConfigErrors()
        {
            // v4.5：形态轴/内容合规锚定校验已删除（纯 XML 填写自由）；仅保留数据完整性校验（D6：报错含 defName 与点位）
            foreach (string error in base.ConfigErrors())
                yield return error;

            foreach (CelesFD_MarketEntry entry in includeThings ?? Enumerable.Empty<CelesFD_MarketEntry>())
            {
                // v2 filter 两形态约束（2026-08-15 用户裁决）：单物模式（thingDefs 恰好 1）或类别模式（categories 恰好 1），互斥；
                // 黑白名单（disallowedThingDefs）作废；材质/耐久/质量字段保留（装填侧校验）
                if (entry.filter != null)
                {
                    var ftds = (List<ThingDef>)filterThingDefsField.GetValue(entry.filter);
                    var fcats = (List<string>)filterCategoriesField.GetValue(entry.filter);
                    int tdCount = ftds?.Count ?? 0;
                    int catCount = fcats?.Count ?? 0;
                    if (tdCount > 1)
                        yield return "thingDefs must have at most 1 item — use a single <categories> for 'any of category' semantics (defName=" + defName + ")";
                    if (catCount > 1)
                        yield return "categories must have exactly 1 item (defName=" + defName + ")";
                    if (tdCount > 0 && catCount > 0)
                        yield return "thingDefs and categories are mutually exclusive (defName=" + defName + ")";
                    if (tdCount == 0 && catCount == 0)
                        yield return "filter is empty — thingDefs (exactly 1) or categories (exactly 1) required (defName=" + defName + ")";
                }
                else
                {
                    yield return "entry filter missing (defName=" + defName + ")";
                }

                // 计价互斥（§3.2）：同币种单价∧总价 → Error
                if (entry.thingPriceCredit.HasValue && entry.orderTotalPriceCredit.HasValue)
                    yield return "entry has both thingPriceCredit and orderTotalPriceCredit (defName=" + defName + ", entry filter=" + (entry.filter != null ? entry.filter.Summary : "null") + ")";
                if (entry.thingPriceKey.HasValue && entry.orderTotalPriceKey.HasValue)
                    yield return "entry has both thingPriceKey and orderTotalPriceKey (defName=" + defName + ")";
            }

            // 计价全空（§3.3）：entry 四计价字段全空 且 fameReward <= 0 → Error
            if (fameReward <= 0)
            {
                bool anyPrice = includeThings != null && includeThings.Any(e =>
                    e.thingPriceCredit.HasValue || e.thingPriceKey.HasValue ||
                    e.orderTotalPriceCredit.HasValue || e.orderTotalPriceKey.HasValue);
                if (!anyPrice)
                    yield return "no price and fameReward <= 0 (defName=" + defName + ")";
            }

            // 出售单计价（v4.1）：isBuy=false 至少一个计价字段非空（否则玩家白拿物资）
            if (!isBuy && includeThings != null && !includeThings.Any(e =>
                e.thingPriceCredit.HasValue || e.thingPriceKey.HasValue ||
                e.orderTotalPriceCredit.HasValue || e.orderTotalPriceKey.HasValue))
                yield return "sell order (isBuy=false) has no price (defName=" + defName + ")";

            // 等级引用（§3.3）：validLevel 元素须存在；保底豁免：isGuaranteed 时留空 = 全等级合法
            if (validLevel == null || validLevel.Count == 0)
            {
                if (!isGuaranteed)
                    yield return "validLevel empty and not guaranteed (defName=" + defName + ")";
            }
            else
            {
                foreach (string lv in validLevel)
                {
                    // M2：强类型校验（UnlockLevelDef 类型已建立）
                    if (DefDatabase<CelesFD_UnlockLevelDef>.GetNamedSilentFail(lv) == null)
                        yield return "validLevel references missing UnlockLevelDef '" + lv + "' (defName=" + defName + ")";
                }
            }

            // 常驻×锁定冗余（v4.1）：isGuaranteed 时 isLockable 忽略 → Warning（ConfigErrors 只收 Error，此处直接 Log.Warning）
            if (isGuaranteed && isLockable)
                Log.Warning("[CelesFD] guaranteed def ignores isLockable (defName=" + defName + ")");
        }
    }
}
