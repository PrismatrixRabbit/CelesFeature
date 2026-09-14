using System.Collections.Generic;
using RimWorld;
using Verse;

namespace CelesFeature
{
    // ═══ R-1：人员部署单状态机（GameComponent 持有；17 §2.2 / 18 §3.2）═══
    // Transit（物流段 24h/1h/0）→ Incoming（即将抵达 arrivalDelayTicks，Alert mm:ss）
    //   → Deployed（驻留至 stayUntilTick；Quest 约束生效；单份锁）→ LeavingSoon（<12h 预警 Alert）
    //   → [R-2：离场序列接管]——R-1 期 stayUntil 到点后仅转 Done 占位（离场本体 = R-2 QuestPart_SupportLeave）
    public class CelesFD_PersonnelOrder : IExposable
    {
        public enum Phase { Transit, Incoming, Deployed, LeavingSoon, Done }

        public string supportDefName;
        public Phase phase = Phase.Transit;
        public long startTick;               // 下单
        public long transitEndTick;          // Standard:+60000 / Expedited:+2500 / Instant:+0
        public long incomingStartTick = -1;  // Transit 终点写入；Incoming 段 = def.arrivalDelayTicks
        public long stayUntilTick;           // 落地时写 = 落地 tick + orderedDays×60000
        public int orderedDays;              // 下单天数（计价/展示/hediff severity = days×0.01）
        public float completenessBaseline;   // Σ(weight×amount) 快照
        public List<Pawn> pawns = new List<Pawn>();   // 抵达后写入（LookMode.Reference——死亡/GC 后 null = 损失）
        public List<float> pawnWeights = new List<float>();   // per-pawn 权重快照（与 pawns 索引对齐——C3 修复：消除运行期 kindDef 反查）
        public Map targetMap;                // 下单地图（C1 修复：落地不随玩家切图漂移；Scribe 引用跨档）
        public int boundQuestId = -1;        // 关联 quest id（提前召回提前结束；按 id 解析避免引用 Scribe 不确定性）
        public int recallRefundCredit;       // 提前召回退款暂存（R-2 撤离 letter 末行消费——修复4）

        public const int DayTicks = 60000;
        public const int LeavingWarnTicks = 30000;    // 12h = 30000 ticks

        public CelesFD_SupportDef Def =>
            supportDefName.NullOrEmpty() ? null : DefDatabase<CelesFD_SupportDef>.GetNamedSilentFail(supportDefName);

        // 存活清点（完整度分子——null/Destroyed 剔除）
        public List<Pawn> AlivePawns(List<Pawn> buffer)
        {
            buffer.Clear();
            for (int i = 0; i < pawns.Count; i++)
                if (pawns[i] != null && !pawns[i].Destroyed) buffer.Add(pawns[i]);
            return buffer;
        }

