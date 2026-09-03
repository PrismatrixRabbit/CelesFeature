using RimWorld;
using RimWorld.Planet;
using Verse;

namespace CelesFeature
{
    // CelesFD 相关 Def 引用集中（原版 DefOf 惯例）：加载期校验 + 单一引用源，替代散落的 GetNamed/defName 字符串比较
    [DefOf]
    public static class CelesFD_DefOf
    {
        public static FactionDef Celes_BeaconFaction;
        public static WorldObjectDef CelesFD_BeaconStation;
        public static JobDef CelesFD_UseAntenna;
        public static ThingDef CelesFD_CargoPod;
        public static LetterDef CelesFD_RelocationOffer;
        public static CelesFD_TickerDef CelesFD_TickerDefault;
        public static CelesFD_SubPageDef CelesFD_SubPageWelcome;
        public static CelesFD_SubPageDef CelesFD_SubPageTrade;
        public static CelesFD_SubPageDef CelesFD_SubPageLogistics;
        public static CelesFD_UnlockLevelConfigDef CelesFD_UnlockLevelConfigDefault;

        public static IncidentDef CelesFD_BeaconRequestSignal;
        public static IncidentDef CelesFD_BeaconAbandonSignal;
        public static IncidentDef CelesFD_RelocationOfferSignal;
        public static IncidentDef CelesFD_RelocationAcceptedSignal;
        public static IncidentDef CelesFD_RelocationDoneSignal;
        public static IncidentDef CelesFD_BeaconMovedSignal;

        // W-1：支援系统
        public static ThingDef CelesFD_SupportCaller;
        public static JobDef CelesFD_SupportCall;
        // W-1.5：投掷式呼叫信标
        public static ThingDef CelesFD_SupportBeacon;

        // 开局任务链（2026-08-25）
        public static ThingDef CelesFD_Antenna;
        public static IncidentDef CelesFD_BeaconSignal;
        public static IncidentDef CelesFD_InitialCrystalOrbitDrop;
        public static IncidentDef CelesFD_ApologyLetter;
    }
}
