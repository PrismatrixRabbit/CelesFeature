using RimWorld;
using Verse;

namespace CelesFeature
{
    // ════════════════════════════════════════════════════════════════
    //  批 4 终版（数值链重构 2026-09-26）：我方 stat 显示门禁与定制 worker 族
    //  · ThreadConsumerOnly：仅覆写 ShouldShowFor=thing 挂 Consumer 才显示（仅我方建筑）——
    //    服务预热时长 / 尘构机效率 / 依赖线程网络 / 未连接效率乘数 四 stat 的基础门禁；
    //    覆写手法=原版惯用（先例 StatWorker_Mechanitor 等 5 处 override ShouldShowFor）
    //  · ThreadDependency：+GetStatDrawEntryLabel 覆写——是/否反转显示（值 1=可离网→「否」；
    //    0=依赖→「是」；GetStatDrawEntryLabel 虚方法 StatWorker.cs:1147）
    //  · UnconnectedEfficiency：+ShouldShowFor 加条件——依赖型（dependency=0）隐藏本行（裁决 2026-09-26）
    // ════════════════════════════════════════════════════════════════
    public class CelesIM_StatWorker_ThreadConsumerOnly : StatWorker
    {
        public static CelesIM_CompThreadConsumer ConsumerOf(StatRequest req)
        {
            if (!req.HasThing)
                return null;
            return req.Thing.TryGetComp<CelesIM_CompThreadConsumer>();
        }

        public override bool ShouldShowFor(StatRequest req)
        {
            return !stat.alwaysHide && stat.CanShowWithLoadedMods() && ConsumerOf(req) != null;
        }
    }

    public class CelesIM_StatWorker_ThreadDependency : CelesIM_StatWorker_ThreadConsumerOnly
    {
        public override string GetStatDrawEntryLabel(StatDef stat, float value, ToStringNumberSense numberSense, StatRequest optionalReq, bool finalized = true)
        {
            return value >= 0.5f
                ? "CelesIM_Keyed_DepNo".Translate()    // 否（可离网——反转显示）
                : "CelesIM_Keyed_DepYes".Translate();  // 是（依赖线程网络）
        }
    }

    public class CelesIM_StatWorker_UnconnectedEfficiency : CelesIM_StatWorker_ThreadConsumerOnly
    {
        public override bool ShouldShowFor(StatRequest req)
        {
            return base.ShouldShowFor(req)
                && req.Thing.GetStatValue(CelesIM_DefOf.Celes_ThreadDependency) >= 0.5f;   // 依赖型隐藏本行
        }
    }
}