        // 实时完整度 = Σ(权重×存活)/基准（C3 修复：读索引对齐的权重快照——不再按 kindDef 反查 def）
        public float CurrentCompleteness()
        {
            if (completenessBaseline <= 0f) return 0f;
            float sum = 0f;
            for (int i = 0; i < pawns.Count && i < pawnWeights.Count; i++)
                if (pawns[i] != null && !pawns[i].Destroyed) sum += pawnWeights[i];
            return sum / completenessBaseline;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref supportDefName, "supportDefName", null);
            Scribe_Values.Look(ref phase, "phase", Phase.Transit);
            Scribe_Values.Look(ref startTick, "startTick", 0L);
            Scribe_Values.Look(ref transitEndTick, "transitEndTick", 0L);
            Scribe_Values.Look(ref incomingStartTick, "incomingStartTick", -1L);
            Scribe_Values.Look(ref stayUntilTick, "stayUntilTick", 0L);
            Scribe_Values.Look(ref orderedDays, "orderedDays", 1);
            Scribe_Values.Look(ref completenessBaseline, "completenessBaseline", 0f);
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Reference);
            Scribe_Collections.Look(ref pawnWeights, "pawnWeights", LookMode.Value);
            Scribe_References.Look(ref targetMap, "targetMap");
            Scribe_Values.Look(ref boundQuestId, "boundQuestId", -1);
        }
    }

    // ═══ R-1：物流段文案基类（用户裁决"提取抽象类避免代码重复"——17 §2.5 / 18 §3.7）═══
    // 推进逻辑唯一实现（线性均分）；六段/三段 = XML 数据差异（全 Keyed，代码零分支）
    public abstract class CelesFD_TransitStageProvider
    {
        public abstract List<string> StageKeys { get; }   // Keyed 键（线性推进）
        public abstract long TransitTicks { get; }        // 60000 / 2500

        public string CurrentStageKey(long startTick, long now)
        {
            int idx = (int)((now - startTick) * StageKeys.Count / TransitTicks);
            if (idx < 0) idx = 0;
            if (idx >= StageKeys.Count) idx = StageKeys.Count - 1;
            return StageKeys[idx];
        }

        public static CelesFD_TransitStageProvider For(CelesFD_PersonnelArrivalMode mode)
        {
            switch (mode)
            {
                case CelesFD_PersonnelArrivalMode.Expedited: return new CelesFD_TransitStage_Expedited();
                default: return new CelesFD_TransitStage_Standard();   // Instant 无物流段（不建卡）
            }
        }
    }

    // 一般六段（24h 线性均分 = 4h/段）
    public class CelesFD_TransitStage_Standard : CelesFD_TransitStageProvider
    {
        public override long TransitTicks => 60000;
        public override List<string> StageKeys => new List<string>
        {
            "CelesFD_Keyed_PStage1", "CelesFD_Keyed_PStage2", "CelesFD_Keyed_PStage3",
            "CelesFD_Keyed_PStage4", "CelesFD_Keyed_PStage5", "CelesFD_Keyed_PStage6"
        };
    }

    // 加急三段（1h 线性均分 ≈ 20min/段）
    public class CelesFD_TransitStage_Expedited : CelesFD_TransitStageProvider
    {
        public override long TransitTicks => 2500;
        public override List<string> StageKeys => new List<string>
        {
            "CelesFD_Keyed_PStageE1", "CelesFD_Keyed_PStageE2", "CelesFD_Keyed_PStageE3"
        };
    }

    // ═══ 审查 R2 修复（2026-09-06）：物品物流段文案迁移同一基类——两族推进逻辑唯一实现 ═══
    // 原物品版 StageText switch（Page_Logistics）删除；段键沿用既有物品侧 Keyed（行为不变）
    public class CelesFD_TransitStage_ItemStandard : CelesFD_TransitStageProvider
    {
        public override long TransitTicks => 60000;
        public override List<string> StageKeys => new List<string>
        {
            "CelesFD_Keyed_StageConfirming", "CelesFD_Keyed_StageWarehouse", "CelesFD_Keyed_StageFueling",
            "CelesFD_Keyed_StageLoading", "CelesFD_Keyed_StageLaunchQueue"
        };
    }

    public class CelesFD_TransitStage_ItemExpedited : CelesFD_TransitStageProvider
    {
        public override long TransitTicks => 2500;
        public override List<string> StageKeys => new List<string>
        {
            "CelesFD_Keyed_StageSkipPaperwork", "CelesFD_Keyed_StageTossCargo", "CelesFD_Keyed_StageBypassLaunch"
        };
    }

    // ═══ 审查 R3 修复（2026-09-06）：人员投送三连（落点两模式 + 分舱 + 投放）收拢唯一实现 ═══
    // 消费方：R-1 ExecutePersonnelArrival；R-2/R-3（b 类信标投送）直接复用
    public static class CelesFD_PawnDeliveryUtility
    {
        // 落点决策唯一实现（审查抽取 #2——人员/货运/G7 共用；R-2/R-3 新消费者直接复用）
        public static IntVec3 ResolveDropCenter(Map map, CelesFD_PersonnelLandingMode landingMode)
        {
            return landingMode == CelesFD_PersonnelLandingMode.MapEdge
                ? DropCellFinder.FindRaidDropCenterDistant(map)          // 原版空投袭击/援军算法（PawnsArrivalModeWorker_EdgeDrop 同款）
                : DropCellFinder.TradeDropSpot(map);                     // 类货物标准选点（G7 同链）
        }

        public static IntVec3 DeliverPawns(Map map, List<Pawn> pawns, CelesFD_PersonnelLandingMode landingMode, Faction podFaction)
        {
            IntVec3 dropCenter = ResolveDropCenter(map, landingMode);
            var groups = new List<List<Thing>>();
            foreach (Pawn p in pawns) groups.Add(new List<Thing> { p });  // 每人一舱
            DropPodUtility.DropThingGroupsNear(dropCenter, map, groups, faction: podFaction);
            return dropCenter;
        }
    }
}
