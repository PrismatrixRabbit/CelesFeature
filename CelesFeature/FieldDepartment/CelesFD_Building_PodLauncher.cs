using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace CelesFeature
{
    // 星铃发射平台：建造货运单元 gizmo + 自动补建（仿原版 Building_PodLauncher，对接口格用自定义 CelesFD_LaunchPortUtility）
    [StaticConstructorOnStartup]   // AutoBuildIcon 静态资源加载——启动主线程（原版警告修复）
    public class CelesFD_Building_PodLauncher : Building, INotifyLaunchableLaunch
    {
        public bool autoPlacePods;

        private static readonly Texture2D AutoBuildIcon = ContentFinder<Texture2D>.Get("UI/Commands/AutoBuildTransportPod");

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
            Designator_Build designator_Build = BuildCopyCommandUtility.FindAllowedDesignator(CelesFD_DefOf.CelesFD_CargoPod);
            if (designator_Build != null)
            {
                AcceptanceReport acceptanceReport = GenConstruct.CanPlaceBlueprintAt(
                    CelesFD_DefOf.CelesFD_CargoPod,
                    CelesFD_LaunchPortUtility.GetLaunchPortCell(this),
                    CelesFD_DefOf.CelesFD_CargoPod.defaultPlacingRot,
                    base.Map);
                Command_Action command_Action = new Command_Action
                {
                    defaultLabel = "BuildThing".Translate(CelesFD_DefOf.CelesFD_CargoPod.label),
                    icon = designator_Build.icon,
                    defaultDesc = designator_Build.Desc,
                    action = delegate
                    {
                        IntVec3 portCell = CelesFD_LaunchPortUtility.GetLaunchPortCell(this);
                        GenConstruct.PlaceBlueprintForBuild(CelesFD_DefOf.CelesFD_CargoPod, portCell, base.Map, CelesFD_DefOf.CelesFD_CargoPod.defaultPlacingRot, Faction.OfPlayer, null);
                    }
                };
                if (!acceptanceReport.Accepted)
                {
                    command_Action.Disable(acceptanceReport.Reason);
                }
                yield return command_Action;
            }
            yield return new Command_Toggle
            {
                icon = AutoBuildIcon,
                defaultLabel = "AutoBuildTransportPod".Translate(),
                defaultDesc = "AutoBuildTransportPodDesc".Translate(),
                isActive = () => autoPlacePods,
                toggleAction = ToggleAutoBuildTransportPods
            };
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref autoPlacePods, "autoPlacePods", defaultValue: false);
        }

        private void ToggleAutoBuildTransportPods()
        {
            autoPlacePods = !autoPlacePods;
            if (autoPlacePods)
            {
                CheckPlacePod();
            }
        }

        private void CheckPlacePod()
        {
            if (autoPlacePods)
            {
                IntVec3 portCell = CelesFD_LaunchPortUtility.GetLaunchPortCell(this);
                if (GenConstruct.CanPlaceBlueprintAt(CelesFD_DefOf.CelesFD_CargoPod, portCell, CelesFD_DefOf.CelesFD_CargoPod.defaultPlacingRot, base.Map).Accepted)
                {
                    GenConstruct.PlaceBlueprintForBuild(CelesFD_DefOf.CelesFD_CargoPod, portCell, base.Map, CelesFD_DefOf.CelesFD_CargoPod.defaultPlacingRot, Faction.OfPlayer, null);
                }
            }
        }

        public void Notify_LaunchableLaunched(CompLaunchable launchable)
        {
            CheckPlacePod();
        }
    }
}
