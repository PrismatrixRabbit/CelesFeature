using System.Collections.Generic;
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

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;
            if (maxTotal < 1) yield return "maxTotal must be >= 1";
            if (arrivalDelayTicks < 0) yield return "arrivalDelayTicks must be >= 0";
            if (effect == null) yield return "W-2 support requires <effect Class=\"...\"> (landing effect dispatch)";
        }
    }

    // 单支援三态状态：available（已可用）/ pending（正在申请的，审批延迟到期转可用）
    // draft（尝试申请的）= UI 草稿增量，不存档（W-3 武备页；§2.3 防 -1 bug）
    public class CelesFD_SupportState : IExposable
    {
        public string defName;
        public int available;
        public int pending;
        public long pendingSinceTick = -1;   // 最近一批申请提交时刻（-1 = 无审批中）

        public void ExposeData()
        {
            Scribe_Values.Look(ref defName, "defName", null);
            Scribe_Values.Look(ref available, "available", 0);
            Scribe_Values.Look(ref pending, "pending", 0);
            Scribe_Values.Look(ref pendingSinceTick, "pendingSinceTick", -1L);
        }
    }
}
