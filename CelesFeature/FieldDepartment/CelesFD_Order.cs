using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // 订单状态（D1：单 List 队列，接取=字段翻转）
    // v2（2026-08-15）：filter 语义修订——订单携带候选集 thingDefNames（"任意此类商品"），推翻 v4.4 单物品定稿
    public enum CelesFD_OrderState
    {
        Available,   // 待接取（市场中）
        Accepted     // 已接取（履约中，模块 D）
    }

    // 市场订单运行时实例（M4 §10.3 M1；v2 候选集结构）
    public class CelesFD_Order : IExposable
    {
        public string templateDefName;        // 模板 Def（defName 存法，运行时解析；Def 缺失安全跳过）
        public string thingDefName;           // 旧单物字段（v2 前存档；ThingDefs getter 回退兼容——新档不再写）
        public List<string> thingDefNames = new List<string>();   // v2 候选集快照（filter 展开全量——"任意此类商品"语义）
        public int entryIndex = -1;           // v2 生成时快照：includeThings 中的 entry 位置（替代 ThingDef 反查——多候选无歧义）
        public int amount;                    // 需求数量（总量——多候选物混合累计，用户裁决 2026-08-15）
        public int remaining;                 // 剩余需求（交付结算扣减用，M6；生成时 = amount）
        // 计价快照（生成时锁定，§3.2：单价制 = price × amount；总价制 = 固定值；v2 计价选项 B：单价为 entry 级配置，Def 作者保证公平）
        public float? priceCredit;            // 信用额单价快照
        public float? priceKey;               // 密钥单价快照
        public float? totalPriceCredit;       // 信用额总价快照
        public float? totalPriceKey;          // 密钥总价快照
        public float orderValue;              // 价值（排序用，§3.2 公式）
        public float thingExtraFameFactor = 1f;  // 该 entry 的声望乘数快照（fame = round(fameReward × factor)，v4.4 单 entry）
        public CelesFD_OrderState state = CelesFD_OrderState.Available;
        public bool IsLocked;                 // 锁定标记（豁免一次刷新，Phase 1 当场消耗）
        public long deadlineTick = -1;        // 履约截止（已接取；-1 = 未接取）
        public int acceptOrderIndex = -1;     // 全局接单顺序（M6 FIFO 填充；-1 = 未接取）
        public bool intervened;               // M6-6 突发单介入标记（开始装载/首次发射后逾期计入成败，§5.7；存档）
        // 材质/品质快照（2026-08-15 用户裁决：生成时从 filter 约束内随机选取；filter 空 → 随机分配，必须分配——
        //   出售端用于发货（MakeThing(td, stuff)+SetQuality，修复 madeFromStuff 报错）；收购端用于显示/装填校验匹配）
        public string stuffDefName;           // 材质 ThingDef 快照（MadeFromStuff 物品；非材质物品 null）
        public QualityCategory qualityCategory = QualityCategory.Normal;
        public bool qualitySet;               // 品质是否分配（有 CompQuality 的物品；旧档默认 false）

        public CelesFD_MarketClassDef TemplateDef =>
            templateDefName.NullOrEmpty() ? null : DefDatabase<CelesFD_MarketClassDef>.GetNamedSilentFail(templateDefName);

        // filter 配置字段反射（ThingFilter.cs:46 private List<string> categories；ResolveReferences 不清空，:301-335 实证）
        private static readonly System.Reflection.FieldInfo filterCategoriesField =
            AccessTools.Field(typeof(ThingFilter), "categories");

        // 材质类别反射（ThingFilter.cs:69 private List<StuffCategoryDef>）——FD-G36（2026-09-02）：
        // 原版 Allows(Thing)（:874-903）无材质检查段（品质/耐久有、材质无）；stuffCategoriesToAllow 仅在
        // ResolveReferences :421 展开，def 级 allowedDefs 不能表达实例材质（同 Def 布制/皮制之分是实例属性）
        private static readonly System.Reflection.FieldInfo filterStuffCatsField =
            AccessTools.Field(typeof(ThingFilter), "stuffCategoriesToAllow");

        // 装填/结算匹配入口（FD-G36 2026-09-02）：filter.Allows(Thing) + 材质类别实例级补充检查——
        //   filter 配置了 stuffCategoriesToAllow 且物品带材质时，材质类别须命中其一；无材质物品不受约束
        public bool EntryFilterAllows(Thing thing)
        {
            ThingFilter f = EntryFilter;
            if (f == null || !f.Allows(thing)) return false;
            var cats = (List<StuffCategoryDef>)filterStuffCatsField.GetValue(f);
            if (cats == null || cats.Count == 0 || thing.Stuff == null) return true;
            return thing.Stuff.stuffProps != null && thing.Stuff.stuffProps.categories != null
                && thing.Stuff.stuffProps.categories.Any(c => cats.Contains(c));
        }

        // v2 候选集解析（旧档 thingDefName 回退单元素——语义一致）
        public List<ThingDef> ThingDefs
        {
            get
            {
                var list = new List<ThingDef>();
                if (thingDefNames != null)
                {
                    foreach (string n in thingDefNames)
                    {
                        if (n.NullOrEmpty()) continue;
                        ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(n);
                        if (d != null) list.Add(d);
                    }
                }
                if (list.Count == 0 && !thingDefName.NullOrEmpty())
                {
                    ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(thingDefName);
                    if (d != null) list.Add(d);
                }
                return list;
            }
        }

        // 字母序首个候选物（icon/价值/质量计算统一规则——v2 用户裁决：defName 字母序）
        public ThingDef FirstThingDef
        {
            get
            {
                List<ThingDef> tds = ThingDefs;
                if (tds.Count == 0) return null;
                ThingDef best = tds[0];
                for (int i = 1; i < tds.Count; i++)
                    if (string.CompareOrdinal(tds[i].defName, best.defName) < 0) best = tds[i];
                return best;
            }
        }

        // v2 类别模式判定（filter 恰好 1 个 categories 条目——校验层保证，ConfigErrors）
        public bool IsCategoryMode
        {
            get
            {
                CelesFD_MarketEntry entry = Entry;
                if (entry?.filter == null) return false;
                var cats = (List<string>)filterCategoriesField.GetValue(entry.filter);
                return cats != null && cats.Count == 1;
            }
        }

        // 类别模式下的类别（label/插值显示源）
        public ThingCategoryDef CategoryDef
        {
            get
            {
                CelesFD_MarketEntry entry = Entry;
                if (entry?.filter == null) return null;
                var cats = (List<string>)filterCategoriesField.GetValue(entry.filter);
                return cats != null && cats.Count == 1
                    ? DefDatabase<ThingCategoryDef>.GetNamedSilentFail(cats[0]) : null;
            }
        }

        // v2 entry 快照（生成时记录 entryIndex——替代按 ThingDef 反查，多候选无歧义；label/description/factor 来源）
        private CelesFD_MarketEntry Entry
        {
            get
            {
                CelesFD_MarketClassDef def = TemplateDef;
                if (def == null || def.includeThings == null || entryIndex < 0 || entryIndex >= def.includeThings.Count) return null;
                return def.includeThings[entryIndex];
            }
        }

        // 订单物品源 filter（2026-08-15：装填/结算匹配校验——原版 ThingFilter.Matches 含材质/品质/耐久约束）
        public ThingFilter EntryFilter => Entry?.filter;

        // M5d 插值：{varName} 解析（同款 ResolveNodeText 模式，DialogueEngine.cs:157-178；取值源 = 订单实例数据）
        // 未知变量（含 {RULEPACK:} 预留，§7.2）→ 原样输出
        public string ResolveText(string template)
        {
            if (template.NullOrEmpty()) return template;
            var sb = new StringBuilder(template.Length);
            for (int i = 0; i < template.Length; i++)
            {
                if (template[i] == '{')
                {
                    int close = template.IndexOf('}', i + 1);
                    if (close > i)
                    {
                        string varName = template.Substring(i + 1, close - i - 1);
                        string value = ResolveVariable(varName);
                        if (value != null)
                        {
                            sb.Append(value);
                            i = close;
                            continue;
                        }
                    }
                }
                sb.Append(template[i]);
            }
            return sb.ToString();
        }

        // 插值变量最小集（§7.1）
        private string ResolveVariable(string varName)
        {
            switch (varName)
            {
                case "thingLabel":
                    // v2：多候选（收购类别模式）= 类别 label；单物品（含出售类别抽定）= 物品 label
                    if (thingDefNames.Count > 1)
                    {
                        ThingCategoryDef cat = CategoryDef;
                        if (cat != null) return cat.label;
                    }
                    return BaseThingLabel();
                case "amount": return amount.ToString();
                case "priceCredit": return CalcPriceCredit().ToString("0.##");
                case "priceKey": return CalcPriceKey().ToString("0.##");
                case "fameReward":
                    CelesFD_MarketClassDef def = TemplateDef;
                    return (def != null ? Mathf.RoundToInt(def.fameReward * thingExtraFameFactor) : 0).ToString();
                default: return null;
            }
        }

        // 计价（§3.2：单价制 = price × amount；总价制 = 固定值）——结算/显示共用
        public float CalcPriceCredit()
            => priceCredit.HasValue ? priceCredit.Value * amount : (totalPriceCredit ?? 0f);

        public float CalcPriceKey()
            => priceKey.HasValue ? priceKey.Value * amount : (totalPriceKey ?? 0f);

        // 单物品 label（含材质/品质快照拼接——"良好 布料战斗服"；2026-08-15 用户裁决）
        private string BaseThingLabel()
        {
            ThingDef td = FirstThingDef;
            if (td == null) return templateDefName;
            ThingDef stuff = StuffDef;
            string label = stuff != null ? GenLabel.ThingLabel(td, stuff) : td.label;
            if (qualitySet)
                label = QualityUtility.GetLabel(qualityCategory) + " " + label;
            return label;
        }

        // 材质快照解析（非材质物品/旧档 null）
        public ThingDef StuffDef =>
            stuffDefName.NullOrEmpty() ? null : DefDatabase<ThingDef>.GetNamedSilentFail(stuffDefName);

        public string ResolveLabel()
        {
            CelesFD_MarketEntry entry = Entry;
            if (entry != null && !entry.label.NullOrEmpty())
                return ResolveText(entry.label);
            // v2 显示规则（用户裁决）：多候选（仅收购类别模式）→ 类别 label；单物品（含出售类别抽定）→ 物品 label + 材质/品质
            if (thingDefNames.Count > 1)
            {
                ThingCategoryDef cat = CategoryDef;
                if (cat != null) return cat.label;
            }
            return BaseThingLabel();
        }

        public string ResolveDescription()
        {
            CelesFD_MarketEntry entry = Entry;
            return entry != null && !entry.description.NullOrEmpty() ? ResolveText(entry.description) : null;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref templateDefName, "templateDefName", null);
            Scribe_Values.Look(ref thingDefName, "thingDefName", null);   // 旧档兼容保留（新档 null）
            Scribe_Collections.Look(ref thingDefNames, "thingDefNames", LookMode.Value);
            Scribe_Values.Look(ref entryIndex, "entryIndex", -1);
            Scribe_Values.Look(ref amount, "amount", 0);
            Scribe_Values.Look(ref remaining, "remaining", 0);
            Scribe_Values.Look(ref priceCredit, "priceCredit", null);
            Scribe_Values.Look(ref priceKey, "priceKey", null);
            Scribe_Values.Look(ref totalPriceCredit, "totalPriceCredit", null);
            Scribe_Values.Look(ref totalPriceKey, "totalPriceKey", null);
            Scribe_Values.Look(ref orderValue, "orderValue", 0f);
            Scribe_Values.Look(ref thingExtraFameFactor, "thingExtraFameFactor", 1f);
            Scribe_Values.Look(ref state, "state", CelesFD_OrderState.Available);
            Scribe_Values.Look(ref IsLocked, "isLocked", false);
            Scribe_Values.Look(ref deadlineTick, "deadlineTick", -1L);
            Scribe_Values.Look(ref acceptOrderIndex, "acceptOrderIndex", -1);
            Scribe_Values.Look(ref intervened, "intervened", false);
            Scribe_Values.Look(ref stuffDefName, "stuffDefName", null);
            Scribe_Values.Look(ref qualityCategory, "qualityCategory", QualityCategory.Normal);
            Scribe_Values.Look(ref qualitySet, "qualitySet", false);
        }
    }
}
