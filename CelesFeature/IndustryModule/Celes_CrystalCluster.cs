using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // [事实] 晶簇等级贴图：覆写 Graphic（原版范式 Plant.cs:470），Comp 持有 GraphicData 列表并惰性解析
    public class Celes_CompProperties_CrystalCluster : CompProperties
    {
        public List<GraphicData> levelGraphics;
        // dev 直接生成（未走培育器 SpawnCluster/InitGrowth）时的默认参数兜底，XML 可配
        public List<int> pointToGrow;
        public List<TerrainDef> terrainToGrow;

        public Celes_CompProperties_CrystalCluster()
        {
            compClass = typeof(Celes_CompCrystalCluster);
        }
    }

    public class Celes_CompCrystalCluster : ThingComp
    {
        [Unsaved] private List<Graphic> levelGraphicCache;

        public Celes_CompProperties_CrystalCluster Props => (Celes_CompProperties_CrystalCluster)props;

        public Graphic GraphicForLevel(int level)
        {
            if (Props.levelGraphics == null)
                return null;
            if (levelGraphicCache == null)
            {
                levelGraphicCache = new List<Graphic>();
                for (int i = 0; i < Props.levelGraphics.Count; i++)
                    levelGraphicCache.Add(Props.levelGraphics[i].Graphic);
            }
            int index = level - 1;
            if (index < 0 || index >= levelGraphicCache.Count)
                return null;
            return levelGraphicCache[index];
        }

        // K1 测试辅助：DEV gizmo（god mode 下显示）
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!DebugSettings.ShowDevGizmos)
                yield break;
            yield return new Command_Action
            {
                defaultLabel = "DEV: 生长值+1",
                action = delegate { (parent as Celes_CrystalCluster)?.AddGrowthPoint(1); }
            };
            yield return new Command_Action
            {
                defaultLabel = "DEV: 立即地板检查",
                action = delegate { (parent as Celes_CrystalCluster)?.ForceTerrainCheck(); }
            };
        }
    }

    // [事实] 基类硬约束：GetFirstMineable 为 is Mineable 类型判断（GridsUtility.cs:362-372），
    // 晶簇必须继承 Mineable 才能进入原版开采系统（Designator_Mine + WorkGiver_Miner 零改动）
    public class Celes_CrystalCluster : Mineable
    {
        private int level = 1;
        private int growthPoints;
        private List<int> pointThresholds;
        private List<TerrainDef> terrainDefs;
        private TerrainAffordanceDef affordanceNeeded;

        public int Level => level;

        public int MaxLevel => pointThresholds?.Count ?? 1;

        // [事实] dev 直接生成时未走 InitGrowth → 参数 null → 地板摧毁失效（K1 根因）
        // 兜底：从 Def 的 Comp 默认参数初始化；培育器/晶种生成时 InitGrowth 覆盖
        // [事实] Thing 的挂点为 SpawnSetup(Map, bool)（PostSpawnSetup 是 ThingComp 的签名）
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (pointThresholds == null)
            {
                Celes_CompProperties_CrystalCluster props = def.GetCompProperties<Celes_CompProperties_CrystalCluster>();
                if (props != null)
                {
                    pointThresholds = props.pointToGrow != null ? new List<int>(props.pointToGrow) : null;
                    terrainDefs = props.terrainToGrow != null ? new List<TerrainDef>(props.terrainToGrow) : null;
                }
            }
        }

        // 地板改变/支撑不足 → 摧毁（独立于生长门控）
        public void ForceTerrainCheck()
        {
            if (!Spawned)
                return;
            if (affordanceNeeded != null)
            {
                // [事实] 支撑判定链 GenGrid.SupportsStructureType（GenGrid.cs:208-211），水面/流沙天然不通过
                if (!Position.GetAffordances(Map).Contains(affordanceNeeded))
                    Destroy(DestroyMode.Vanish);
            }
            else if (terrainDefs != null && !terrainDefs.Contains(Map.terrainGrid.TerrainAt(Position)))
            {
                // [事实] Mineable 覆写 Destroy(DestroyMode) 无默认参数（Mineable.cs:50）；Vanish 不触发产出掉落（:54 仅 KillFinalize）
                Destroy(DestroyMode.Vanish);
            }
        }

        // 参数由生长源（培育器/晶种）在创建时写入实例——[事实] 草案定稿"晶体的参数也由培育器决定"
        // affordanceNeeded 非 null 时地板检查走支撑判定（晶种模式 B），否则走地形列表
        public void InitGrowth(List<int> thresholds, List<TerrainDef> terrains, TerrainAffordanceDef affordance)
        {
            pointThresholds = thresholds;
            terrainDefs = terrains;
            affordanceNeeded = affordance;
        }

        // [事实] 阈值语义：累计生长值，list 长度即等级上限（可扩展，XML 加数值即加一级）
        public void AddGrowthPoint(int amount)
        {
            if (pointThresholds == null)
                return;
            growthPoints += amount;
            while (level < MaxLevel && growthPoints >= pointThresholds[level])
                level++;
        }

        // [事实] 地板改变摧毁：TickLong 2000tick 轮询，参照 Plant.DyingBecauseOfTerrainTags（Plant.cs:263-269）
        // 此检查独立于生长门控——失活/断连冻结不阻断地板检查
        public override void TickLong()
        {
            base.TickLong();
            ForceTerrainCheck();
        }

        public override Graphic Graphic
        {
            get
            {
                Graphic graphic = this.TryGetComp<Celes_CompCrystalCluster>()?.GraphicForLevel(level);
                return graphic ?? base.Graphic;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref level, "level", 1);
            Scribe_Values.Look(ref growthPoints, "growthPoints", 0);
            Scribe_Collections.Look(ref pointThresholds, "pointThresholds", LookMode.Value);
            Scribe_Collections.Look(ref terrainDefs, "terrainDefs", LookMode.Def);
            Scribe_Defs.Look(ref affordanceNeeded, "affordanceNeeded");
        }
    }
}
