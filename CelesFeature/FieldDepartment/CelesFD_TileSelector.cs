using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // 信标站选点器（F4）：近/远点用正态权重 FloodFill（复用原版判定 + 反向 landmark），太空随机复用原版 RandomSettlementTileFor
    public static class CelesFD_TileSelector
    {
        // 近点：区间 [7,18]，μ=9，σ=4 正态（初次信标站，峰值靠近点）
        public static bool TryFindNearTile(out PlanetTile tile)
            => TryFindCandidates(out tile, minDist: 7, maxDist: 18, pickUniform: false, mean: 9f, sigma: 4f);

        // 远点：区间 [12,32] 线性均等（申请新站，各距离等概率）
        public static bool TryFindFarTile(out PlanetTile tile)
            => TryFindCandidates(out tile, minDist: 12, maxDist: 32, pickUniform: true);

        // 太空随机：复用原版全球随机可建 settlement 位点（无基准），反向过滤 landmark
        public static bool TryFindRandomTile(out PlanetTile tile, Faction beaconFaction)
        {
            tile = TileFinder.RandomSettlementTileFor(Find.WorldGrid.Surface, beaconFaction,
                extraValidator: t => Find.World.landmarks[t] == null);
            return tile.Valid;
        }

        // 玩家是否在地面（有可建位点基准）；太空时失败 → 对话层走太空复选分支
        public static bool TryFindPlayerSurfaceTile(out PlanetTile tile)
            => TileFinder.TryFindRandomPlayerTile(out tile, allowCaravans: false, validator: null, canBeSpace: false);

        // 通用选点：FloodFill 收集 [min,max] 可达候选 + IsValidTileForNewSettlement + 反向 landmark；
        // pickUniform=true 均等随机，否则正态权重（mean/sigma）
        private static bool TryFindCandidates(out PlanetTile tile, int minDist, int maxDist, bool pickUniform, float mean = 0f, float sigma = 1f)
        {
            if (!CelesFD_BeaconUtility.TryGetPlayerGroundTile(out PlanetTile root))
            {
                tile = PlanetTile.Invalid;   // 无地面基准（太空时映射失败 → 申请失败节点）
                return false;
            }

            var candidates = new List<(PlanetTile tile, int dist)>();
            root.Layer.Filler.FloodFill(root,
                (PlanetTile x) => !Find.World.Impassable(x),   // 可通行 → region 可达性天然保证（与 WorldReachability 同款判定）
                delegate (PlanetTile x, int traversalDistance)
                {
                    if (traversalDistance < minDist || traversalDistance > maxDist) return false;
                    if (!TileFinder.IsValidTileForNewSettlement(x)) return false;
                    if (Find.World.landmarks[x] != null) return false;   // 反向 landmark：信标站避开地标
                    candidates.Add((x, traversalDistance));
                    return false;
                });

            if (candidates.Count == 0)
            {
                tile = PlanetTile.Invalid;
                return false;
            }

            if (pickUniform)
            {
                if (candidates.TryRandomElement(out var u))
                {
                    tile = u.tile;
                    return true;
                }
            }
            else if (candidates.TryRandomElementByWeight(c => Mathf.Exp(-(Mathf.Pow(c.dist - mean, 2f)) / (2f * sigma * sigma)), out var chosen))
            {
                tile = chosen.tile;
                return true;
            }
            tile = PlanetTile.Invalid;
            return false;
        }
    }
}
