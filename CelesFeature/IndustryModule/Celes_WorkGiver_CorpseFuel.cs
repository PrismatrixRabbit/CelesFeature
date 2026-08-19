/* 尸体投入机制暂时注释（K7 搁置，恢复时移除首尾注释）
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace CelesFeature
{
    [DefOf]
    public static class Celes_CrystalDefOf
    {
        public static JobDef Celes_CorpseRefuel;
    }

    // 尸体投入培育器：仅玩家强制（右键尸体 → "优先投入"），AI 不自动执行
    // [事实] 原版强制菜单机制：FloatMenuMakerMap 以 forced:true 调 JobOnThing；
    // HasJobOnThing 恒 false → JobGiver_Work 永不自动派单（WorkGiver_Scanner 基类默认 false）
    public class Celes_WorkGiver_CorpseFuel : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Corpse);

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return false;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!forced || !(t is Corpse corpse) || !t.Spawned)
                return null;
            if (corpse.IsForbidden(pawn))
                return null;
            Thing seeder = FindBestSeeder(pawn, corpse);
            if (seeder == null)
                return null;
            if (!pawn.CanReserve(seeder, 1, -1, null, true) || !pawn.CanReserve(corpse, 1, -1, null, true))
                return null;
            return JobMaker.MakeJob(Celes_CrystalDefOf.Celes_CorpseRefuel, seeder, corpse);
        }

        private static Thing FindBestSeeder(Pawn pawn, Corpse corpse)
        {
            Thing best = null;
            float bestDistSq = float.MaxValue;
            List<Building> buildings = pawn.Map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building building = buildings[i];
                Celes_CompCorpseFuel comp = building.TryGetComp<Celes_CompCorpseFuel>();
                if (comp == null || !comp.CanAcceptCorpse(corpse))
                    continue;
                float distSq = (building.Position - corpse.Position).LengthHorizontalSquared;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = building;
                }
            }
            return best;
        }
    }
}
*/
