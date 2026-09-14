using System.Collections.Generic;
using RimWorld;
using Verse;

namespace CelesFeature
{
    // W-2b：弹幕/烟幕排程器（渐进弹幕 / 一字烟幕 / 混杂弹幕共用）——时刻表驱动 spawn 坠落舱
    // 生命周期：比较式（startTick + offset <= now 逐项放行——dev 快进兼容；Bombardment 模式 A2-2）；
    //   表尽 → Destroy（本控制器经 ctx.RegisterController 注册进信标，Destroy 即 G19 终结，无需回调）
    public class CelesFD_BarrageController : ThingWithComps
    {
        public class ScheduleEntry : IExposable
        {
            public int offsetTicks;
            public IntVec3 cell;
            public ThingDef fallerDef;

            public void ExposeData()
            {
                Scribe_Values.Look(ref offsetTicks, "offsetTicks", 0);
                Scribe_Values.Look(ref cell, "cell");
                Scribe_Defs.Look(ref fallerDef, "fallerDef");
            }
        }

        public List<ScheduleEntry> schedule = new List<ScheduleEntry>();
        public int startTick;
        public int spawnedCount;   // 已放行索引（存档续行）

        public static CelesFD_BarrageController SpawnController(Map map, IntVec3 anchorCell, List<ScheduleEntry> entries)
        {
            CelesFD_BarrageController controller = (CelesFD_BarrageController)ThingMaker.MakeThing(
                DefDatabase<ThingDef>.GetNamed("CelesFD_BarrageController"));
            controller.schedule = entries;
            return (CelesFD_BarrageController)GenSpawn.Spawn(controller, anchorCell, map);
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!respawningAfterLoad) startTick = Find.TickManager.TicksGame;
        }

        protected override void Tick()
        {
            base.Tick();
            int now = Find.TickManager.TicksGame;
            while (spawnedCount < schedule.Count && now >= startTick + schedule[spawnedCount].offsetTicks)
            {
                ScheduleEntry e = schedule[spawnedCount];
                if (e.fallerDef != null && e.cell.InBounds(Map))
                    SkyfallerMaker.SpawnSkyfaller(e.fallerDef, e.cell, Map);   // 无内胆重载——纯爆/气体舱
                spawnedCount++;
            }
            if (spawnedCount >= schedule.Count)
                Destroy();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref schedule, "schedule", LookMode.Deep);
            Scribe_Values.Look(ref startTick, "startTick", 0);
            Scribe_Values.Look(ref spawnedCount, "spawnedCount", 0);
        }
    }

    // W-5：排程投放（合并旧 ProgressiveBarrage/SmokeLine/MixedBarrage——空间解析统一移入 LandingRule）
    // Grid 规则（IGridLayout）：行主序索引 → 排间 forwardInterval / 同排 lateralInterval
    //   （与旧公式 r*rowInterval + c*sameRow 逐 tick 等价）；取 fallerDefs 首项
    // Scatter 规则：均匀 scatterInterval；每点随机选弹
    public class CelesFD_Effect_Barrage : CelesFD_SupportEffect
    {
        public List<ThingDef> fallerDefs;        // Grid 取首项 / Scatter 每点随机选
        public int forwardIntervalTicks = 90;    // 前向排间延迟（1.5s——渐进弹幕线上值）
        public int lateralIntervalTicks = 18;    // 同排侧向延迟（0.3s）
        public int scatterIntervalTicks = 18;    // 散布均匀间隔（原版 Bombardment.bombIntervalTicks 同值 A2-2）

        public override void Trigger(CelesFD_EffectContext ctx)
        {
            if (fallerDefs.NullOrEmpty() || ctx.Map == null)
            {
                Log.Warning("[CelesFD] Effect_Barrage missing fallerDefs (support " +
                    (ctx.SupportDef != null ? ctx.SupportDef.defName : "null") + ")");
                return;
            }
            var entries = new List<CelesFD_BarrageController.ScheduleEntry>();
            if (ctx.SupportDef != null && ctx.SupportDef.landingRule is CelesFD_IGridLayout grid)
            {
                // Grid 路径：r = i / LateralCount、c = i % LateralCount（行主序契约——见 LandingRule）
                for (int i = 0; i < ctx.Cells.Count; i++)
                {
                    entries.Add(new CelesFD_BarrageController.ScheduleEntry
                    {
                        offsetTicks = (i / grid.LateralCount) * forwardIntervalTicks
                                    + (i % grid.LateralCount) * lateralIntervalTicks,
                        cell = ctx.Cells[i],
                        fallerDef = fallerDefs[0]
                    });
                }
            }
            else
            {
                for (int i = 0; i < ctx.Cells.Count; i++)
                {
                    entries.Add(new CelesFD_BarrageController.ScheduleEntry
                    {
                        offsetTicks = i * scatterIntervalTicks,
                        cell = ctx.Cells[i],
                        fallerDef = fallerDefs.RandomElement()
                    });
                }
            }
            // 锚点 = 信标格（Grid(1,5) 的 Cells[0] 是垂线端点而非信标；排程器为隐形 Ethereal，锚点无功能）
            if (entries.Count == 0) return;
            CelesFD_BarrageController controller = CelesFD_BarrageController.SpawnController(
                ctx.Map, ctx.Beacon.Position, entries);
            ctx.RegisterController(controller);
        }
    }
}
