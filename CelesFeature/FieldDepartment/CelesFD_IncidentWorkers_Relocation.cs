using RimWorld;
using RimWorld.Planet;
using Verse;

namespace CelesFeature
{
    // ═══ 搬迁事件（F5）：轮询/ChoiceLetter 按钮触发 → Worker 执行 ═══

    // 搬迁询问：发 ChoiceLetter（接受/拒绝双按钮，letterDef=CelesFD_RelocationOffer）
    public class CelesFD_IncidentWorker_RelocationOffer : IncidentWorker
    {
        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            var stations = CelesFD_BeaconUtility.GetStations();
            var oldest = stations.Count > 0 ? stations[0] : null;
            string oldDesc = oldest != null
                ? CelesFD_BeaconUtility.FormatStationDescription(oldest)
                : (string)"CelesFD_Keyed_Letter_NoOldStation".Translate();
            SendStandardLetter(parms,
                oldest != null ? new LookTargets(new GlobalTargetInfo(oldest)) : LookTargets.Invalid,
                oldDesc.Named("OLDSTATION"));
            return true;
        }
    }

    // 搬迁接受：回收最早站 → 近点生成 → letter；无近点则委托 RelocationDone
    public class CelesFD_IncidentWorker_RelocationAccepted : IncidentWorker
    {
        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            var stations = CelesFD_BeaconUtility.GetStations();
            string oldDesc = null;
            if (stations.Count > 0)
            {
                var oldest = stations[0];
                oldDesc = CelesFD_BeaconUtility.FormatStationDescription(oldest);
                oldest.Destroy();
                Log.Message("[CelesFD] Relocation: abandoned oldest station " + oldest.Name);
            }

            if (!CelesFD_TileSelector.TryFindNearTile(out var tile))
            {
                // 无近点可生成（罕见）：已回收旧站，委托"回收完成"事件发 letter（仿 IncidentWorker_CaravanMeeting 委托模式）
                Log.Warning("[CelesFD] Relocation: no valid near tile for new station");
                return CelesFD_DefOf.CelesFD_RelocationDoneSignal.Worker.TryExecute(parms);
            }
            var s = CelesFD_BeaconUtility.GenerateStation(tile);
            if (s == null)
            {
                Log.Warning("[CelesFD] Relocation: station generation returned null");
                return false;
            }
            // 无旧站可回收时退回基础定位文案（模板不含 {OLDSTATION}）
            if (oldDesc == null)
            {
                var basic = CelesFD_DefOf.CelesFD_BeaconRequestSignal;
                IncidentWorker.SendIncidentLetter(basic.letterLabel, basic.letterText, basic.letterDef, parms,
                    new LookTargets(new GlobalTargetInfo(s)), basic,
                    CelesFD_BeaconUtility.FormatStationDescription(s).Named("STATION"));
                return true;
            }
            SendStandardLetter(parms, new LookTargets(new GlobalTargetInfo(s)),
                CelesFD_BeaconUtility.FormatStationDescription(s).Named("STATION"),
                oldDesc.Named("OLDSTATION"));
            return true;
        }
    }

    // 搬迁回收完成：纯 letter（无近点可新建时告知玩家）
    public class CelesFD_IncidentWorker_RelocationDone : IncidentWorker
    {
        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            SendStandardLetter(parms, LookTargets.Invalid);
            return true;
        }
    }
}
