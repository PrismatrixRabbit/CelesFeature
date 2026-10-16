using System.Collections.Generic;
using RimWorld;
using Verse;

namespace CelesFeature
{
    // ════════════════════════════════════════════════════════════════
    //  批 6 收尾修正：星铃调频 comp（双形态 · 用户裁决 2026-10-03）
    //  · 「是否受星铃调频」的 XML 挂载点——StatPart_CelestiaSync 经本 comp 判定
    //    （原直判建筑类型=解耦前形态；现在任意建筑挂 comp 即可参与调频）
    //  · 形态 A（AutoBill，默认）：挂自动生产机——触发源=bill 旗标（D36 原语义零变化：
    //    Bill_AutoProducer.Notify_DoBillStarted 判 billDoer race 置位 / Reset 清除，
    //    经建筑 ActiveBill（public 属性 Building_WorkTableAutonomous:14）读当前活动单）
    //  · 形态 B（OperatorAtWork）：挂常规工作台（小型纳米锻炉）——操作者（星铃 race）
    //    正在本台工作 → ×1.2。落点同在尘构机效率 stat（D40：效率加成单点），
    //    经 P1 postfix 自动传导工作速度系数——Inspect 尘构机效率行/工作速度系数行
    //    （CompReportWorkSpeed）/InfoCard 分解行全链免费跟随
    //  · 操作者检测：InteractionCell 上 curJob 目标为本建筑的 pawn 判 race——
    //    原版做单期间恒站交互格（JobDriver_DoBill 保留 targetA=工作台），站定才计=
    //    「正在该台工作」语义（用户裁决 10-03）。查询驱动+60t 节流缓存（≤1s 滞后），
    //    刻意不用 CompTick：工作台 def tickerType 多为 Never，comp tick 不可靠
    // ════════════════════════════════════════════════════════════════
    public enum CelesIM_CelestiaSyncMode
    {
        AutoBill,        // 形态 A：bill 旗标（自动生产机）
        OperatorAtWork   // 形态 B：操作者正在本台工作（常规工作台）
    }

    public class CelesIM_CompProperties_CelestiaSync : CompProperties
    {
        public CelesIM_CelestiaSyncMode mode = CelesIM_CelestiaSyncMode.AutoBill;

        public CelesIM_CompProperties_CelestiaSync()
        {
            compClass = typeof(CelesIM_CompCelestiaSync);
        }
    }

    public class CelesIM_CompCelestiaSync : ThingComp
    {
        public CelesIM_CompProperties_CelestiaSync Props => (CelesIM_CompProperties_CelestiaSync)props;

        // 形态 B 瞬态缓存（可推导不存档）；cachedTick 负初值=首查必扫
        private int cachedTick = -99999;
        private bool cachedValue;

        // StatPart_CelestiaSync 唯一消费方（尘构机效率 parts 链）
        public bool TuningActive
        {
            get
            {
                if (Props.mode == CelesIM_CelestiaSyncMode.AutoBill)
                {
                    return parent is CelesIM_Building_AutoProducer producer
                        && producer.ActiveBill is CelesIM_Bill_AutoProducer bill
                        && bill.CelestiaSyncActive;
                }
                if (Find.TickManager.TicksGame - cachedTick < 60)
                {
                    return cachedValue;
                }
                cachedTick = Find.TickManager.TicksGame;
                cachedValue = ScanOperatorCelestiaSync();
                return cachedValue;
            }
        }

        private bool ScanOperatorCelestiaSync()
        {
            Map map = parent.Map;
            if (map == null || !(parent is Building building) || !building.def.hasInteractionCell)
            {
                return false;   // 蓝图/微缩/无交互格守卫（InteractionCell 无格时访问会记错误）
            }
            List<Thing> cellThings = building.InteractionCell.GetThingList(map);
            for (int i = 0; i < cellThings.Count; i++)
            {
                if (cellThings[i] is Pawn pawn
                    && pawn.def == CelesIM_DefOf.Celes_Race
                    && pawn.jobs != null && pawn.jobs.curJob != null
                    && pawn.jobs.curJob.targetA.Thing == parent)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
