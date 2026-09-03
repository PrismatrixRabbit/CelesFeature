using System.Collections.Generic;
using System.Linq;
using Verse;

namespace CelesFeature
{
    // W-2a：落点规则基类——开放性接口（用户需求：允许扩展 Mod 写入落点规则）
    // XML 载体：<landingRule Class="CelesFeature.XxxRule"/>（单字段 Class 节点——A2-9）
    // null = 坠落系统默认（Skyfaller_OrbitCrate 落地 r=10 就近 + 迷雾两段式——现行语义）
    public abstract class CelesFD_LandingRule
    {
        public abstract bool TryResolveCell(IntVec3 beaconCell, Map map, out IntVec3 cell);
    }

    // 精确落位（a-1 无误差）：信标格本体——格上 edifice 避让与 L1 清障由坠落 Impact 统一处理
    public class CelesFD_LandingRule_Exact : CelesFD_LandingRule
    {
        public override bool TryResolveCell(IntVec3 beaconCell, Map map, out IntVec3 cell)
        {
            cell = beaconCell;
            return beaconCell.InBounds(map);
        }
    }

    // 均匀散射（a-2/a-3 误差坠落）：半径内可通行格等概率（"范围内概率平均"——按格数均匀 ≈ 面积均匀）
    // 半径走 x.9 约定（半径规范 §4-1：用户整数 → N-0.1）；无有效格回落信标格
    public class CelesFD_LandingRule_UniformScatter : CelesFD_LandingRule
    {
        public int radius = 3;

        public override bool TryResolveCell(IntVec3 beaconCell, Map map, out IntVec3 cell)
        {
            List<IntVec3> cells = GenRadial.RadialCellsAround(beaconCell, radius, useCenter: false)
                .Where(c => c.InBounds(map) && c.Walkable(map)).ToList();
            if (cells.Count > 0)
            {
                cell = cells.RandomElement();
                return true;
            }
            cell = beaconCell;
            return beaconCell.InBounds(map);
        }
    }
}
