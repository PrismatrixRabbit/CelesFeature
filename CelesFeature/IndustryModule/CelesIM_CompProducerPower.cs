using RimWorld;
using Verse;

namespace CelesFeature
{
    // ════════════════════════════════════════════════════════════════
    //  批 7：中枢功率 comp（挂中枢）
    //  · 唯一耗电写点：250t 读「当前功率修正」stat（值链=max(1,(1600+Σ)×Π)，
    //    全链 StatDef+parts 驱动）写 powerComp.PowerOutput = -值
    //  · 显示与实际同读一条 stat 链（显示同步红线）；PostSpawnSetup 即写一次，
    //    首帧 Tick 补写（文本 order 修订后 comp 前置于产出行——首写可能早于链接成形，首帧纠正）
    //  · Inspect「当前功率修正：+N W」直出净修正量（D55；实时读 stat 非缓存）
    //  · PowerOutput 非 virtual、SetUpPowerVars 单点写入（CompPowerTrader.cs:36-58/:245-258）
    //    → 250t 动态覆写为唯一路径（批 5 待机电力同款已验证模式）
    // ════════════════════════════════════════════════════════════════
    public class CelesIM_CompProperties_ProducerPower : CompProperties
    {
        public CelesIM_CompProperties_ProducerPower()
        {
            compClass = typeof(CelesIM_CompProducerPower);
        }
    }

    public class CelesIM_CompProducerPower : ThingComp
    {
        [Unsaved] private CompPowerTrader powerComp;
        [Unsaved] private bool wroteFirst;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.GetComp<CompPowerTrader>();
            WritePower();
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!wroteFirst || parent.IsHashIntervalTick(250))
            {
                WritePower();
                wroteFirst = true;
            }
        }

        // 设备开关联动/事件即时刷新入口
        public void WritePower()
        {
            if (powerComp == null || parent == null || !parent.Spawned)
            {
                return;
            }
            float watts = parent.GetStatValue(CelesIM_DefOf.Celes_ProducerPowerAdjust);
            powerComp.PowerOutput = 0f - watts;
        }

        public override string CompInspectStringExtra()
        {
            // D55 修订（10-04 用户语义确认）：本行=净修正量（带符号）——最终耗电 − 中枢默认耗电；
            // 恒等式「默认 + 修正 = 最终」恒成立（含 1W 钳制折入：双平衡器 = −1599 W + 1600 = 1 W）。
            // 最终总功率由原版功率行自然显示（PowerOutput 写入=最终值；无需另显）；
            // stat 分解仍以默认耗电为基准（基础值 1600 → 加值 → 乘区 → 最终值）
            CompPowerTrader pc = powerComp ?? parent.GetComp<CompPowerTrader>();
            float defaultWatts = pc != null ? pc.Props.PowerConsumption : 0f;
            float delta = parent.GetStatValue(CelesIM_DefOf.Celes_ProducerPowerAdjust) - defaultWatts;
            return "CelesIM_Keyed_ProducerPowerInspect".Translate(
                (delta >= 0f ? "+" : "") + delta.ToString("#####0") + " W");
        }
    }
}
