using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // 信标站工具：B 派系获取 / 站列表查询 / 生成命名 / 站描述（坐标·距离·地形）
    public static class CelesFD_BeaconUtility
    {
        public static Faction BeaconFaction
            => Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => f.def == CelesFD_DefOf.Celes_BeaconFaction);

        // 站列表：B 派系 + CelesFD_BeaconStation，按生成先后（creationGameTicks）
        public static List<Settlement> GetStations()
        {
            var faction = BeaconFaction;
            if (faction == null) return new List<Settlement>();
            return Find.WorldObjects.Settlements
                .Where(s => s.Faction == faction && s.def == CelesFD_DefOf.CelesFD_BeaconStation)
                .OrderBy(s => s.creationGameTicks)
                .ToList();
        }

        // 生成 + 命名（复用 F3 生成链；rulePack null → 用 B 派系 settlementNameMaker）
        public static Settlement GenerateStation(PlanetTile tile)
        {
            WorldObject obj = WorldObjectMaker.MakeWorldObject(CelesFD_DefOf.CelesFD_BeaconStation);
            obj.Tile = tile;
            obj.SetFaction(BeaconFaction);
            Find.WorldObjects.Add(obj);
            var s = obj as Settlement;
            if (s != null)
                s.Name = SettlementNameGenerator.GenerateSettlementName(s);
            return s;
        }

        // 站描述：{站名} - {坐标} ({距离}格, 位于{地形})
        public static string FormatStationDescription(Settlement s)
        {
            string name = s.Name.NullOrEmpty() ? s.Label : s.Name;
            string coord = FormatCoords(s.Tile);
            int dist = DistanceToPlayer(s.Tile);
            string biome = Find.WorldGrid[s.Tile].PrimaryBiome.LabelCap;
            return "CelesFD_Keyed_StationDesc".Translate(name, coord, dist, biome);
        }

        private static string FormatCoords(PlanetTile tile)
        {
            Vector2 longLat = Find.WorldGrid.LongLatOf(tile);
            float lat = longLat.y;
            float lon = longLat.x;
            return $"{Mathf.Abs(lat):0.#}°{(lat >= 0 ? "N" : "S")} {Mathf.Abs(lon):0.#}°{(lon >= 0 ? "E" : "W")}";
        }

        // 玩家地面基准：地面=玩家地面位；太空=太空位映射对应地面 tile（Orbit 层直接连接 Surface）
        public static bool TryGetPlayerGroundTile(out PlanetTile groundTile)
        {
            if (TileFinder.TryFindRandomPlayerTile(out var ground, allowCaravans: false, validator: null, canBeSpace: false))
            {
                groundTile = ground;
                return true;
            }
            if (TileFinder.TryFindRandomPlayerTile(out var space, allowCaravans: true, validator: null, canBeSpace: true)
                && Find.WorldGrid.TryGetFirstAdjacentLayerOfDef(space, PlanetLayerDefOf.Surface, out var surfaceLayer))
            {
                groundTile = surfaceLayer.GetClosestTile_NewTemp(space);   // 太空 tile 对应的地面坐标
                return groundTile.Valid;
            }
            groundTile = PlanetTile.Invalid;
            return false;
        }

        private static int DistanceToPlayer(PlanetTile tile)
        {
            if (!TryGetPlayerGroundTile(out var player))
                return -1;
            return Find.WorldGrid.TraversalDistanceBetween(player, tile);
        }
    }
}
