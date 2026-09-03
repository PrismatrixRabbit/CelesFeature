using System.Collections.Generic;
using RimWorld;
using UnityEngine;
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

    // c 类·渐进弹幕（用户清单 3）：originCell→信标方向，信标=第一排中列，向远离投掷者推进；
    //   排内左→右（同排间隔），排间推进（排间间隔），格距 grid pitch（用户裁决 5）
    public class CelesFD_Effect_ProgressiveBarrage : CelesFD_SupportEffect
    {
        public int rows = 5;
        public int columns = 3;
        public int sameRowIntervalTicks = 180;   // 0.3s
        public int rowIntervalTicks = 900;       // 1.5s
        public int cellSpacing = 5;              // 用户裁决
        public ThingDef fallerDef;

        public override void Trigger(CelesFD_EffectContext ctx)
        {
            if (fallerDef == null || ctx.Map == null) return;
            // 方向向量：投掷者→信标（零向量兜底向北）
            Vector3 dir = (ctx.Cell - ctx.OriginCell).ToVector3();
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = new Vector3(0f, 0f, -1f);
            dir.Normalize();
            Vector3 perp = new Vector3(-dir.z, 0f, dir.x);
            var entries = new List<CelesFD_BarrageController.ScheduleEntry>();
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    IntVec3 cell = (ctx.Cell.ToVector3()
                        + dir * (r * cellSpacing)
                        + perp * ((c - (columns - 1) * 0.5f) * cellSpacing)).ToIntVec3();
                    if (!cell.InBounds(ctx.Map)) continue;
                    var e = new CelesFD_BarrageController.ScheduleEntry
                    {
                        offsetTicks = r * rowIntervalTicks + c * sameRowIntervalTicks,
                        cell = cell,
                        fallerDef = fallerDef
                    };
                    entries.Add(e);
                }
            }
            SpawnAndRegister(ctx, entries);
        }

        internal static void SpawnAndRegister(CelesFD_EffectContext ctx, List<CelesFD_BarrageController.ScheduleEntry> entries)
        {
            if (entries.Count == 0) return;
            CelesFD_BarrageController controller = CelesFD_BarrageController.SpawnController(ctx.Map, ctx.Cell, entries);
            ctx.RegisterController(controller);
        }
    }

    // c 类·一字烟幕（用户清单 3'）：投掷线垂线方向、信标为中点、逐点错峰坠落烟幕舱（单点 r7.9 一次性 BlindSmoke）
    public class CelesFD_Effect_SmokeLine : CelesFD_SupportEffect
    {
        public int dropCount = 5;
        public int intervalTicks = 300;   // 0.5s
        public int spacing = 5;           // 用户裁决
        public ThingDef fallerDef;

        public override void Trigger(CelesFD_EffectContext ctx)
        {
            if (fallerDef == null || ctx.Map == null) return;
            Vector3 dir = (ctx.Cell - ctx.OriginCell).ToVector3();
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = new Vector3(0f, 0f, -1f);
            dir.Normalize();
            Vector3 perp = new Vector3(-dir.z, 0f, dir.x);
            var entries = new List<CelesFD_BarrageController.ScheduleEntry>();
            for (int i = 0; i < dropCount; i++)
            {
                IntVec3 cell = (ctx.Cell.ToVector3() + perp * ((i - (dropCount - 1) * 0.5f) * spacing)).ToIntVec3();
                if (!cell.InBounds(ctx.Map)) continue;
                entries.Add(new CelesFD_BarrageController.ScheduleEntry
                {
                    offsetTicks = i * intervalTicks,
                    cell = cell,
                    fallerDef = fallerDef
                });
            }
            CelesFD_Effect_ProgressiveBarrage.SpawnAndRegister(ctx, entries);
        }
    }

    // c 类·混杂弹幕（用户清单 4）：半径内随机点、随机二类弹（爆/燃）、齐射节奏（原版轨道轰炸间隔 18 tick）
    public class CelesFD_Effect_MixedBarrage : CelesFD_SupportEffect
    {
        public float radius = 16.9f;      // 用户确认（x.9 约定）
        public int count = 15;
        public int intervalTicks = 18;    // Bombardment.bombIntervalTicks 默认（A2-2）
        public List<ThingDef> fallerDefs;
        // 范围预览圈移至选点期（SupportDef.previewRadius——三轮裁决：执行期不画）

        public override void Trigger(CelesFD_EffectContext ctx)
        {
            if (fallerDefs.NullOrEmpty() || ctx.Map == null) return;
            // 候选格缓存（半径内可通行——一次构建，均匀抽取）
            var cells = new List<IntVec3>();
            foreach (IntVec3 c in GenRadial.RadialCellsAround(ctx.Cell, radius, useCenter: true))
                if (c.InBounds(ctx.Map) && c.Walkable(ctx.Map)) cells.Add(c);
            var entries = new List<CelesFD_BarrageController.ScheduleEntry>();
            for (int i = 0; i < count; i++)
            {
                IntVec3 cell = cells.Count > 0 ? cells.RandomElement() : ctx.Cell;
                entries.Add(new CelesFD_BarrageController.ScheduleEntry
                {
                    offsetTicks = i * intervalTicks,
                    cell = cell,
                    fallerDef = fallerDefs.RandomElement()
                });
            }
            CelesFD_Effect_ProgressiveBarrage.SpawnAndRegister(ctx, entries);
        }
    }
}
