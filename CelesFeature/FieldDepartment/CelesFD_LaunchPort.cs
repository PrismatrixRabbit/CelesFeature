using Verse;

namespace CelesFeature
{
    // 对接口扩展：发射平台侧 portOffset（对接口相对本地坐标偏移，随旋转）+ 舱体侧 podPortOffset（自身对接口偏移）
    // 对接判定规则：舱体对接口世界格 == 发射平台对接口世界格 才可放置
    public class CelesFD_LaunchPortExtension : DefModExtension
    {
        // 发射平台：对接口相对本地坐标的偏移（旋转跟随）
        public IntVec3 portOffset = IntVec3.Zero;

        // 舱体：自身对接口相对本地坐标的偏移（旋转跟随）
        public IntVec3 podPortOffset = IntVec3.Zero;

        // 是否是对接口源（发射平台 true / 货运单元 false）
        public bool launchPadSource;
    }

    // 对接口工具：世界格计算 + 对接判定（仿原版 FuelingPortUtility 结构）
    public static class CelesFD_LaunchPortUtility
    {
        // 发射平台对接口世界格（本地偏移按旋转转）
        public static IntVec3 GetLaunchPortCell(Building platform)
        {
            var ext = platform.def.GetModExtension<CelesFD_LaunchPortExtension>();
            if (ext == null)
            {
                return platform.Position;
            }
            return platform.Position + ext.portOffset.RotatedBy(platform.Rotation);
        }

        // 发射平台对接口世界格（静态重载：蓝图/选中重放统一用参数计算——仿原版 GetFuelingPortCell(center, rot)）
        public static IntVec3 GetLaunchPortCell(IntVec3 center, Rot4 rot, ThingDef def)
        {
            var ext = def.GetModExtension<CelesFD_LaunchPortExtension>();
            if (ext == null)
            {
                return center;
            }
            return center + ext.portOffset.RotatedBy(rot);
        }

        // 舱体对接口世界格（放置预览：center + 自身偏移按旋转转）
        public static IntVec3 GetPodPortCell(IntVec3 center, Rot4 rot, ThingDef def)
        {
            var ext = def.GetModExtension<CelesFD_LaunchPortExtension>();
            if (ext == null)
            {
                return center;
            }
            return center + ext.podPortOffset.RotatedBy(rot);
        }

        // 是否是对接口源（发射平台）
        public static bool IsLaunchPadSource(Building b)
        {
            var ext = b.def.GetModExtension<CelesFD_LaunchPortExtension>();
            return ext != null && ext.launchPadSource;
        }

        // 对接判定：地图上是否存在对接口格 == podPortCell 的发射平台
        public static bool AnyLaunchPadPortAt(IntVec3 podPortCell, Map map)
        {
            return GetLaunchPadAt(podPortCell, map) != null;
        }

        // 对接口格 == podPortCell 的发射平台（舱体→平台反向查找）
        public static Building GetLaunchPadAt(IntVec3 podPortCell, Map map)
        {
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                if (IsLaunchPadSource(b) && GetLaunchPortCell(b) == podPortCell)
                {
                    return b;
                }
            }
            return null;
        }

        // 舱体 → 对应发射平台（舱体对接口格 = 平台对接口格）
        public static Building GetLaunchPadForPod(Building pod)
        {
            var ext = pod.def.GetModExtension<CelesFD_LaunchPortExtension>();
            if (ext == null)
            {
                return null;
            }
            IntVec3 podPortCell = pod.Position + ext.podPortOffset.RotatedBy(pod.Rotation);
            return GetLaunchPadAt(podPortCell, pod.Map);
        }
    }
}
