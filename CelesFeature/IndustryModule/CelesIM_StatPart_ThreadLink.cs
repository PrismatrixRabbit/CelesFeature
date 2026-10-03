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
            if (consumer == null)
                return;
            if (consumer.CannotWorkUnconnected)
            {
                val = 0f;   // 依赖型未连接=无法工作（计算归零 2026-09-27——InfoCard 0% 与 Inspect 文字一致）
                return;
            }
            val *= consumer.WarmUpFactor;
        }

        public override string ExplanationPart(StatRequest req)
        {
            CelesIM_CompThreadConsumer consumer = CelesIM_StatWorker_ThreadConsumerOnly.ConsumerOf(req);
            if (consumer == null)
                return null;
            if (consumer.CannotWorkUnconnected)
                return "CelesIM_Keyed_CannotWorkUnlinked".Translate();   // 基础 100%→最终 0% 的原因行（非乘数，无 x 前缀）
            if (!consumer.IsConnected)
                return "CelesIM_Keyed_EffOffline".Translate() + ": x"
                    + consumer.EfficiencyFloor.ToStringPercent();
            if (consumer.IsWarmingUp)
                return "CelesIM_Keyed_EffWarming".Translate() + ": x"
                    + consumer.WarmUpFactor.ToStringPercent();
            return null;
        }
    }

    // ════════════════════════════════════════════════════════════════
    // 批 6 D36：星铃调频乘数 part（2026-10-03）——落尘构机效率 stat（D40 修订）
    // · 乘法链：BillTick WorkSpeedMultiplier → GetStatValue(WorkTableWorkSpeedFactor) →
    //   P1 postfix → GetWorkSpeedFactor → GetStatValue(Celes_ThreadLinkEfficiency) → 本 part ×1.2
    //   → 周期 ETA（AppendForgeCycles）/终端 Inspect 尘构机效率行/InfoCard 分解行全链自动跟随
    // · 判定源=CelesIM_CompStarlinkTuning（收尾修正 10-03 解耦：原直判建筑类型，comp 化后
    //   双形态可挂任意建筑——A=自动生产机 bill 旗标 / B=常规工作台操作者判定，见该文件头）
    // · 注入：XML parts li（Stats_Building.xml Celes_ThreadLinkEfficiency，与 ThreadProgress 同位）
    // ════════════════════════════════════════════════════════════════
    public class CelesIM_StatPart_StarlinkTuning : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (!Tuned(req))
            {
                return;
            }
            val *= CelesIM_Bill_AutoProducer.StarlinkTuningFactor;
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!Tuned(req))
            {
                return null;   // 未调频不占分解行（原版 StatPart 仅在生效时显示，与 ThreadLink 同构）
            }
            return "CelesIM_Keyed_StarlinkTuning".Translate(
                CelesIM_Bill_AutoProducer.StarlinkTuningFactor.ToStringPercent());
        }

        private static bool Tuned(StatRequest req)
        {
            if (!req.HasThing)
            {
                return false;
            }
            CelesIM_CompStarlinkTuning tuning = req.Thing.TryGetComp<CelesIM_CompStarlinkTuning>();
            return tuning != null && tuning.TuningActive;
        }
    }
}
