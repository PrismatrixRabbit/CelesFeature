using System.Collections.Generic;
using RimWorld;
using Verse;

namespace CelesFeature
{
    // U7 定稿：terrain 筛选（TerrainList）与仅看支撑（Affordance）互斥双模式
    public enum CrystalTerrainCheckMode
    {
        TerrainList,
        Affordance
    }

    public class Celes_CompProperties_CrystalSeed : Celes_CompProperties_CrystalGrowth
    {
        public int attemptsLimit = 30;
        public CrystalTerrainCheckMode terrainCheckMode = CrystalTerrainCheckMode.TerrainList;
        public GraphicData activeGraphicData;

        public Celes_CompProperties_CrystalSeed()
        {
            compClass = typeof(Celes_CompCrystalSeed);
        }
    }

    public class Celes_CompCrystalSeed : ThingComp
    {
        private int attemptsRemaining;
        private int nextGrowTick;

        [Unsaved] private Graphic activeGraphic;

        public Celes_CompProperties_CrystalSeed Props => (Celes_CompProperties_CrystalSeed)props;

        public bool IsActive => attemptsRemaining > 0;

        public Graphic ActiveGraphic
        {
            get
            {
                if (Props.activeGraphicData == null)
                    return null;
                if (activeGraphic == null)
                    activeGraphic = Props.activeGraphicData.Graphic;
                return activeGraphic;
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                attemptsRemaining = Props.attemptsLimit;
                nextGrowTick = Find.TickManager.TicksGame + Props.ticksToGrow;
            }
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!parent.Spawned || !IsActive)
                return;
            if (Find.TickManager.TicksGame < nextGrowTick)
                return;
            nextGrowTick = Find.TickManager.TicksGame + Props.ticksToGrow;
            // U3 定稿：每次检测扣 1，无论成败；先扣后判，耗尽当次不检测
            attemptsRemaining--;
            if (attemptsRemaining <= 0)
            {
                attemptsRemaining = 0;
                return;
            }
            TerrainAffordanceDef affordance = null;
            if (Props.terrainCheckMode == CrystalTerrainCheckMode.Affordance)
                affordance = parent.def.terrainAffordanceNeeded;
            Celes_CrystalGrowthUtility.DoGrowCheck(parent.Map, parent.Position, Props, CellValidator, affordance);
        }

        private bool CellValidator(IntVec3 cell)
        {
            if (Props.terrainCheckMode == CrystalTerrainCheckMode.Affordance)
            {
                // [事实] 复用 def 自带字段 terrainAffordanceNeeded（BuildableDef.cs:52）
                TerrainAffordanceDef need = parent.def.terrainAffordanceNeeded;
                return need != null && cell.GetAffordances(parent.Map).Contains(need);
            }
            return Props.terrainToGrow != null && Props.terrainToGrow.Contains(parent.Map.terrainGrid.TerrainAt(cell));
        }

        // U4 定稿：重置活性接口预留，MVP 仅 DEV gizmo 使用（互动 Job/Work 不注册）
        public void ResetSeed()
        {
            attemptsRemaining = Props.attemptsLimit;
            nextGrowTick = Find.TickManager.TicksGame + Props.ticksToGrow;
        }

        // 选中圈：显示 growRadius 生效范围（类太阳灯，放置 ghost 见 PlaceWorker_CrystalGrowRadius）
        public override void PostDrawExtraSelectionOverlays()
        {
            GenDraw.DrawRadiusRing(parent.Position, Props.growRadius);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!DebugSettings.ShowDevGizmos)
                yield break;
            yield return new Command_Action
            {
                defaultLabel = "DEV: 重置判定次数",
                action = delegate { ResetSeed(); }
            };
            yield return new Command_Action
            {
                defaultLabel = "DEV: 立即检测",
                action = delegate { nextGrowTick = 0; }
            };
        }

        public override string CompInspectStringExtra()
        {
            if (!Prefs.DevMode)
                return base.CompInspectStringExtra();
            string text = base.CompInspectStringExtra();
            text = text.NullOrEmpty() ? "" : text + "\n";
            // U7b 定稿：剩余次数玩家不可见，dev 可见
            return text + "DEV: 剩余判定次数 " + attemptsRemaining + "/" + Props.attemptsLimit;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref attemptsRemaining, "attemptsRemaining", 0);
            Scribe_Values.Look(ref nextGrowTick, "nextGrowTick", 0);
        }
    }

    // [事实] 活跃/失活双贴图：覆写 Graphic（Plant.cs:470 范式）；XML graphicData = 失活态贴图
    public class Celes_CrystalSeedThing : Building
    {
        public override Graphic Graphic
        {
            get
            {
                Celes_CompCrystalSeed comp = this.TryGetComp<Celes_CompCrystalSeed>();
                if (comp != null && comp.IsActive && comp.ActiveGraphic != null)
                    return comp.ActiveGraphic;
                return base.Graphic;
            }
        }
    }
}
