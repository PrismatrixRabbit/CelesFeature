using UnityEngine;
using Verse;

namespace CelesFeature
{
    // 发射平台对接口源（机制 1 + 机制 3）：
    // 放置平台蓝图时画自己的对接口格（PlaceWorker 常规 DrawGhost）；
    // 选中已建平台时由基类重放本 DrawGhost（Thing.DrawExtraSelectionOverlays，drawPlaceWorkersWhileSelected）——
    // 两场景统一用参数（center/rot/def）计算，不依赖 thing。
    // 材质与静态绘制方法供舱体 PlaceWorker（机制 2）复用（仿原版 PlaceWorker_FuelingPort 模式）。
    [StaticConstructorOnStartup]   // LaunchPortCellMaterial 静态资源加载——启动主线程（原版警告修复）
    public class CelesFD_PlaceWorker_LaunchPortSource : PlaceWorker
    {
        private static readonly Material LaunchPortCellMaterial = MaterialPool.MatFrom("UI/Overlays/FuelingPort", ShaderDatabase.Transparent);

        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
        {
            Map currentMap = Find.CurrentMap;
            if (currentMap == null)
            {
                return;
            }
            var ext = def.GetModExtension<CelesFD_LaunchPortExtension>();
            if (ext == null || !ext.launchPadSource)
            {
                return;
            }
            IntVec3 portCell = CelesFD_LaunchPortUtility.GetLaunchPortCell(center, rot, def);
            if (portCell.Standable(currentMap))
            {
                DrawLaunchPortCell(portCell);
            }
        }

        public static void DrawLaunchPortCell(IntVec3 cell)
        {
            Vector3 position = cell.ToVector3ShiftedWithAltitude(AltitudeLayer.MetaOverlays);
            Graphics.DrawMesh(MeshPool.plane10, position, Quaternion.identity, LaunchPortCellMaterial, 0);
        }
    }
}
