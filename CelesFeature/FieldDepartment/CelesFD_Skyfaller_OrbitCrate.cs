using RimWorld;
using Verse;

namespace CelesFeature
{
    // 建筑坠落落地（fallerDef thingClass——2026-08-15）：
    //   Impact 覆写（原版 protected virtual Skyfaller.cs:478 实证）→ 落点 r=10 就近找可放置格生成建筑；
    //   双分支（2026-08-15 修复）：容器模式（lootTableDef+buildingDef——危险垃圾箱，生成后填战利品表）/
    //   普通建筑模式（内容 Building 条目的 thingDefName——晶体 CrystalSeed，非容器）；找不到格 → Log.Warning（黄字）不生成
    public class CelesFD_Skyfaller_OrbitCrate : Skyfaller
    {
        protected override void Impact()
        {
            hasImpacted = true;
            Map map = base.Map;
            CelesFD_OrbitDropFallerExtension ext = def.GetModExtension<CelesFD_OrbitDropFallerExtension>();
            CelesFD_DropCrateDef crate = ext?.crateDef;
            if (crate == null)
            {
                Log.Warning($"[CelesFD] Orbit crate faller missing crateDef ({def.defName})");
                Destroy();
                return;
            }
            // 容器模式（危险垃圾箱）：生成容器建筑 + 按战利品表填内容（可搜刮出 pawn）
            if (crate.lootTableDef != null && crate.lootTableDef.buildingDef != null)
            {
                if (!TryFindPlaceNear(base.Position, map, out IntVec3 cell))
                {
                    Log.Warning($"[CelesFD] Orbit crate landing failed: no valid spot within r=10 near {base.Position}");
                    Destroy();
                    return;
                }
                Thing building = ThingMaker.MakeThing(crate.lootTableDef.buildingDef);
                building.SetFactionDirect(Faction.OfPlayer);
                GenSpawn.Spawn(building, cell, map);
                if (building is CelesFD_Building_OrbitCrate orbitCrate)
                    orbitCrate.FillFromLootTable(crate.lootTableDef);
                Destroy();
                return;
            }
            // 普通建筑模式（晶体）：内容 Building 条目的 thingDefName → r=10 生成（非容器）
            CelesFD_DropContentEntry entry = crate.contents?.Find(e => e.type == CelesFD_DropContentType.Building);
            ThingDef buildingDef = entry != null ? DefDatabase<ThingDef>.GetNamedSilentFail(entry.thingDefName) : null;
            if (buildingDef == null)
            {
                Log.Warning($"[CelesFD] Orbit crate {crate.defName} has no building target (lootTableDef or Building entry thingDefName)");
                Destroy();
                return;
            }
            if (!TryFindPlaceNear(base.Position, map, out IntVec3 cell2))
            {
                Log.Warning($"[CelesFD] Orbit crate landing failed: no valid spot within r=10 near {base.Position}");
                Destroy();
                return;
            }
            Thing b = ThingMaker.MakeThing(buildingDef);
            b.SetFactionDirect(Faction.OfPlayer);
            GenSpawn.Spawn(b, cell2, map);
            Destroy();
        }

        // r=10 就近找可放置格（walkable/无建筑/无屋顶遮蔽可选——最小：walkable + 无 edifice）
        private static bool TryFindPlaceNear(IntVec3 center, Map map, out IntVec3 result)
        {
            for (int r = 0; r <= 10; r++)
            {
                foreach (IntVec3 c in GenRadial.RadialCellsAround(center, r, useCenter: false))
                {
                    if (c.InBounds(map) && c.Walkable(map) && !c.Fogged(map) && c.GetEdifice(map) == null)
                    {
                        result = c;
                        return true;
                    }
                }
            }
            result = IntVec3.Invalid;
            return false;
        }
    }
}
