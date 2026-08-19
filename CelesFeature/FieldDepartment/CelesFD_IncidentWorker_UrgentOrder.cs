using RimWorld;
using RimWorld.Planet;
using Verse;

namespace CelesFeature
{
    // M6-6 突发订单（v4.3 B1 定稿：原版 Incident 体系——IncidentDef + 自定义 IncidentWorker；
    // 触发 = 叙事者 StorytellerCompProperties_SingleMTB 随机（Storyteller.xml Patch，mtbDays=20，天然兼容 dev 快进）；
    // Worker 内生成已接取突发单实例（TryGenerateUrgentOrder——计入 maxTradeOrder，满则跳过））
    public class CelesFD_IncidentWorker_UrgentOrder : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            return CelesFD_GameComponent.Instance != null;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return false;
            if (!CelesFD_MarketGenerator.TryGenerateUrgentOrder(gc))
                return false;   // 上限满/无模板 → 本次不触发（下个 interval 重试）
            SendStandardLetter(parms, LookTargets.Invalid);   // 突发单无地图目标（F3 Relocation :74 同款签名：IncidentParms, LookTargets, params NamedArgument[]）
            return true;
        }
    }
}
