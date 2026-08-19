using UnityEngine;
using Verse;

namespace CelesFeature
{
    // 放置校验（对接判定）：舱体对接口格必须等于某发射平台的对接口格（仿原版 PlaceWorker_NeedsFuelingPort 结构）
    // ghost 渲染（机制 2）：遍历画所有未选中发射平台的对接口格（选中者由机制 3 基类重放绘制——每平台恰好一格）
    // 额外：画舱体自身对接口点（大型货运单元确认对接位置）
    public class CelesFD_PlaceWorker_NeedsLaunchPort : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef def, IntVec3 center, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            if (def is ThingDef thingDef)
            {
                IntVec3 podPortCell = CelesFD_LaunchPortUtility.GetPodPortCell(center, rot, thingDef);
                if (!CelesFD_LaunchPortUtility.AnyLaunchPadPortAt(podPortCell, map))
                {
                    return "CelesFD_MustPlaceNearLaunchPad".Translate();
                }
            }
            return true;
        }

        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
        {
            Map currentMap = Find.CurrentMap;
            if (currentMap == null)
            {
                return;
            }
            // 舱体侧对接口点（大型舱体对接位置确认；与平台对接口格重叠时同材质叠画，无视觉差异）
            IntVec3 podPortCell = CelesFD_LaunchPortUtility.GetPodPortCell(center, rot, def);
            if (podPortCell.Standable(currentMap))
            {
                CelesFD_PlaceWorker_LaunchPortSource.DrawLaunchPortCell(podPortCell);
            }
            // 机制 2：未选中发射平台的对接口格（选中者由机制 3 基类重放绘制）
            var allBuildingsColonist = currentMap.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < allBuildingsColonist.Count; i++)
            {
                Building b = allBuildingsColonist[i];
                if (!Find.Selector.IsSelected(b)
                    && CelesFD_LaunchPortUtility.IsLaunchPadSource(b)
                    && CelesFD_LaunchPortUtility.GetLaunchPortCell(b).Standable(currentMap))
                {
                    CelesFD_PlaceWorker_LaunchPortSource.DrawLaunchPortCell(CelesFD_LaunchPortUtility.GetLaunchPortCell(b));
                }
            }
        }
    }
}
