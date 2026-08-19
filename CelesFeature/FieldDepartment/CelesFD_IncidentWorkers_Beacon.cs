using RimWorld;
using RimWorld.Planet;
using Verse;

namespace CelesFeature
{
    // ═══ 信标站操作事件（对话/轮询触发 → Worker 执行，原版 Incident 架构）═══
    // 触发方构造 IncidentParms → def.Worker.TryExecute(parms)（仿 FactionDialogMaker.CallForAid）；
    // 执行逻辑（检查→选点→生成/销毁→letter）全部内聚于 TryExecuteWorker，letter 走 SendStandardLetter 读 def 字段

    // 申请信标站：检查名额/信用 → 远点选点 → 生成 → 扣信用 → letter
    public class CelesFD_IncidentWorker_BeaconRequest : IncidentWorker
    {
        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            var gc = CelesFD_GameComponent.Instance;
            if (gc == null) return false;
            int cost = parms is CelesFD_IncidentParms p ? p.creditCost : 0;

            // ① 名额
            if (CelesFD_BeaconUtility.GetStations().Count >= gc.BeaconSlotMax)
            {
                Log.Message("[CelesFD] BeaconRequest FAIL: slot full");
                return false;
            }
            // ② 信用
            if (gc.Credit < cost)
            {
                Log.Message("[CelesFD] BeaconRequest FAIL: credit " + gc.Credit + " < " + cost);
                return false;
            }
            // ③ 远点选点
            if (!CelesFD_TileSelector.TryFindFarTile(out var tile))
            {
                Log.Message("[CelesFD] BeaconRequest FAIL: no far tile");
                return false;
            }
            // ④ 生成 + 命名
            var s = CelesFD_BeaconUtility.GenerateStation(tile);
            // ⑤ 扣信用 + letter（文案从 IncidentDef letter 三件套读）
            gc.Credit -= cost;
            if (s != null)
                SendStandardLetter(parms, new LookTargets(new GlobalTargetInfo(s)),
                    CelesFD_BeaconUtility.FormatStationDescription(s).Named("STATION"));
            Log.Message($"[CelesFD] Beacon requested: {s?.Name} credit→{gc.Credit}");
            return s != null;
        }
    }

    // 放弃信标站：按 beaconTarget 销毁 → letter
    public class CelesFD_IncidentWorker_BeaconAbandon : IncidentWorker
    {
        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            int idx = parms is CelesFD_IncidentParms p ? p.beaconTarget : -1;
            var stations = CelesFD_BeaconUtility.GetStations();
            if (idx < 0 || idx >= stations.Count)
            {
                Log.Message("[CelesFD] BeaconAbandon FAIL: bad beaconTarget " + idx);
                return false;
            }
            var s = stations[idx];
            string desc = CelesFD_BeaconUtility.FormatStationDescription(s);
            PlanetTile tile = s.Tile;   // Destroy 后 WorldObject 失效，先取 tile 供 lookTargets
            s.Destroy();
            SendStandardLetter(parms, new LookTargets(new GlobalTargetInfo(tile)),
                desc.Named("STATION"));
            Log.Message($"[CelesFD] Beacon abandoned: {s.Name}");
            return true;
        }
    }

    // 冷却期满消失：随机选站销毁 → "改变坐标" letter（F6）
    public class CelesFD_IncidentWorker_BeaconMoved : IncidentWorker
    {
        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            var stations = CelesFD_BeaconUtility.GetStations();
            if (stations.Count == 0) return false;
            var s = stations.RandomElement();
            string desc = CelesFD_BeaconUtility.FormatStationDescription(s);
            PlanetTile tile = s.Tile;
            s.Destroy();
            SendStandardLetter(parms, new LookTargets(new GlobalTargetInfo(tile)),
                desc.Named("STATION"));
            Log.Message($"[CelesFD] Beacon station disappeared (cooldown expiry): {s.Name}");
            return true;
        }
    }
}
