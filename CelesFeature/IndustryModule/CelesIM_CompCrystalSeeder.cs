using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // 培育器/晶种共享的判定链参数基类
    public class CelesIM_CompProperties_CrystalGrowth : CompProperties
    {
        public int ticksToGrow = 10000;
        public float growRadius = 9.9f;
        public float totalGrowChance = 1.5f;
        public float maxGrowChance = 0.8f;
        public float growStepDecrement = 0.5f;
        public float perDecreaseChance = 0.15f;
        public float minGrowChance = 0.05f;
        public ThingDef baseGrowDef;
        public List<TerrainDef> terrainToGrow;
        public List<int> pointToGrow = new List<int> { 0, 2, 5, 10 };
        public float chanceToGrow = 0.33f;
        public TerrainDef spawnTerrain;   // 晶簇生成位置创建的地板（layerable 晶化表面）
    }

    public class CelesIM_CompProperties_CrystalSeeder : CelesIM_CompProperties_CrystalGrowth
    {
        public CelesIM_CompProperties_CrystalSeeder()
        {
            compClass = typeof(CelesIM_CompCrystalSeeder);
        }
    }

    public class CelesIM_CompCrystalSeeder : ThingComp
    {
        private int nextGrowTick;
        private bool autoMarkHarvest;

        [Unsaved] private CompRefuelable refuelable;
        [Unsaved] private CelesIM_CompThreadConsumer threadComp;
        [Unsaved] private CompPowerTrader powerComp;

        public CelesIM_CompProperties_CrystalGrowth Props => (CelesIM_CompProperties_CrystalGrowth)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            refuelable = parent.GetComp<CompRefuelable>();
            threadComp = parent.GetComp<CelesIM_CompThreadConsumer>();
            powerComp = parent.GetComp<CompPowerTrader>();
            if (!respawningAfterLoad)
            {
                nextGrowTick = Find.TickManager.TicksGame + Props.ticksToGrow;
            }
        }

        // [事实] 必须用 CompTick：培育器 tickerType=Normal（CompRefuelable 持续消耗硬约束，CompProperties_Refuelable.cs:152-155），
        // Normal 型 Thing 仅逐 tick 调 CompTick，不调 CompTickRare——挂 CompTickRare 会导致检测永不执行
        // F1 后每 tick 求值三态门控（冻结平移要求停机期间逐 tick 执行；单建筑量级可控，用户裁决 2026-10-16）
        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned)
                return;
            // [事实] 燃料停机门控 CompRefuelable.HasFuel（CompRefuelable.cs:65-79）
            // [事实] 功耗双档由原版 SetUpPowerVars 处理（CompPowerTrader.cs:245-258：spawn 时按 PowerOn 设 idlePowerDraw/PowerConsumption），
            // 勿手动覆盖 PowerOutput——曾致电网负载突增 → PowerNet 随机关闭（PowerNet.cs:249-271）
            bool hasFuel = refuelable == null || refuelable.HasFuel;
            // 带宽门控：断连/中枢离线冻结生长（不生长不新建），带宽组件为可选挂载
            bool threadOk = threadComp == null || (threadComp.IsConnected && !threadComp.IsStandby);
            // F1 断电门控：断电时 consumeFuelOnlyWhenPowered 不耗料→HasFuel 常 true，原链路漏判致断电仍生长——
            // [事实] 原版耗料同构判定（CompRefuelable.cs:225：consumeFuelOnlyWhenPowered 时 comp.PowerOn 才 ConsumeFuel）；
            // 无电 comp 视为不依赖电（与 refuelable/threadComp 可选挂载放行惯例同构）；flick 关闭→PowerOn=false 已覆盖
            bool powerOk = powerComp == null || powerComp.PowerOn;
            if (!hasFuel || !threadOk || !powerOk)
            {
                // F1 停机冻结：nextGrowTick 逐 tick 平移 → "剩余倒计时"恒定在断开瞬间的值，恢复后走完剩余再检测；
                // dev 快进（DebugSetTicksGame 跳变无 CompTick）不平移 → 恢复即检一次（等价旧档过期值语义）
                if (Find.TickManager.TicksGame < nextGrowTick)
                    nextGrowTick++;
                return;
            }
            if (Find.TickManager.TicksGame < nextGrowTick)
                return;
            nextGrowTick = Find.TickManager.TicksGame + Props.ticksToGrow;
            CelesIM_CrystalGrowthUtility.DoGrowCheck(parent.Map, parent.Position, Props,
                cell => Props.terrainToGrow != null && Props.terrainToGrow.Contains(parent.Map.terrainGrid.TerrainAt(cell)),
                null);
            if (autoMarkHarvest)
                MarkMatureClusters();
        }

        // [事实] 自动标记：DesignationDefOf.Mine 为格目标类型（targetType=Cell）——必须用格目标构造 + 格查重
        // （DesignationManager.cs:169 格查重/AddDesignation；Designator_Mine.DesignateSingleCell 同款 :77）
        // 防重禁用品 HasMapDesignationOn(Thing)：其查 Thing 索引（:328-331），查不到格目标 designation → 恒 false → double-add 报错
        // 只标记已达 Def 规定最高级（pointToGrow 末级）的晶簇
        private void MarkMatureClusters()
        {
            int maxLevel = Props.pointToGrow.Count;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(parent.Position, Props.growRadius, true))
            {
                if (!cell.InBounds(parent.Map))
                    continue;
                List<Thing> things = parent.Map.thingGrid.ThingsListAtFast(cell);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is CelesIM_CrystalCluster cluster && cluster.Level >= maxLevel
                        && parent.Map.designationManager.DesignationAt(cluster.Position, DesignationDefOf.Mine) == null)
                        parent.Map.designationManager.AddDesignation(new Designation(cluster.Position, DesignationDefOf.Mine));
                }
            }
        }

        // 选中圈：显示 growRadius 生效范围（类太阳灯，放置 ghost 见 CelesIM_PlaceWorker_CrystalGrowRadius）
        public override void PostDrawExtraSelectionOverlays()
        {
            GenDraw.DrawRadiusRing(parent.Position, Props.growRadius);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // [事实] Command_Toggle 范式（CompRefuelable.cs:356-371）；默认关（U8 定稿）
            Command_Toggle toggle = new Command_Toggle();
            toggle.isActive = () => autoMarkHarvest;
            toggle.toggleAction = () => autoMarkHarvest = !autoMarkHarvest;
            toggle.defaultLabel = "CelesIM_Keyed_AutoMarkLabel".Translate();
            toggle.defaultDesc = "CelesIM_Keyed_AutoMarkDesc".Translate();
            toggle.icon = ContentFinder<Texture2D>.Get("UI/Designators/Mine");
            yield return toggle;
            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: 立即检测",
                    action = delegate { nextGrowTick = 0; }
                };
            }
        }

        // 自动标记状态由 gizmo 开关形态表达（用户裁决 2026-10-16：inspect 行与 gizmo 信息重复，删除）；DEV 倒计时保留
        public override string CompInspectStringExtra()
        {
            if (!Prefs.DevMode)
                return null;
            int remain = nextGrowTick - Find.TickManager.TicksGame;
            return remain > 0
                ? "DEV: 距下次检测 " + remain.ToStringTicksToPeriod()
                : "DEV: 检测待执行";
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref nextGrowTick, "nextGrowTick", 0);
            Scribe_Values.Look(ref autoMarkHarvest, "autoMarkHarvest", false);
        }
    }
}
