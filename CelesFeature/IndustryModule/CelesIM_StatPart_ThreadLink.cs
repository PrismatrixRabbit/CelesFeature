using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // ════════════════════════════════════════════════════════════════
    //  批 4（E5）：尘构机效率——纯显示 StatPart（裁决 2026-09-26）
    //  · 唯一职责：InfoCard「工作效率系数」详情分解清单显示一行「尘构机效率: x{百分比}」，
    //    与原版四 StatPart 乘数（无电/房间角色/温度/室外）并列——修复 postfix「总值动、解释缺」
    //  · TransformValue 刻意空体：值乘法唯一在 CelesIM_Patch_StatExtension（P1 postfix）——
    //    两虚方法独立调用（Worker 计算只调 TransformValue / 详情页只调 ExplanationPart），双轨不冲突不双乘
    //  · 注入：StaticConstructorOnStartup 向 WorkTableWorkSpeedFactor.parts 追加
    //    （public List，StatDef.cs:140；parts 循环在 FinalizeValue :793-799——空体对值零影响）
    //  · 「尘构机效率」全 mod 显示点恰两处：①本分解清单 ②Consumer Inspect 行（建筑信息菜单）
    // ════════════════════════════════════════════════════════════════
    public class CelesIM_StatPart_ThreadLink : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            // 空体：值乘法唯一在 postfix（见类头注释）
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!req.HasThing)
                return null;
            CelesIM_CompThreadConsumer consumer = req.Thing.TryGetComp<CelesIM_CompThreadConsumer>();
            if (consumer == null)
                return null;
            float factor = consumer.GetWorkSpeedFactor();
            if (Mathf.Approximately(factor, 1f))
                return null;   // 满速不占分解行（原版 StatPart 仅在生效时显示，同构）
            return "CelesIM_Keyed_ThreadLinkFactor".Translate(factor.ToStringPercent());
        }
    }

    [StaticConstructorOnStartup]
    public static class CelesIM_StatPartInjection
    {
        static CelesIM_StatPartInjection()
        {
            StatDefOf.WorkTableWorkSpeedFactor.parts.Add(new CelesIM_StatPart_ThreadLink());
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  批 4 终版（数值链重构 2026-09-26）：尘构机效率 stat 的统一乘法 part
    //  · 单式统一（替代 EffOffline+EffWarming 双 part 双乘）：val ×= WarmUpFactor——
    //    floor + (1−floor) × 热度全态覆盖（完成=1 无害恒乘；未连接余温/无信号冷却/预热爬升全入式）
    //  · ExplanationPart=信息行模式（保留原措辞、互斥显示、不相乘对账——裁决 2026-09-26）：
    //    未连接→「未连接线程: x{floor}」（恒起点值）；预热中→「预热进程: x{当前}」
    // ════════════════════════════════════════════════════════════════
    public class CelesIM_StatPart_ThreadProgress : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            CelesIM_CompThreadConsumer consumer = CelesIM_StatWorker_ThreadConsumerOnly.ConsumerOf(req);
            if (consumer != null)
                val *= consumer.WarmUpFactor;
        }

        public override string ExplanationPart(StatRequest req)
        {
            CelesIM_CompThreadConsumer consumer = CelesIM_StatWorker_ThreadConsumerOnly.ConsumerOf(req);
            if (consumer == null)
                return null;
            if (!consumer.IsConnected)
                return "CelesIM_Keyed_EffOffline".Translate() + ": x"
                    + consumer.EfficiencyFloor.ToStringPercent();
            if (consumer.IsWarmingUp)
                return "CelesIM_Keyed_EffWarming".Translate() + ": x"
                    + consumer.WarmUpFactor.ToStringPercent();
            return null;
        }
    }
}
