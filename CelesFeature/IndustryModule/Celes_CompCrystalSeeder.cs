using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // 培育器/晶种共享的判定链参数基类
    public class Celes_CompProperties_CrystalGrowth : CompProperties
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
    }

    public class Celes_CompProperties_CrystalSeeder : Celes_CompProperties_CrystalGrowth
    {
        public Celes_CompProperties_CrystalSeeder()
        {
            compClass = typeof(Celes_CompCrystalSeeder);
        }
    }

    public class Celes_CompCrystalSeeder : ThingComp
    {
        private int nextGrowTick;
        private bool autoMarkHarvest;

        [Unsaved] private CompRefuelable refuelable;
        [Unsaved] private CompThreadConsumer threadComp;

        public Celes_CompProperties_CrystalGrowth Props => (Celes_CompProperties_CrystalGrowth)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            refuelable = parent.GetComp<CompRefuelable>();
            threadComp = parent.GetComp<CompThreadConsumer>();
            if (!respawningAfterLoad)
            {
                nextGrowTick = Find.TickManager.TicksGame + Props.ticksToGrow;
            }
        }

        // [事实] 必须用 CompTick：培育器 tickerType=Normal（CompRefuelable 持续消耗硬约束，CompProperties_Refuelable.cs:152-155），
        // Normal 型 Thing 仅逐 tick 调 CompTick，不调 CompTickRare——挂 CompTickRare 会导致检测永不执行
        // 每 tick 仅 int 比较 + 布尔门控，O(1) 成本可忽略
        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned)
                return;
            // [事实] 燃料停机门控 CompRefuelable.HasFuel（CompRefuelable.cs:65-79）
            // 功耗双档由原版 SetUpPowerVars 处理（CompPowerTrader.cs:245-258：spawn 时按 PowerOn 设 idlePowerDraw/PowerConsumption），
            // 勿手动覆盖 PowerOutput——曾致电网负载突增 → PowerNet 随机关闭（PowerNet.cs:249-271）
            bool hasFuel = refuelable == null || refuelable.HasFuel;
            // 带宽门控：断连/中枢离线冻结生长（不生长不新建），带宽组件为可选挂载
            bool threadOk = threadComp == null || (threadComp.IsConnected && !threadComp.IsStandby);
            if (Find.TickManager.TicksGame < nextGrowTick || !hasFuel || !threadOk)
                return;
            nextGrowTick = Find.TickManager.TicksGame + Props.ticksToGrow;
            Celes_CrystalGrowthUtility.DoGrowCheck(parent.Map, parent.Position, Props,
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
                    if (things[i] is Celes_CrystalCluster cluster && cluster.Level >= maxLevel
                        && parent.Map.designationManager.DesignationAt(cluster.Position, DesignationDefOf.Mine) == null)
                        parent.Map.designationManager.AddDesignation(new Designation(cluster.Position, DesignationDefOf.Mine));
                }
            }
        }

        // 选中圈：显示 growRadius 生效范围（类太阳灯，放置 ghost 见 PlaceWorker_CrystalGrowRadius）
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
            toggle.defaultLabel = "自动标记开采";
            toggle.defaultDesc = "开启后每次生长检测自动为已达最高等级的晶簇标记开采指令。";
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

        public override string CompInspectStringExtra()
        {
            string text = "自动标记开采: " + (autoMarkHarvest ? "开" : "关");
            if (Prefs.DevMode)
            {
                int remain = nextGrowTick - Find.TickManager.TicksGame;
                text += remain > 0
                    ? "\nDEV: 距下次检测 " + remain.ToStringTicksToPeriod()
                    : "\nDEV: 检测待执行";
            }
            return text;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref nextGrowTick, "nextGrowTick", 0);
            Scribe_Values.Look(ref autoMarkHarvest, "autoMarkHarvest", false);
        }
    }
}
