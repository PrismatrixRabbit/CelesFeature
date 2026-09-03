using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace CelesFeature
{
    // 建筑坠落落地（fallerDef thingClass——2026-08-15）：
    //   Impact 覆写（原版 protected virtual Skyfaller.cs:478 实证）→ 落点就位生成建筑
    // W-2a 改造（2026-09-03 终版：落地爆 + ignoredThings 自伤根治）：
    //   落地爆炸在 Impact 时刻触发（用户裁决：回退落地表现，r0.9——空中爆时机难选/表现力差，弃）；
    //   Explosion 是持续传播实体（cellsToAffect 按 GetCellAffectTick 逐 tick 推进，Explosion.cs:119-173），
    //   落地后生成的建筑会被仍在传播的爆心格命中（二轮回归根因）——根治 = 建筑实例先 ThingMaker 生成、
    //   以 ignoredThings 传入爆炸（传播期跳过清单对象——原版同机制保护落地舱内容物，:309 传递链），
    //   再落格：容器自伤数据层面消除（内容物在内胆未入 thingGrid，天然不受击）
    //   选格中心优先（L1 精确落位基础）+ L1 清障默认（植物销毁；Filth/物品交原版 Wipe 语义）
    // 双分支：容器模式（lootTableDef+buildingDef）/ 普通建筑模式（Building 条目 thingDefName）；找不到格 → Warning 不生成
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
                // W-2b：纯效果舱——无 crateDef = 只承载 impact 扩展（爆炸 + 独立烟幕圈），不生成建筑
                CelesFD_SkyfallerImpactExtension boomPure = def.GetModExtension<CelesFD_SkyfallerImpactExtension>();
                DoImpactExplosion(map, null);
                if (boomPure != null && boomPure.postExplosionGasType.HasValue && boomPure.gasRadius > 0f)
                {
                    // 逐格圆形投放（三轮修复：单点投放经 GasGrid 4 邻扩散成形=曼哈顿菱形[TryDiffuseGases :374-415]；
                    //   逐格直投 = 真圆[GenRadial 圆形格集]；每格 gasAmount 低于 255 防过饱和外溢[圈增长根因]）
                    GasType gas = boomPure.postExplosionGasType.Value;
                    foreach (IntVec3 c in GenRadial.RadialCellsAround(base.Position, boomPure.gasRadius, useCenter: true))
                        if (c.InBounds(map)) GasUtility.AddGas(c, map, gas, boomPure.gasAmount);
                }
                Destroy();
                return;
            }
            // 容器模式：生成容器建筑 + 填内容（lootTableDef.spawnAll = 固定清单[W-2a 武备空投]；否则权重抽一）
            if (crate.lootTableDef != null && crate.lootTableDef.buildingDef != null)
            {
                if (!TryFindPlaceNear(base.Position, map, allowFogged: true, out IntVec3 cell))
                {
                    Log.Warning($"[CelesFD] Orbit crate landing failed: no valid spot within r=10 near {base.Position}");
                    Destroy();
                    return;
                }
                Thing building = ThingMaker.MakeThing(crate.lootTableDef.buildingDef);
                if (crate.playerOwned) building.SetFactionDirect(Faction.OfPlayer);   // FD-G37
                DoImpactExplosion(map, building);   // 先爆（未落格的建筑实例在 ignoredThings——传播期免疫）
                SpawnCrateBuilding(crate, crate.lootTableDef.buildingDef, cell, map, building);   // L1 清障 + 落格
                if (building is CelesFD_Building_OrbitCrate orbitCrate)
                    orbitCrate.FillFromLootTable(crate.lootTableDef);
                Destroy();
                return;
            }
            // 普通建筑模式（晶体/a-2a-3 打击箱）：Building 条目 → pending 免疫爆炸 → L1 落格
            //（W-2b 修复：本分支此前漏调 DoImpactExplosion——a-2/a-3 落地爆未触发的根因）
            CelesFD_DropContentEntry entry = crate.contents?.Find(e => e.type == CelesFD_DropContentType.Building);
            ThingDef buildingDef = entry != null ? DefDatabase<ThingDef>.GetNamedSilentFail(entry.thingDefName) : null;
            if (buildingDef == null)
            {
                Log.Warning($"[CelesFD] Orbit crate {crate.defName} has no building target (lootTableDef or Building entry thingDefName)");
                Destroy();
                return;
            }
            if (!TryFindPlaceNear(base.Position, map, allowFogged: true, out IntVec3 cell2))
            {
                Log.Warning($"[CelesFD] Orbit crate landing failed: no valid spot within r=10 near {base.Position}");
                Destroy();
                return;
            }
            Thing building2 = ThingMaker.MakeThing(buildingDef);
            DoImpactExplosion(map, building2);
            SpawnCrateBuilding(crate, buildingDef, cell2, map, building2);
            Destroy();
        }

        // 落地爆炸（扩展配置；ignoredThings = 即将落格的建筑实例——传播期免疫（自伤根治 A2-19）；
        //   GenExplosion.DoExplosion 签名实证 GenExplosion.cs:14，命名参数防错位；
        //   AP 直传（-1=伤害 def 默认）；气体不走爆炸（独立成圈——见 Impact 纯效果分支）
        private void DoImpactExplosion(Map map, Thing pendingBuilding)
        {
            CelesFD_SkyfallerImpactExtension boom = def.GetModExtension<CelesFD_SkyfallerImpactExtension>();
            if (boom == null || boom.explosionRadius <= 0f || boom.explosionDamage == null) return;
            int damAmount = boom.explosionDamage.defaultDamage > 0
                ? GenMath.RoundRandom(boom.explosionDamage.defaultDamage * boom.explosionDamageFactor)
                : -1;
            List<Thing> ignored = pendingBuilding != null
                ? new List<Thing> { pendingBuilding }
                : null;
            GenExplosion.DoExplosion(base.Position, map, boom.explosionRadius, boom.explosionDamage, null,
                damAmount, boom.armorPenetration, null, null, null, null,
                boom.postExplosionSpawnThingDef, boom.postExplosionSpawnChance, 1,
                null, null, 255,
                applyDamageToExplosionCellsNeighbors: false,
                preExplosionSpawnThingDef: boom.preExplosionSpawnThingDef,
                preExplosionSpawnChance: boom.preExplosionSpawnChance,
                chanceToStartFire: boom.chanceToStartFire, damageFalloff: false, ignoredThings: ignored);
        }

        // L1 清障 + 落格（植物销毁；Filth/物品按原版 SpawningWipes 语义——VanishOrMoveAside）；
        //   preMade = 爆炸前已生成并列入 ignoredThings 的建筑实例（容器分支）；null = 此处现造
        private static Thing SpawnCrateBuilding(CelesFD_DropCrateDef crate, ThingDef buildingDef, IntVec3 cell, Map map, Thing preMade = null)
        {
            // ToList 快照：Destroy 会改写格上物品列表
            foreach (Thing t in cell.GetThingList(map).ToList())
            {
                if (t != null && t.def.category == ThingCategory.Plant)
                    t.Destroy();
            }
            Thing building = preMade != null ? preMade : ThingMaker.MakeThing(buildingDef);
            if (crate.playerOwned && building.Faction == null) building.SetFactionDirect(Faction.OfPlayer);
            return GenSpawn.Spawn(building, cell, map, WipeMode.VanishOrMoveAside);
        }

        // 中心格优先 → r=1..10 螺旋外扩（walkable + 无 edifice）；迷雾两段式（FD-G20）：
        //   第一遍仅非迷雾；全无果且 allowFogged=true 时第二遍才放行迷雾格（中心格同规则）
        private static bool TryFindPlaceNear(IntVec3 center, Map map, bool allowFogged, out IntVec3 result)
        {
            if (CellValid(center, map, allowFogged: false))
            {
                result = center;
                return true;
            }
            for (int r = 1; r <= 10; r++)
            {
                foreach (IntVec3 c in GenRadial.RadialCellsAround(center, r, useCenter: false))
                {
                    if (CellValid(c, map, allowFogged: false))
                    {
                        result = c;
                        return true;
                    }
                }
            }
            if (allowFogged)
            {
                if (CellValid(center, map, allowFogged: true))
                {
                    result = center;
                    return true;
                }
                for (int r = 1; r <= 10; r++)
                {
                    foreach (IntVec3 c in GenRadial.RadialCellsAround(center, r, useCenter: false))
                    {
                        if (CellValid(c, map, allowFogged: true))
                        {
                            result = c;
                            return true;
                        }
                    }
                }
            }
            result = IntVec3.Invalid;
            return false;
        }

        private static bool CellValid(IntVec3 c, Map map, bool allowFogged)
        {
            return c.InBounds(map) && c.Walkable(map) && (allowFogged || !c.Fogged(map)) && c.GetEdifice(map) == null;
        }
    }
}
