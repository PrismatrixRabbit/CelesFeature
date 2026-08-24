using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // 野生晶种污染机制（独立组件，K9）
    // 状态机（唯一判定）：晶种活性 → 每 expandCheckTicks 扩张 +radiusGrowPerCheck；晶种失活 → 半径冻结（效果持续）
    // [事实] 植物伤害参照原版 DefoliatorShipPart（CompPlantHarmRadius.cs:37-91）：面积频率采样 → 随机点脱叶/击杀
    // [事实] 半径平滑微增量（无每日跳变）；冻结底线 = 晶簇生成判定次数 0/x（晶种失活）
    public class Celes_CompProperties_CrystalBlight : CompProperties
    {
        public float startRadius = 10.9f;
        public float radiusGrowPerCheck = 0.5f;
        public int expandCheckTicks = 2500;
        public float maxRadius = 125f;
        public HediffDef pawnHediff;              // XML 可选：不配则无 hediff 功能（cs 不内联默认）
        public float hediffSeverityPerDay = 1.0f;
        public float plantHarmFrequencyPerArea = 0.011f;
        public float leaflessPlantKillChance = 0.05f;
        public bool ignoreSpecialTrees = true;
        public bool messageOnCropDeath = true;

        public Celes_CompProperties_CrystalBlight()
        {
            compClass = typeof(Celes_CompCrystalBlight);
        }
    }

    public class Celes_CompCrystalBlight : ThingComp
    {
        private float currentRadius;
        private int nextExpandTick;
        private float plantHarmTimer;

        [Unsaved] private Celes_CompCrystalSeed seedComp;

        public Celes_CompProperties_CrystalBlight Props => (Celes_CompProperties_CrystalBlight)props;

        public float CurrentRadius => currentRadius;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            seedComp = parent.GetComp<Celes_CompCrystalSeed>();
            if (!respawningAfterLoad)
            {
                currentRadius = Props.startRadius;
                nextExpandTick = Find.TickManager.TicksGame + Props.expandCheckTicks;
            }
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!parent.Spawned)
                return;
            TryExpand();
            HarmPlants();
            ApplyHediffToPawns();
        }

        // 状态机：唯一判定 = 晶种活性；失活 → 冻结（植物/Hediff 持续生效）
        private void TryExpand()
        {
            if (Find.TickManager.TicksGame < nextExpandTick)
                return;
            nextExpandTick += Props.expandCheckTicks;
            if (seedComp != null && seedComp.IsActive && currentRadius < Props.maxRadius)
                currentRadius = Mathf.Min(currentRadius + Props.radiusGrowPerCheck, Props.maxRadius);
        }

        // [事实] 类同原版 CompPlantHarmRadius（CompPlantHarmRadius.cs:44-66）：倒计时 = 60/(πr²×freq)，rare 粒度换算
        private void HarmPlants()
        {
            plantHarmTimer -= 250f;
            if (plantHarmTimer > 0f)
                return;
            float area = Mathf.PI * currentRadius * currentRadius;
            float num = area * Props.plantHarmFrequencyPerArea;
            float interval = 60f / num;
            int hitCount;
            if (interval >= 1f)
            {
                plantHarmTimer = GenMath.RoundRandom(interval);
                hitCount = 1;
            }
            else
            {
                plantHarmTimer = 1f;
                hitCount = GenMath.RoundRandom(1f / interval);
            }
            for (int i = 0; i < hitCount; i++)
                HarmRandomPlant();
        }

        private void HarmRandomPlant()
        {
            IntVec3 c = parent.Position + (Rand.InsideUnitCircleVec3 * currentRadius).ToIntVec3();
            if (!c.InBounds(parent.Map))
                return;
            Plant plant = c.GetPlant(parent.Map);
            if (plant == null || (Props.ignoreSpecialTrees && plant.def.plant.treeCategory == TreeCategory.Super))
                return;
            if (plant.LeaflessNow)
            {
                if (Rand.Value < Props.leaflessPlantKillChance)
                    plant.Kill();
            }
            else
            {
                plant.MakeLeafless(Plant.LeaflessCause.Poison, Props.messageOnCropDeath);
            }
        }

        // Hediff：每 rareTick，排除机械体/星铃；严重度按天累计（250 tick 粒度）
        // [事实] 原版 API：HediffSet.GetFirstHediffOfDef（HediffSet.cs:531）+ AddDirect（HediffSet.cs:310），无 GetOrAddHediff
        // [事实] 遍历快照：AddDirect 触发 CheckForStateChange → 倒地重伤 pawn 死亡 → DeRegisterPawn 修改 AllPawnsSpawned → 直接 foreach 抛
        // "Collection was modified"（实测异常）——ToList 快照 + Destroyed 防御
        private void ApplyHediffToPawns()
        {
            if (Props.pawnHediff == null)
                return;
            float severityGain = Props.hediffSeverityPerDay * (250f / 60000f);
            foreach (Pawn pawn in parent.Map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (pawn.Destroyed || !pawn.Spawned)
                    continue;
                if (pawn.RaceProps.IsMechanoid || pawn.def == Celes_ThingDefOf.Celes_Race)
                    continue;
                if (!pawn.Position.InHorDistOf(parent.Position, currentRadius))
                    continue;
                Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(Props.pawnHediff);
                if (hediff == null)
                {
                    hediff = HediffMaker.MakeHediff(Props.pawnHediff, pawn);
                    // [事实] 根因：Hediff.PostMake 应用 def.initialSeverity（Hediff.cs:548），默认 0.5（HediffDef.cs:28），
                    // ToxicBuildup 未覆写 → 新施加即 50%（"直接进入中等中毒"）——显式压低从 0 缓慢累积
                    // [事实] 0 会被自动移除（hediff 严重度 0 无法存活）——至少 0.001 保底起步（实测确认）
                    hediff.Severity = 0.001f;
                    pawn.health.hediffSet.AddDirect(hediff);
                }
                hediff.Severity += severityGain;
            }
        }

        // 渲染：选中显示当前枯萎半径圈（类太阳灯）
        public override void PostDrawExtraSelectionOverlays()
        {
            GenDraw.DrawRadiusRing(parent.Position, currentRadius);
        }

        public override string CompInspectStringExtra()
        {
            string text = base.CompInspectStringExtra();
            // 单位换算为 /天（每判定 +radiusGrowPerCheck，判定周期 expandCheckTicks）：0.5×24 = 12.0/天
            float perDay = Props.radiusGrowPerCheck * (60000f / Props.expandCheckTicks);
            string s = "枯萎半径: " + currentRadius.ToString("0.0")
                + " (+" + perDay.ToString("0.0") + "/天)";
            return text.NullOrEmpty() ? s : text + "\n" + s;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref currentRadius, "currentRadius", 0f);
            Scribe_Values.Look(ref nextExpandTick, "nextExpandTick", 0);
            Scribe_Values.Look(ref plantHarmTimer, "plantHarmTimer", 0f);
        }
    }
}
