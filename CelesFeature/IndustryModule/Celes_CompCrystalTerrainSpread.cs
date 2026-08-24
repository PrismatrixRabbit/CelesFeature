using System.Collections.Generic;
using Verse;

namespace CelesFeature
{
    // 晶化表面扩散（真菌状分叉 + 间隙填充——参照原版 CompGrowsFleshmassTendrils 的
    // GrowTendril/CreateBranch 分叉模式 + ThickenTendril 填充模式，CompGrowsFleshmassTendrils.cs:164-232）
    // 地板为 layerable terrain：SetTerrain 铺层保留底层（TerrainGrid.cs:216-228），玩家拆除还原原始地形（:280-305）
    public class Celes_CompProperties_CrystalTerrainSpread : CompProperties
    {
        public TerrainDef spreadTerrain;              // 感染地板（晶化表面）
        public int spreadPerCheck = 3;                // 每周期扩散操作数（XML 可配）
        public float branchChance = 0.1f;             // 尖端分支概率
        public int maxTips = 12;                      // 尖端数量上限
        public int minTips = 3;                       // 尖端数量下限（不足则从源头重生）
        public float thickenChance = 0.6f;            // 填充概率（B：分叉+间隙填充，参照原版 ThickenTendril 权重）
        public TerrainAffordanceDef neededAffordance; // 可感染支撑（Light——水面无 Light 不感染）

        public Celes_CompProperties_CrystalTerrainSpread()
        {
            compClass = typeof(Celes_CompCrystalTerrainSpread);
        }
    }

    // 生长尖端（菌丝端点：位置 + 方向 + 已生长长度）
    public class Celes_GrowthTip : IExposable
    {
        public IntVec3 position;
        public IntVec3 direction;
        public int length;

        public void ExposeData()
        {
            Scribe_Values.Look(ref position, "position");
            Scribe_Values.Look(ref direction, "direction");
            Scribe_Values.Look(ref length, "length", 0);
        }
    }

    public class Celes_CompCrystalTerrainSpread : ThingComp
    {
        private const int SpreadCheckTicks = 2500;    // 固定扩散周期（用户裁决：不可配）

        private int nextSpreadTick;
        private List<Celes_GrowthTip> tips = new List<Celes_GrowthTip>();

        [Unsaved] private Celes_CompCrystalSeed seedComp;

