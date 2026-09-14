using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // 支援类型（W-1：武备侧骨架；人员侧 R 系列扩展）
    public enum CelesFD_SupportType
    {
        CombatEquipment,   // 武备支援
        Personnel          // 人员支援
    }

    // 武备支援 Def（W-1：数据骨架 + 三态状态存储；落点效果分发随 W-2）
    // 规格来源：《武备人员支援实现方案》§2.1/§2.3（用户定稿）
    public class CelesFD_SupportDef : Def
    {
        public CelesFD_SupportType supportType = CelesFD_SupportType.CombatEquipment;
        public int unlockLevel;           // 开放等级（W-3 武备页等级分割线/下一级灰显）
        public int creditCost;            // 信用额消耗（W-3 付款）
        public int keyCost;               // 密钥消耗（W-3 付款）
        public float applyDelayHours;     // 武备申请审批延迟（小时；0 = 即申即送——人员侧）
        public int maxTotal = 32;         // 该支援"已可用 + 申请中"总上限（§2.3 默认 32）
        // ── W-2a（2026-09-03 用户裁决）──
        public int arrivalDelayTicks = 300;   // 信标落地 → 效果触发延迟（默认 5 秒；60 tick = 1 秒）
        public CelesFD_SupportEffect effect;         // 落点效果分发（XML <effect Class="...">——一个效果一个子类）
        public CelesFD_LandingRule landingRule;       // 特殊落点规则（XML <landingRule Class="...">；null = 坠落系统默认就近）
        public float previewRadius = 0f;              // 选点期范围预览圈半径（0=无；玩家选中支援后 Targeter 选落点时鼠标跟随渲染）
        // canTargetPawns 已删除（2026-09-03 裁决：目标唯一 = 信标落点，永久）
        public Color beamColor = new Color(1f, 0.078f, 0.078f, 0.95f);   // 信标光柱颜色（XML (r,g,b,a) 0-255——ParseHelper.cs:242-282 GenColor.FromBytes 实证；默认攻击红 255,20,20,242）
        public float beamWidth = 0.5f;    // 信标光柱宽度（格）
        // ── W-3（2026-09-04 用户三项新需求）──
        public int cooldownTicks = 0;          // 冷却时长（tick；0=无冷却；XML 可调占位）
        public bool occupiesQuota = true;      // 是否占用每象武备额度（description 显示）
        public string uiIcon;                  // FloatMenu/卡片 icon 路径（Celes/UI/CelesFD_Icons/xxx；不含扩展名）
        // ── W-4（2026-09-04 用户需求：生命周期解耦）──
        public bool hasOngoingTimer = false;   // 是否有正在进行计时（true=信标监视控制器完整执行[仅弹幕类]；
                                               // false=触发即销毁[激光/狙击/投送等——效果实体独立存活]）
        // ── R-1（2026-09-06）：人员支援字段（supportType == Personnel 时消费；规格 = 17 §2.1 v2.3）──
        public CelesFD_PersonnelArrivalMode arrivalMode = CelesFD_PersonnelArrivalMode.Standard;
        public CelesFD_PersonnelLandingMode landingMode = CelesFD_PersonnelLandingMode.TradeBeacon;
        public List<CelesFD_PersonnelEntry> personnel;             // 人员构成 = 完整度清单
        public int dailyWage;                                      // 日单价（总价 = 天数 × 日单价，下单收取）
        public int maxStayDays = 7;                                // 申请天数上限（+- 选框 1~此值）
        public int insuranceCost;                                  // 保险预缴额（下单时预缴）
        public int fuelFeePerPawn;                                 // 燃油费/人（信用额——按 headCount=Σamount 计费；三档模式对应值全 XML）
        public List<CelesFD_CompletenessTier> completenessTiers;   // 逐档结算（3 节点 + 兜底 = 4 项）
        public WorkTags workTagsToDisable = WorkTags.None;         // 禁工配置 per-def（C2——经 slate $workTags 注入 QuestNode_WorkDisabled）

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;
            if (maxTotal < 1) yield return "maxTotal must be >= 1";
            if (arrivalDelayTicks < 0) yield return "arrivalDelayTicks must be >= 0";
            if (effect == null && supportType == CelesFD_SupportType.CombatEquipment)
                yield return "W-2 support requires <effect Class=\"...\"> (landing effect dispatch)";
            if (supportType == CelesFD_SupportType.Personnel)
            {
                if (personnel.NullOrEmpty())
                    yield return "Personnel support requires <personnel> roster";
                if (dailyWage < 0 || insuranceCost < 0 || maxStayDays < 1)
                    yield return "Personnel economics invalid (dailyWage/insuranceCost >= 0, maxStayDays >= 1)";
                // 逐档校验：4 项、threshold 前 3 项降序 ∈(0,1]、multiplier ≥ 0（<0 报错——C4 裁决）
                if (completenessTiers == null || completenessTiers.Count < 2)
                    yield return "Personnel support requires completenessTiers (N nodes + fallback)";
                else
                {
                    for (int i = 0; i < completenessTiers.Count; i++)
                    {
                        if (completenessTiers[i].multiplier < 0f)
                            yield return "completenessTiers[" + i + "].multiplier must be >= 0";
                        if (i < completenessTiers.Count - 1)
                        {
                            float th = completenessTiers[i].threshold;
                            if (th <= 0f || th > 1f || (i > 0 && th >= completenessTiers[i - 1].threshold))
                                yield return "completenessTiers thresholds must be descending in (0,1] (fallback tier has no threshold)";
                        }
                    }
                }
                // B+ 弱校验（P16 v2 用户裁决）：含机械条目但无 isMechanitor 标记 → 提示（yield 红字——非硬错，纯机械 def 允许）
                bool hasMech = personnel.Any(e => e.pawnKind != null && e.pawnKind.RaceProps.IsMechanoid);
                bool hasAnchor = personnel.Any(e => e.isMechanitor);
                if (hasMech && !hasAnchor)
                    yield return defName + ": mech entries present but no <isMechanitor> anchor — mechs arrive unoverseen (feral MTB 10 days)";
            }
        }
    }

    // ── R-1 人员支援类型族（17 §2.1；枚举/条目/档位）──
    public enum CelesFD_PersonnelArrivalMode { Standard, Expedited, Instant }   // 一般 24h / 加急 1h / 即刻
    public enum CelesFD_PersonnelLandingMode { TradeBeacon, MapEdge }          // 轨道交易信标 / 地图边缘

    public class CelesFD_PersonnelEntry
    {
        public PawnKindDef pawnKind;
        public int amount = 1;
        public int weight = 1;        // 完整度权重
        public bool isMechanitor;     // B+ 锚定者标记（P16 v2）：仅此条目施加协议 hediff + 初次绑定全队机械
    }

    // 逐档结算（C4 终版）：扣除 = insuranceCost × multiplier（<1 退差 /=1 全扣 />1 全扣+超出部分扣信用额罚款）
    public class CelesFD_CompletenessTier
    {
        public float threshold;    // 档位下限（降序；兜底档不配；判定严格大于）
        public int fameOutput;     // 声望输出（可负）
        public float multiplier;   // 该档保险倍率
    }

    // 单支援三态状态：available（已可用）/ pending（正在申请的，审批延迟到期转可用）
    // draft（尝试申请的）= UI 草稿增量，不存档（W-3 武备页；§2.3 防 -1 bug）
    public class CelesFD_SupportState : IExposable
    {
        public string defName;
        public int available;
        public int pending;
        public long pendingSinceTick = -1;   // 最近一批申请提交时刻（-1 = 无审批中）
        public long cooldownUntilTick = -1;  // 冷却截止时刻（-1 = 不在冷却；W-3 N1）

        public void ExposeData()
        {
            Scribe_Values.Look(ref defName, "defName", null);
            Scribe_Values.Look(ref available, "available", 0);
            Scribe_Values.Look(ref pending, "pending", 0);
            Scribe_Values.Look(ref pendingSinceTick, "pendingSinceTick", -1L);
            Scribe_Values.Look(ref cooldownUntilTick, "cooldownUntilTick", -1L);
        }
    }
}
