using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace CelesFeature
{
    // 信标站生成事件（F3）：叙事者 SingleOnceFixed 第 7 天触发，近距选点生成 B 派系（星铃外勤部）信标站
    public class CelesFD_IncidentWorker_Beacon : IncidentWorker
    {
        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            // ① B 派系实例（DefOf 集中引用）
            Faction beacon = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => f.def == CelesFD_DefOf.Celes_BeaconFaction);
            if (beacon == null)
            {
                Log.Error("[CelesFD] Beacon faction 'Celes_BeaconFaction' not found");
                return false;
            }

            // ② 近距选点（正态 [7,18] μ9σ4 + 反向 landmark；初次信标站必定较近）
            if (!CelesFD_TileSelector.TryFindNearTile(out PlanetTile tile))
            {
                Log.Warning("[CelesFD] No valid tile for beacon station");
                return false;
            }

            // ③ 生成 Settlement（继承原版 Settlement 的 WorldObjectDef；DefOf 集中引用）
            WorldObject obj = WorldObjectMaker.MakeWorldObject(CelesFD_DefOf.CelesFD_BeaconStation);
            obj.Tile = tile;
            obj.SetFaction(beacon);
            Find.WorldObjects.Add(obj);
            if (obj is Settlement s)
                s.Name = SettlementNameGenerator.GenerateSettlementName(s);   // 复用 B 派系 settlementNameMaker（CelesSettlement_Namer）
            Log.Message($"[CelesFD] Beacon station generated at tile {tile}, faction={beacon.Name} name={(obj is Settlement ns ? ns.Name : "?")}");

            // ④ 信件（原版 SendStandardLetter：文案从 IncidentDef letterText/letterLabel/letterDef 读，{STATION} 插值站描述，lookTargets 支持"转至事件发生地点"）
            if (obj is Settlement station)
                SendStandardLetter(parms, new LookTargets(new GlobalTargetInfo(station)),
                    CelesFD_BeaconUtility.FormatStationDescription(station).Named("STATION"));
            return true;
        }
    }
}