        public Celes_CompProperties_CrystalTerrainSpread Props => (Celes_CompProperties_CrystalTerrainSpread)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            seedComp = parent.GetComp<Celes_CompCrystalSeed>();
            if (respawningAfterLoad)
                return;
            // 晶种位置创建感染地板（layerable 铺层——可逆拆除还原原始地形）
            if (Props.spreadTerrain != null)
                parent.Map.terrainGrid.SetTerrain(parent.Position, Props.spreadTerrain);
            nextSpreadTick = Find.TickManager.TicksGame + SpreadCheckTicks;
            for (int i = 0; i < Props.minTips; i++)
                tips.Add(new Celes_GrowthTip { position = parent.Position, direction = RandomDirection(), length = 0 });
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!parent.Spawned)
                return;
            // 底线停止判定（用户裁决）：晶种失活 → 不再扩散；已铺地板保留
            if (seedComp == null || !seedComp.IsActive)
                return;
            if (Find.TickManager.TicksGame < nextSpreadTick)
                return;
            nextSpreadTick = Find.TickManager.TicksGame + SpreadCheckTicks;
            GrowOnce();
        }

        // 每周期 spreadPerCheck 次操作：分叉（尖端延伸）/ 填充（间隙填补）加权混合
        private void GrowOnce()
        {
            for (int i = 0; i < Props.spreadPerCheck; i++)
            {
                if (Rand.Value < Props.thickenChance)
                    Thicken();
                else
                    GrowTendril();
            }
            MaintainTips();
        }

        // 分叉：尖端沿方向前进（四方向，原版 CardinalDirections 同款）；概率分支（垂直方向 ±90°）
        private void GrowTendril()
        {
            if (tips.Count == 0)
                return;
            Celes_GrowthTip tip = tips.RandomElement();
            IntVec3 target = tip.position + tip.direction;
            if (!CanPlace(target))
            {
                tips.Remove(tip);   // 尖端受阻死亡
                return;
            }
            parent.Map.terrainGrid.SetTerrain(target, Props.spreadTerrain);
            tip.position = target;
            tip.length++;
            if (Rand.Value < Props.branchChance && tips.Count < Props.maxTips)
            {
                tips.Add(new Celes_GrowthTip
                {
                    position = tip.position,
                    direction = BranchDirection(tip.direction),
                    length = 0
                });
            }
        }

        // 填充：随机采样找感染边缘格（邻接非感染格）→ 随机可放置邻格铺地板（间隙填补）
        private void Thicken()
        {
            IntVec3 edge = FindRandomEdgeCell();
            if (edge == IntVec3.Invalid)
                return;
            if (GenAdj.AdjacentCells.TryRandomElementByWeight(delegate(IntVec3 dir)
            {
                return CanPlace(edge + dir) ? 1f : 0f;
            }, out IntVec3 dir))
            {
                parent.Map.terrainGrid.SetTerrain(edge + dir, Props.spreadTerrain);
            }
        }

        // 随机采样找感染边缘格（避免全图 List 分配——每周期最多 200 次采样 × 9 格检查）
        private IntVec3 FindRandomEdgeCell()
        {
            Map map = parent.Map;
            for (int i = 0; i < 200; i++)
            {
                IntVec3 cell = CellFinder.RandomCell(map);
                if (map.terrainGrid.TerrainAt(cell) != Props.spreadTerrain)
                    continue;
                for (int d = 0; d < GenAdj.AdjacentCells.Length; d++)
                {
                    IntVec3 adj = cell + GenAdj.AdjacentCells[d];
                    if (adj.InBounds(map) && CanPlace(adj))
                        return cell;
                }
            }
            return IntVec3.Invalid;
        }

        // 可放置判定：界内 + 无建筑 + 可站 + 支撑达标（Light）+ 未感染
        private bool CanPlace(IntVec3 cell)
        {
            Map map = parent.Map;
            if (!cell.InBounds(map))
                return false;
            if (cell.GetEdifice(map) != null)
                return false;
            if (!cell.Walkable(map))
                return false;
            if (Props.neededAffordance != null && !cell.GetAffordances(map).Contains(Props.neededAffordance))
                return false;
            if (map.terrainGrid.TerrainAt(cell) == Props.spreadTerrain)
                return false;
            return true;
        }

        private void MaintainTips()
        {
            if (tips.Count >= Props.minTips)
                return;
            for (int i = tips.Count; i < Props.minTips; i++)
                tips.Add(new Celes_GrowthTip { position = parent.Position, direction = RandomDirection(), length = 0 });
        }

        private static IntVec3 RandomDirection()
        {
            return GenAdj.CardinalDirections[Rand.RangeInclusive(0, GenAdj.CardinalDirections.Length - 1)];
        }

        // 垂直方向（±90° 分支）
        private static IntVec3 BranchDirection(IntVec3 dir)
        {
            if (dir.x != 0)
                return new IntVec3(0, 0, Rand.Value < 0.5f ? 1 : -1);
            return new IntVec3(Rand.Value < 0.5f ? 1 : -1, 0, 0);
        }

        // DEV：立即执行一次扩散周期
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!DebugSettings.ShowDevGizmos)
                yield break;
            yield return new Command_Action
            {
                defaultLabel = "DEV: 立即扩散",
                action = delegate { GrowOnce(); }
            };
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref nextSpreadTick, "nextSpreadTick", 0);
            Scribe_Collections.Look(ref tips, "tips", LookMode.Deep);
        }
    }
}
