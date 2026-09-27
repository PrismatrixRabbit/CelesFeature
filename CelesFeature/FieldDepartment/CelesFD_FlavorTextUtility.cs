using System.Collections.Generic;
using Verse;
using Verse.Grammar;

namespace CelesFeature
{
    // ═══ 文法文本工具（2026-09-25）：市场风味 desc 三态之③ RULEPACK 模式的解析底座 ═══
    // 消费方：CelesFD_Order.ResolveDescription（市场订单描述）；未来述职报告随机文本传各自根词复用。
    // 路径参照原版 NameGenerator.cs:47（纯 rulepack 无 tale）：GrammarRequest + Includes + GrammarResolver.Resolve。
    // desc 三态（2026-09-25 用户裁决）：①空 = 无描述 ②纯文本（可含 M5d 插值变量）③{RULEPACK:包名} 整字段。
    public static class CelesFD_FlavorTextUtility
    {
        public const string RulePackPrefix = "RULEPACK:";
        public const string MarketRootKeyword = "r_market_flavor";   // 市场 flavor 包统一根词约定（包内须含此 root 规则）

        // 整字段判定："{RULEPACK:PackName}" 恰好占满 description（粒度裁决：不开内嵌混排）
        // 整字段形态 = "{RULEPACK:PackName}"（花括号包裹）——判定/拆名统一 Trim 容错（防多行书写首尾空白）
        public static bool IsWholeFieldRulePackRef(string s)
        {
            if (s.NullOrEmpty()) return false;
            string t = s.Trim();
            return t.StartsWith("{" + RulePackPrefix) && t.EndsWith("}");
        }

        // ═══ 双段拆解（FD 小收尾 2026-09-25）：{RULEPACK:包名:根词} ═══
        // 单段 {RULEPACK:X} 回退默认根词 MarketRootKeyword（向后兼容）；
        // 双段 {RULEPACK:X:Y} → pack=X, root=Y——一个 pack 服务多个市场 entry（按根词区分文案组）
        public static void ExtractPackAndRoot(string s, out string packName, out string rootKeyword)
        {
            string t = s.Trim();
            int start = 1 + RulePackPrefix.Length;
            string inner = t.Substring(start, t.Length - start - 1);   // "Pack" 或 "Pack:Root"
            int colon = inner.IndexOf(':');
            if (colon < 0)
            {
                packName = inner;
                rootKeyword = MarketRootKeyword;   // 单段=回退默认
            }
            else
            {
                packName = inner.Substring(0, colon);
                rootKeyword = inner.Substring(colon + 1);
            }
        }

        // 生成：包缺失 / 无根词 → null（调用方回退原样输出——对齐 M5d 未知变量原样输出行为）。
        // HasRule 预检防 GrammarResolver 运行期红字；结果由调用方缓存为字符串落盘（无 seed——预解析裁决）。
        public static string ResolveRulePack(string packName, string rootKeyword)
        {
            RulePackDef pack = DefDatabase<RulePackDef>.GetNamedSilentFail(packName);
            if (pack == null)
            {
                Log.Warning("[CelesFD] Flavor rulepack not found: " + packName);
                return null;
            }
            var request = new GrammarRequest();
            request.Includes.Add(pack);
            if (!request.HasRule(rootKeyword))
            {
                Log.Warning("[CelesFD] Flavor rulepack '" + packName + "' lacks root rule '" + rootKeyword + "'");
                return null;
            }
            return GrammarResolver.Resolve(rootKeyword, request, "[CelesFD] Flavor " + packName);
        }

        // ═══ FD 小收尾（2026-09-25）：纯字符串列表选取——跑马灯/要闻专用 ═══
        // 纯文本候选集（无嵌套语法/无 [slot]）不需要 GrammarResolver 解析，直接读包内规则过滤+Generate 提取。
        // 防重逻辑（跑马灯 lastTicker1/2）由调用方在 C# 侧维护（用户裁决）——本方法只提供候选列表。
        // RulePackDef.rulePack 为 private——公共入口 RulesImmediate（RulePackDef.cs:66）
        public static List<string> GetRuleStrings(string packName, string rootKeyword)
        {
            RulePackDef pack = DefDatabase<RulePackDef>.GetNamedSilentFail(packName);
            var rules = pack?.RulesImmediate;
            if (rules.NullOrEmpty())
            {
                Log.Warning("[CelesFD] RulePack not found or empty: " + packName);
                return null;
            }
            var result = new List<string>();
            foreach (Rule rule in rules)
            {
                if (rule.keyword == rootKeyword && rule is Rule_String rs && !rs.OutputNull)
                    result.Add(rs.Generate());
            }
            if (result.Count == 0)
                Log.Warning("[CelesFD] RulePack '" + packName + "' has no rules for root '" + rootKeyword + "'");
            return result;
        }

        // ═══ 条件文本选取（2026-09-25）：Constants 注入版——跑马灯/要闻按游戏状态条件显示 ═══
        // 调用方把对话变量（Level/Relation/ApologyTriggered 等）塞进 Dictionary → GrammarResolver
        // 的 ConstantConstraint 过滤链自动排除不匹配条件的规则（GrammarResolver.cs:513）。
        // 规则写法：r_welcome_ticker(Level==1)->文本（条件写在关键词后的参数列表）
        // 防重逻辑由调用方 C# 侧维护（对齐 GetRuleStrings 裁决）。
        public static string ResolveConditional(string packName, string rootKeyword, Dictionary<string, string> constants)
        {
            RulePackDef pack = DefDatabase<RulePackDef>.GetNamedSilentFail(packName);
            if (pack == null)
            {
                Log.Warning("[CelesFD] RulePack not found: " + packName);
                return null;
            }
            var request = new GrammarRequest();
            request.Includes.Add(pack);
            if (constants != null)
            {
                foreach (var kv in constants)
                    request.Constants[kv.Key] = kv.Value;
            }
            if (!request.HasRule(rootKeyword))
            {
                Log.Warning("[CelesFD] RulePack '" + packName + "' lacks root rule '" + rootKeyword + "'");
                return null;
            }
            return GrammarResolver.Resolve(rootKeyword, request, "[CelesFD] Conditional " + packName);
        }
    }
}
