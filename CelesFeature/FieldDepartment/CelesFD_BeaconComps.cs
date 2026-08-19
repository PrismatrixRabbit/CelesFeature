using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace CelesFeature
{
    // ═══ 清单生成器（entryComp）：站列表 → beaconCount + 动态选项（放弃清单） ═══
    public class CelesFD_DialogueActionCompProperties_BeaconList : CelesFD_DialogueActionCompProperties
    {
        public string confirmNode = "N_Beacon_Confirm";   // 选中站进入的复核节点

        public CelesFD_DialogueActionCompProperties_BeaconList() { compClass = typeof(CelesFD_DialogueActionComp_BeaconList); }
    }

    public class CelesFD_DialogueActionComp_BeaconList : CelesFD_DialogueActionComp
    {
        public override bool TryExecute(CelesFD_DialogueEngine engine) => true;

        public override void OnNodeEntered(CelesFD_DialogueEngine engine)
        {
            var stations = CelesFD_BeaconUtility.GetStations();
            engine.SetVariable("beaconCount", stations.Count);
            engine.ClearDynamicOptions();
            var p = (CelesFD_DialogueActionCompProperties_BeaconList)props;
            for (int i = 0; i < stations.Count; i++)
            {
                int idx = i;
                engine.AddDynamicOption(new CelesFD_ResolvedOption
                {
                    Label = CelesFD_BeaconUtility.FormatStationDescription(stations[i]),
                    Next = p.confirmNode,
                    Sets = new List<CelesFD_VarOperationDef>
                    {
                        new CelesFD_VarOperationDef { varName = "beaconTarget", Set = idx }
                    }
                });
            }
        }
    }

    // ═══ 申请（单一 comp，原子：名额/信用/选点→生成→扣信用） ═══
    public class CelesFD_DialogueActionCompProperties_BeaconRequest : CelesFD_DialogueActionCompProperties
    {
        public int creditCost = 5;

        public CelesFD_DialogueActionCompProperties_BeaconRequest() { compClass = typeof(CelesFD_DialogueActionComp_BeaconRequest); }
    }

    public class CelesFD_DialogueActionComp_BeaconRequest : CelesFD_DialogueActionComp
    {
        public override bool TryExecute(CelesFD_DialogueEngine engine)
        {
            var gc = CelesFD_GameComponent.Instance;
            if (gc == null) return false;
            int cost = ((CelesFD_DialogueActionCompProperties_BeaconRequest)props).creditCost;
            // 触发申请事件：执行逻辑（名额/信用检查→远点选点→生成→扣信用→letter）内聚于 CelesFD_IncidentWorker_BeaconRequest
            var parms = new CelesFD_IncidentParms
            {
                target = Find.World,
                faction = CelesFD_BeaconUtility.BeaconFaction,
                creditCost = cost
            };
            bool ok = CelesFD_DefOf.CelesFD_BeaconRequestSignal.Worker.TryExecute(parms);
            Log.Message("[CelesFD] BeaconRequest triggered, success=" + ok);
            return ok;
        }
    }

    // ═══ 放弃（按 beaconTarget 销毁） ═══
    public class CelesFD_DialogueActionCompProperties_BeaconAbandon : CelesFD_DialogueActionCompProperties
    {
        public CelesFD_DialogueActionCompProperties_BeaconAbandon() { compClass = typeof(CelesFD_DialogueActionComp_BeaconAbandon); }
    }

    public class CelesFD_DialogueActionComp_BeaconAbandon : CelesFD_DialogueActionComp
    {
        public override bool TryExecute(CelesFD_DialogueEngine engine)
        {
            int idx = (int)engine.GetVariable("beaconTarget");
            // 触发放弃事件：执行逻辑（按 beaconTarget 销毁→letter）内聚于 CelesFD_IncidentWorker_BeaconAbandon
            var parms = new CelesFD_IncidentParms
            {
                target = Find.World,
                faction = CelesFD_BeaconUtility.BeaconFaction,
                beaconTarget = idx
            };
            bool ok = CelesFD_DefOf.CelesFD_BeaconAbandonSignal.Worker.TryExecute(parms);
            Log.Message("[CelesFD] BeaconAbandon triggered, success=" + ok);
            return ok;
        }
    }

    // ═══ 复核入口（entryComp）：设 beaconTargetName 供 {beaconTargetName} 插值 ═══
    public class CelesFD_DialogueActionCompProperties_BeaconConfirmEntry : CelesFD_DialogueActionCompProperties
    {
        public CelesFD_DialogueActionCompProperties_BeaconConfirmEntry() { compClass = typeof(CelesFD_DialogueActionComp_BeaconConfirmEntry); }
    }

    public class CelesFD_DialogueActionComp_BeaconConfirmEntry : CelesFD_DialogueActionComp
    {
        public override bool TryExecute(CelesFD_DialogueEngine engine) => true;

        public override void OnNodeEntered(CelesFD_DialogueEngine engine)
        {
            int idx = (int)engine.GetVariable("beaconTarget");
            var stations = CelesFD_BeaconUtility.GetStations();
            string name = (idx >= 0 && idx < stations.Count) ? stations[idx].Name : null;
            engine.SetStringVariable("beaconTargetName", name.NullOrEmpty() ? "?" : name);
        }
    }

}
