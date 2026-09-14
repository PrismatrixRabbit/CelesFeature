using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace CelesFeature
{
    public class CelesFD_GameComponent : GameComponent
    {
        public int Fame;                 // 外勤部声望
        public int Credit;               // 外勤部信用额
        public int QuantumKey;           // 外勤部密钥
        public int UnlockLevelValue;     // 等级上限 LevelCap（Fame/Trade 判定，Phase 2 每次刷新重算）
        public int TradeVolume;          // 外勤部交易额（EffectiveLevelValue 字段已删——v4.7 改派生 GetEffectiveLevel()）
        public int KeyToCredit;          // 1 密钥 = ? 信用额（汇率，待定稿）
        public const int KeyToCreditDefault = 100; // 占位值
        public int BeaconSlotMax = 3;    // 信标站名额上限（变量，默认 3）
        public bool RelocationAsked;     // 搬迁询问标志（全局仅一次，F5）
        public int BeaconCooldownStartTick = -1;   // 信标站冷却开始时刻（-1 = 未冷却；F6）

        // ═══ M5 后半段：开局任务链状态机（2026-08-25） ═══
        public bool CrystalEventTriggered;   // 晶体坠落已触发（道歉信 3700 起算前置）
        public long CrystalEventTick;        // 晶体事件触发时刻（3700 起算=事件触发时刻，用户裁决）
        public bool ApologyTriggered;        // 道歉信已触发
        public long ApologyTriggerTick;      // 道歉信触发时刻（2 象过期起算）
        public int ApologyScenario;          // 剧本分组（0-4，道歉信触发时记录；B 树差分）
        public bool ApologySkipped;          // 道歉信被截断（敌对/派系缺失，2026-08-29 修复）——独立终止标志：
                                             //   终止 3700 tick 空转检查 + 防和解后补触发；不污染 ApologyTriggered（防对话树误判进道歉树）
        // ApologyViewed 无字段——纯对话变量（道歉树 entryComps SetVariable 置位，引擎持久化；根树 conditions 读取）
        public bool BeaconEstablished;       // 信标站已触发（首建发信器，原 F3 第 7 天改挂载）

        // 事件执行失败重试状态（FD-G08/G09 修复 2026-09-02：有限重试 3 次 + 日志上限；
        // [Unsaved]——会话级状态，读档后允许重新尝试，无害）
        [Unsaved]
        private int firstAntennaFails;       // 首建发信器→信标站事件失败计数
        [Unsaved]
        private bool firstAntennaAbandoned;  // 本会话放弃标志（3 次失败后置位）
        [Unsaved]
        private int apologyFails;            // 道歉信事件失败计数
        [Unsaved]
        private bool apologyAbandoned;       // 本会话放弃标志（3 次失败后置位）

        // 开局任务链参数（硬编码占位，后续 XML 化）
        public const int ApologyDelayTicks = 3700;        // 晶体→道歉信间隔（比较式定时，dev 快进跳变兼容）
        public const int ApologyExpireTicks = 1800000;    // 道歉树 2 象过期（1 象 = 900,000 ticks）

        // F6 冷却参数（硬编码占位，后续 XML 化）
        public const int BeaconCooldownTicks = 1800000;        // 冷却期 30 天（1,800,000 tick）
        public const int BeaconCooldownCheckInterval = 1000;   // 期满后随机判定间隔
        public const float BeaconDisappearChance = 0.001f;     // 每次判定消失概率（期望窗口 ~16.7 天）

        // 轮询基准：已知玩家 Settlement ID 集合（[Unsaved]，Start/Load 时重建，不存档）
        [Unsaved]
        private HashSet<int> knownPlayerSettlements = new HashSet<int>();
        
        // 新增字段（类成员区）：
        public List<CelesFD_DialogueEntry> DialogueHistory = new List<CelesFD_DialogueEntry>();

        // M0：本季度要闻 index（-1 = 未初始化懒随机；每象随市场刷新重随机——M2 刷新点挂接 RefreshNews）
        public int CurrentNewsIndex = -1;
        [Unsaved]
        private int lastNewsIndex = -1;   // 防重：上次随机出的要闻 index（运行期，不存档）

        // ═══ M2：市场与订单（v4.3 D1 单 List 队列；刷新触发） ═══
        public List<CelesFD_Order> MarketOrders = new List<CelesFD_Order>();   // 单市场队列（category/isBuy 字段过滤；接取=状态翻转）
        public long LastMarketRefreshTick = -1;    // 上次市场刷新时刻（-1 = 未刷新，开局无条件一次）
        [Unsaved]
        public float LastManualRefreshRealTime = -1f;   // 手动刷新冷却时间戳（真实时间，不随暂停；游戏会话内持续）
        public int ManualRefreshCount;             // 自上次自动刷新以来的手动刷新次数（§5.5：自动刷新归零——2026-08-15 修正，删象限判定）
        public HashSet<int> AcquiredLevels = new HashSet<int>();   // 已获取等级集合（§5.3 首次升级蓝信，M3.5 用）
        public List<CelesFD_Order> ShoppingCart = new List<CelesFD_Order>();   // 购物车（§3.6 独立 List；M5/M7 用，M2 建字段）
        public int NextAcceptOrderIndex;   // 全局接单序号（§5.7 FIFO 单调递增——防删除订单后 Max 复用破坏序；M6 用）
        // M6-2 A5 违约惩罚（§5.8 定稿）：
        public int PunishmentLevel = 1;    // 三级概率惩罚等级 L ∈ {1,2,3}（倍率 1.0x/2.0x/5.0x；触发后 +1 封顶 3；任意订单成功后重置 1）
        public int FailCount;              // 违约累计次数（"第二次失败起"概率触发判定）
        public List<CelesFD_OrderArchiveEntry> OrderArchive = new List<CelesFD_OrderArchiveEntry>();   // 外勤档案（§5.7：超 20 删最旧；M7 物流页历史显示）
        public List<CelesFD_LogisticsOrder> InTransitList = new List<CelesFD_LogisticsOrder>();   // M7 在途物流单（出售物流：到期交付；标准 24h/加急 1h）
        public CelesFD_DialogueEngine DialogueEngine = new CelesFD_DialogueEngine();

        // ═══ W-1：支援系统状态（武备三态：available/pending；draft 为 UI 草稿不存档——§2.3） ═══
        public List<CelesFD_SupportState> SupportStates = new List<CelesFD_SupportState>();

        // ═══ R-1：人员部署单 ═══
        public List<CelesFD_PersonnelOrder> PersonnelOrders = new List<CelesFD_PersonnelOrder>();

        // ═══ R-2：7 日窗口追踪（PendingSettlement）═══
        public List<CelesFD_PendingSettlement> PendingSettlements = new List<CelesFD_PendingSettlement>();

        // ═══ W-2a：支援信标注册表（[Unsaved]——信标本体随地图存档，注册表由信标 SpawnSetup/Destroy 维护；
        //     Alert_SupportIncoming 数据源；含 null/Destroyed 惰性清理，遍历方自防） ═══
        [Unsaved]
        public readonly List<CelesFD_SupportBeacon> SupportBeacons = new List<CelesFD_SupportBeacon>();
        
        public CelesFD_GameComponent(Game game)
        {
            CelesFD_SupportAlertSlots.ClearAll();   // 跨存档静态槽位清理（每次新游戏/读档均执行）
            CelesFD_PersonnelAlertSlots.ClearAll(); // R-1 修复3：人员 Alert 槽位同步清理
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref Fame, "CFD_Fame", 0);
            Scribe_Values.Look(ref Credit, "CFD_Credit", 0);
            // W-3 N3：每象武备额度
            Scribe_Values.Look(ref quotaUsedThisQuadrum, "CFD_quotaUsedThisQuadrum", 0);
            Scribe_Values.Look(ref FirstSupportPurchaseDone, "CFD_FirstSupportPurchaseDone", false);
            Scribe_Values.Look(ref weaponRefreshCount, "CFD_weaponRefreshCount", 0);
            Scribe_Values.Look(ref QuantumKey, "CFD_QuantumKey", 0);
            Scribe_Values.Look(ref UnlockLevelValue, "CFD_UnlockLevelValue", 0);
            Scribe_Values.Look(ref TradeVolume, "CFD_TradeVolume", 0);
            Scribe_Values.Look(ref KeyToCredit, "CFD_KeyToCredit", KeyToCreditDefault);
            Scribe_Values.Look(ref BeaconSlotMax, "CFD_BeaconSlotMax", 3);
            Scribe_Values.Look(ref RelocationAsked, "CFD_RelocationAsked", false);
            Scribe_Values.Look(ref BeaconCooldownStartTick, "CFD_BeaconCooldownStartTick", -1);
            Scribe_Collections.Look(ref DialogueHistory, "CFD_DialogueHistory", LookMode.Deep);
            Scribe_Values.Look(ref CurrentNewsIndex, "CFD_CurrentNewsIndex", -1);
            Scribe_Collections.Look(ref MarketOrders, "CFD_MarketOrders", LookMode.Deep);
            Scribe_Values.Look(ref LastMarketRefreshTick, "CFD_LastMarketRefreshTick", -1L);
            Scribe_Values.Look(ref ManualRefreshCount, "CFD_ManualRefreshCount", 0);
            Scribe_Collections.Look(ref AcquiredLevels, "CFD_AcquiredLevels", LookMode.Value);
            Scribe_Collections.Look(ref ShoppingCart, "CFD_ShoppingCart", LookMode.Deep);
            Scribe_Values.Look(ref NextAcceptOrderIndex, "CFD_NextAcceptOrderIndex", 0);
            Scribe_Values.Look(ref PunishmentLevel, "CFD_PunishmentLevel", 1);
            Scribe_Values.Look(ref FailCount, "CFD_FailCount", 0);
            Scribe_Collections.Look(ref OrderArchive, "CFD_OrderArchive", LookMode.Deep);
            Scribe_Collections.Look(ref InTransitList, "CFD_InTransitList", LookMode.Deep);
            Scribe_Collections.Look(ref SupportStates, "CFD_SupportStates", LookMode.Deep);
            Scribe_Collections.Look(ref PersonnelOrders, "CFD_PersonnelOrders", LookMode.Deep);
            Scribe_Collections.Look(ref PendingSettlements, "CFD_PendingSettlements", LookMode.Deep);
            Scribe_Values.Look(ref CrystalEventTriggered, "CFD_CrystalEventTriggered", false);
            Scribe_Values.Look(ref CrystalEventTick, "CFD_CrystalEventTick", 0L);
            Scribe_Values.Look(ref ApologyTriggered, "CFD_ApologyTriggered", false);
            Scribe_Values.Look(ref ApologyTriggerTick, "CFD_ApologyTriggerTick", 0L);
            Scribe_Values.Look(ref ApologyScenario, "CFD_ApologyScenario", 0);
            Scribe_Values.Look(ref ApologySkipped, "CFD_ApologySkipped", false);
            Scribe_Values.Look(ref BeaconEstablished, "CFD_BeaconEstablished", false);
            if (MarketOrders == null) MarketOrders = new List<CelesFD_Order>();
            if (AcquiredLevels == null) AcquiredLevels = new HashSet<int>();
            if (ShoppingCart == null) ShoppingCart = new List<CelesFD_Order>();
            if (DialogueHistory == null) DialogueHistory = new List<CelesFD_DialogueEntry>();
            if (OrderArchive == null) OrderArchive = new List<CelesFD_OrderArchiveEntry>();
            if (InTransitList == null) InTransitList = new List<CelesFD_LogisticsOrder>();
            if (SupportStates == null) SupportStates = new List<CelesFD_SupportState>();   // FD-G12（2026-09-02）：对称补齐 null 守卫
            // 对话引擎持久化（D6）
            var savedVars = DialogueEngine.ExportVariables();
            Scribe_Collections.Look(ref savedVars, "CFD_DialogueVars", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref DialogueEngine.SavedGameNode, "CFD_SavedGameNode", null);
            string savedTree = DialogueEngine.CurrentTreeName;
            string savedNode = DialogueEngine.CurrentNodeName;
            Scribe_Values.Look(ref savedTree, "CFD_CurrentTree", null);
            Scribe_Values.Look(ref savedNode, "CFD_CurrentNode", null);
            if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
            {
                DialogueEngine.ImportVariables(savedVars);
                DialogueEngine.RestoreState(savedTree, savedNode);
            }
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            KeyToCredit = KeyToCreditDefault;
            RebuildKnownSettlements();   // 开局基地不触发搬迁询问
            LogData("New game initialized");
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            RebuildKnownSettlements();   // 读档基准=当前集合，不误触发
            LogData("Game loaded");
        }

        public override void AppendDebugString(StringBuilder sb)
        {
            base.AppendDebugString(sb);
            sb.AppendLine("CelesFD:");
            sb.AppendLine("  Fame=" + Fame);
            sb.AppendLine("  Credit=" + Credit);
            sb.AppendLine("  QuantumKey=" + QuantumKey);
            sb.AppendLine("  UnlockLevelValue=" + UnlockLevelValue);
            sb.AppendLine("  EffectiveLevelValue(derived)=" + GetEffectiveLevel());
            sb.AppendLine("  TradeVolume=" + TradeVolume);
            sb.AppendLine("  KeyToCredit=" + KeyToCredit);
            sb.AppendLine("  RelocationAsked=" + RelocationAsked);
            sb.AppendLine("  BeaconCooldownStartTick=" + BeaconCooldownStartTick);
        }

        private void LogData(string label)
        {
            Log.Message("[CelesFD] " + label
                + " Fame=" + Fame + " Credit=" + Credit + " Key=" + QuantumKey
                + " Unlock=" + UnlockLevelValue + " Eff=" + GetEffectiveLevel()
                + " Trade=" + TradeVolume + " KeyToCredit=" + KeyToCredit);
        }

        // ═══ F5 搬迁询问轮询：每 250 tick 检测玩家 Settlement 新增 ═══
        public override void GameComponentTick()
        {
            base.GameComponentTick();
            // 快速路径（每 tick）：秒级倒计时到期→执行——消除 00:00 后最多 200 tick 可见延迟
            // 仅在有秒级倒计时订单时激活（人员 Incoming / 货运倒计时中——订单数极少，开销可忽略）
            int now = Find.TickManager.TicksGame;
            bool fastPath = false;
            for (int i = 0; i < PersonnelOrders.Count; i++)
                if (PersonnelOrders[i].phase == CelesFD_PersonnelOrder.Phase.Incoming) { fastPath = true; break; }
            if (!fastPath)
                for (int i = 0; i < InTransitList.Count; i++)
                    if (InTransitList[i].countdownStartTick >= 0) { fastPath = true; break; }
            if (fastPath) TickFastPaths(now);
            if (now % 250 != 0) return; // 节流：~4 秒一次（审批/LeavingSoon/Done 等 250tick 延迟可接受）
            TryCheckRelocation();
            TryTickBeaconCooldown();
            TryCheckMarketRefresh();
            TryCheckOrderDeadlines();   // M6-2：逾期自动违约
            TryCheckLogisticsArrivals();   // M7-3：在途物流到期交付
            TryCheckApologyLetter();       // 开局任务链：3700 定时道歉信
            TryCheckFirstAntenna();        // 开局任务链：首建发信器 → 信标站生成
            TickSupportApprovals();        // W-1：武备申请审批到期 → pending 转 available
            TickPersonnelOrders();         // R-1：人员部署单状态机推进
            CelesFD_Settlement.TickPendingSettlements(this);   // R-2：7 日窗口轮询
            TickPendingPawnRescue();   // R-2 !Spawned 修复：被动追踪者恢复 Spawned 后接走
        }

        // 秒级倒计时快速路径（每 tick——仅覆盖用户可感知的秒级到期：人员落地 + 货运交付）
        private void TickFastPaths(int now)
        {
            // 人员 Incoming 到期 → 落地
            for (int i = PersonnelOrders.Count - 1; i >= 0; i--)
            {
                CelesFD_PersonnelOrder o = PersonnelOrders[i];
                if (o.phase != CelesFD_PersonnelOrder.Phase.Incoming) continue;
                CelesFD_SupportDef def = o.Def;
                if (def != null && now >= o.incomingStartTick + def.arrivalDelayTicks)
                    ExecutePersonnelArrival(o, def);
            }
            // 货运倒计时到期 → 交付（仅倒计时中的单——非倒计时阶段仍走 250 tick 块）
            for (int i = InTransitList.Count - 1; i >= 0; i--)
            {
                CelesFD_LogisticsOrder lo = InTransitList[i];
                if (lo.countdownStartTick < 0) continue;
                if (now >= lo.countdownStartTick + CelesFD_LogisticsOrder.CountdownTicks)
                    if (DeliverLogistics(lo)) InTransitList.RemoveAt(i);
            }
        }

        // ═══ R-1 人员支援：下单/轮询/落地（17 §2.2-§2.4 / 18 §3.3-§3.4）═══

        // 单份锁：同一 def 上一份未结束（Phase != Done）不可再申请（P16/C3 裁决）
        // 修复1：获取部署单当前阶段（null = 无单；卡片层区分"未抵达"/"在场"两种召回按钮表现）
        public CelesFD_PersonnelOrder.Phase? GetPersonnelPhase(string defName)
        {
            for (int i = 0; i < PersonnelOrders.Count; i++)
                if (PersonnelOrders[i].supportDefName == defName
                    && PersonnelOrders[i].phase != CelesFD_PersonnelOrder.Phase.Done)
                    return PersonnelOrders[i].phase;
            return null;
        }

        public bool IsPersonnelDeployed(string defName)
        {
            for (int i = 0; i < PersonnelOrders.Count; i++)
                if (PersonnelOrders[i].supportDefName == defName
                    && PersonnelOrders[i].phase != CelesFD_PersonnelOrder.Phase.Done) return true;
            return false;
        }

        // 下单（校验链：等级 → 单份 → 资金；通过即扣费建单——日单价×天数 + 保险预缴）
        public bool TryOrderPersonnel(CelesFD_SupportDef def, int days, Map map, out string failKey)
        {
            failKey = null;
            if (def == null || def.supportType != CelesFD_SupportType.Personnel) { failKey = "CelesFD_Keyed_PersonnelFailDef"; return false; }
            if (def.unlockLevel > GetEffectiveLevel()) { failKey = "CelesFD_Keyed_ArmoryLevelLocked"; return false; }
            if (IsPersonnelDeployed(def.defName)) { failKey = "CelesFD_Keyed_PersonnelAlreadySent"; return false; }
            if (days < 1 || days > def.maxStayDays) { failKey = "CelesFD_Keyed_PersonnelFailDays"; return false; }
            if (map == null) { failKey = "CelesFD_Keyed_PersonnelFailDef"; return false; }
            // 计价（UI 改造终版）：燃油(×人数) + 雇佣(×天数) + 保险 + 额外（creditCost）；密钥额外（keyCost）
            int headCount = def.personnel.Sum(e => e.amount);
            int cost = def.fuelFeePerPawn * headCount + def.dailyWage * days + def.insuranceCost + def.creditCost;
            int keyCost = def.keyCost;
            if (Credit < cost) { failKey = "CelesFD_Keyed_PersonnelFailCredit"; return false; }
            if (QuantumKey < keyCost) { failKey = "CelesFD_Keyed_PersonnelFailKey"; return false; }
            Credit -= cost;
            if (keyCost > 0) ModifyQuantumKey(-keyCost);
            long now = Find.TickManager.TicksGame;
            var order = new CelesFD_PersonnelOrder
            {
                supportDefName = def.defName,
                orderedDays = days,
                startTick = now,
                targetMap = map,   // C1：落地目标图下单锁定——不随玩家切图漂移
                transitEndTick = now + (def.arrivalMode == CelesFD_PersonnelArrivalMode.Standard ? 60000L
                                       : def.arrivalMode == CelesFD_PersonnelArrivalMode.Expedited ? 2500L : 0L)
            };
            PersonnelOrders.Add(order);
            Log.Message("[CelesFD] Personnel ordered: " + def.defName + " days=" + days + " cost=" + cost
                + " mode=" + def.arrivalMode + " map=" + map.Tile);
            return true;
        }

        // 250 tick 轮询推进（比较式——dev 快进兼容）
        private void TickPersonnelOrders()
        {
            long now = Find.TickManager.TicksGame;
            for (int i = 0; i < PersonnelOrders.Count; i++)
            {
                CelesFD_PersonnelOrder o = PersonnelOrders[i];
                if (o.phase == CelesFD_PersonnelOrder.Phase.Done) continue;   // 修复3：跳过 Done——R-2 结算后统一清理（当前保留数据）
                CelesFD_SupportDef def = o.Def;
                if (def == null) continue;
                switch (o.phase)
                {
                    case CelesFD_PersonnelOrder.Phase.Transit:
                        if (now >= o.transitEndTick)
                        {
                            o.phase = CelesFD_PersonnelOrder.Phase.Incoming; o.incomingStartTick = now;
                            CelesFD_PersonnelAlertSlots.AssignIncoming(o);   // 修复3：槽位分配（独立 Alert 行）
                        }
                        break;
                    case CelesFD_PersonnelOrder.Phase.Incoming:
                        if (now >= o.incomingStartTick + def.arrivalDelayTicks) ExecutePersonnelArrival(o, def);
                        break;
                    case CelesFD_PersonnelOrder.Phase.Deployed:
                        if (now >= o.stayUntilTick - CelesFD_PersonnelOrder.LeavingWarnTicks)
                        {
                            o.phase = CelesFD_PersonnelOrder.Phase.LeavingSoon;
                            CelesFD_PersonnelAlertSlots.AssignLeaving(o);   // 修复3：槽位分配
                        }
                        break;
                    case CelesFD_PersonnelOrder.Phase.LeavingSoon:
                        if (now >= o.stayUntilTick)
                        {
                            // R-1 占位：离场由 quest 内 Delay→Leave（原版边缘走出）接管；R-2 替换 G9 序列后由此触发结算
                            o.phase = CelesFD_PersonnelOrder.Phase.Done;
                            Log.Message("[CelesFD] Personnel stay expired (leave handled by quest until R-2): " + def.defName);
                        }
                        break;
                }
                // 全灭释放（防单份锁死锁；结算本体 R-2）
                if (o.phase == CelesFD_PersonnelOrder.Phase.Deployed || o.phase == CelesFD_PersonnelOrder.Phase.LeavingSoon)
                {
                    var alive = new List<Pawn>();
                    o.AlivePawns(alive);
                    if (alive.Count == 0)
                    {
                        // 全灭 → 立即触发 SupportLeave（方案 B：即时结算 + "已确认全灭"开场句）
                        if (o.boundQuestId >= 0)
                        {
                            foreach (Quest q in Find.QuestManager.QuestsListForReading)
                                if (q.id == o.boundQuestId && q.State == QuestState.Ongoing)
                                {
                                    foreach (QuestPart part in q.PartsListForReading)
                                        if (part is CelesFD_QuestPart_SupportLeave supportLeave)
                                        {
                                            supportLeave.isWiped = true;
                                            supportLeave.ExecuteLeaveSequence();
                                            break;
                                        }
                                    break;
                                }
                        }
                        Log.Message("[CelesFD] Personnel squad wiped → SupportLeave triggered: " + def.defName);
                    }
                }
            }
        }

        // R-2 !Spawned 修复：被动追踪者（被背着/容器内）恢复 Spawned 后接走
        // 遍历 PendingSettlement 的 pendingPawnIds → 发现 Spawned（在地图上）→ 传送或 Lord → 从 pending 移除
        private void TickPendingPawnRescue()
        {
            Faction beacon = CelesFD_BeaconUtility.BeaconFaction;
            if (beacon == null) return;
            foreach (CelesFD_PendingSettlement ps in PendingSettlements)
            {
                for (int i = ps.pendingPawnIds.Count - 1; i >= 0; i--)
                {
                    string id = ps.pendingPawnIds[i];
                    // 搜索所有地图的 Spawned pawn（被放下后在地图上，非 WorldPawns）
                    Pawn pawn = null;
                    foreach (Map map in Find.Maps)
                    {
                        pawn = map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.thingIDNumber.ToString() == id);
                        if (pawn != null) break;
                    }
                    if (pawn == null || pawn.Dead || pawn.Destroyed) continue;
                    // 恢复 Spawned → 尝试接走
                    if (pawn.Faction != beacon) pawn.SetFaction(beacon);
                    if (CelesFD_PawnTeleport.TryTeleportOut(pawn))
                    {
                        // Bug A 修复：Harmony postfix 已同步处理（ExitMap→PassToWorld→postfix→RemoveAt+returnedIds+Differential）
                        // 此处不做任何后续——postfix 是唯一翻案处理点，双重处理导致 IndexOutOfRange
                    }
                    else
                    {
                        // 传送失败 → 创建 Lord 走边缘（后续 PassedToWorld 自然翻案）
                        LordMaker.MakeNewLord(beacon,
                            new LordJob_ExitMapBest(LocomotionUrgency.Walk, canDig: true, canDefendSelf: true),
                            pawn.Map, new[] { pawn });
                        Log.Message("[CelesFD] PendingPawnRescue: Lord created for " + pawn.LabelShort);
                    }
                }
            }
        }

        // 落地执行（探针 fire B+ 演进——P1-P16 全结论内嵌：slate 预置/B+ 绑定/GetBrain/守卫/同操作入舱）
        public void ExecutePersonnelArrival(CelesFD_PersonnelOrder order, CelesFD_SupportDef def)
        {
            Map map = order.targetMap != null ? order.targetMap : (Find.CurrentMap ?? Find.Maps.FirstOrDefault());   // C1：下单图优先
            Faction beacon = CelesFD_BeaconUtility.BeaconFaction;
            QuestScriptDef questDef = CelesFD_DefOf.CelesFD_QuestDef_SupportReinforcement;   // T3：DefOf 引用（加载期校验）
            if (map == null || beacon == null || questDef == null)
            {
                order.phase = CelesFD_PersonnelOrder.Phase.Done;
                Log.Error("[CelesFD] Personnel arrival aborted (map/beacon/questDef missing): " + def.defName);
                return;
            }
            if (!MapUsable(map))   // 订单地图已失效（移出 Find.Maps）→ 零生成，走既有提前撤离结算（满完整度）
            {
                CancelPersonnelNoTarget(order, def);
                return;
            }
            // ① 生成（BeaconFaction——忠实"星铃外勤部"身份；P4：与入舱同一操作）
            var pawns = new List<Pawn>();
            var weights = new List<float>();
            Pawn anchor = null;
            foreach (CelesFD_PersonnelEntry entry in def.personnel)
            {
                for (int i = 0; i < entry.amount; i++)
                {
                    Pawn p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                        entry.pawnKind, beacon, PawnGenerationContext.NonPlayer));
                    // 机械体年龄修正（B——/100 等比映射：0~2500 → 0~25 年；测试实证 2026-09-06）
                    //   走默认生成路径零参数修改，后处理缩放——biologicalAgeRange 因发育阶段冲突不可用
                    if (p.RaceProps.IsMechanoid)
                    {
                        p.ageTracker.AgeBiologicalTicks /= 100;
                        p.ageTracker.AgeChronologicalTicks /= 100;
                    }
                    pawns.Add(p);
                    weights.Add(entry.weight);   // C3：权重快照与 pawns 索引对齐
                    if (entry.isMechanitor && anchor == null) anchor = p;   // B+ 锚定者（仅标记条目——P16 v2）
                }
            }
            // ② 落点两模式 + 分舱 + 投放（R3 修复：收拢 CelesFD_PawnDeliveryUtility——R-2/R-3 b 类直接复用）
            IntVec3 dropCenter = CelesFD_PawnDeliveryUtility.DeliverPawns(map, pawns, def.landingMode, beacon);
            // ③ slate 预置（P1：map 必设；名/描述/到期 letter 全旁路）→ quest 生成（autoAccept 同步翻转）
            var slate = new RimWorld.QuestGen.Slate();
            slate.Set("map", map);
            slate.Set("asker", beacon);
            slate.Set("helpers", pawns.AsEnumerable());
            slate.Set("workers", pawns.Where(p => !p.RaceProps.IsMechanoid).AsEnumerable());   // 机械豁免
            slate.Set("stayTicks", (long)order.orderedDays * CelesFD_PersonnelOrder.DayTicks);
            slate.Set("supportDefName", def.defName);   // R-2：SupportLeave QuestNode 消费
            slate.Set("workTags", def.workTagsToDisable);   // C2：禁工配置 per-def（SupportDef → slate → QuestNode）
            slate.Set("resolvedQuestName", def.LabelCap);
            slate.Set("resolvedQuestDescription",
                "CelesFD_Keyed_PersonnelQuestDesc".Translate(beacon.Name, def.label, order.orderedDays));
            Quest q = QuestUtility.GenerateQuestAndMakeAvailable(questDef, slate);
            order.boundQuestId = q != null ? q.id : -1;   // 提前召回提前结束用
            // ④ B+ 绑定（翻转后；原版 DEV Assign 三行配方 CompOverseerSubject.cs:225-244 + GetBrain/severity 守卫）
            if (anchor != null && !anchor.Destroyed)
            {
                HediffDef boost = CelesFD_DefOf.Celes_FieldAidRestrict;   // T3：DefOf 引用
                if (boost != null)
                {
                    Hediff h = anchor.health.AddHediff(boost, anchor.health.hediffSet.GetBrain());
                    if (h != null) h.Severity = order.orderedDays * 0.01f;   // 天数×0.01（P16 v2——随驻留归零防俘虏白嫖）
                }
                foreach (Pawn mech in pawns)
                {
                    if (!mech.RaceProps.IsMechanoid) continue;
                    mech.GetOverseer()?.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, mech);
                    if (mech.Faction != Faction.OfPlayer) mech.SetFaction(Faction.OfPlayer);
                    anchor.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
                }
            }
            // ⑤ 注册（单据转 Deployed）
            order.pawns.AddRange(pawns);
            order.pawnWeights.AddRange(weights);
            order.completenessBaseline = def.personnel.Sum(e => e.weight * e.amount);
            order.stayUntilTick = Find.TickManager.TicksGame + (long)order.orderedDays * CelesFD_PersonnelOrder.DayTicks;
            order.phase = CelesFD_PersonnelOrder.Phase.Deployed;
            Messages.Message("CelesFD_Keyed_PersonnelArrived".Translate(def.LabelCap), MessageTypeDefOf.PositiveEvent);
            Log.Message("[CelesFD] Personnel arrived: " + def.defName + " x" + pawns.Count
                + " anchor=" + (anchor != null ? anchor.LabelShort : "无") + " quest spawned");
        }

        // ═══ 无降落点结算：抵达时刻订单地图已失效 → 不生成 pawn，复用提前撤离结算 ═══
        // completeness = baseline/baseline = 1.0 → 最高档（fame/保险正常算法）；退款 = dailyWage × orderedDays 全额
        // （抵达时刻 0 天已驻留 → daysLeft = orderedDays，与 TryEarlyRecall 同一算法）；空 edgePawns → 零 PendingSettlement
        private void CancelPersonnelNoTarget(CelesFD_PersonnelOrder order, CelesFD_SupportDef def)
        {
            float baseline = def.personnel.Sum(e => e.weight * e.amount);
            int creditBack = def.dailyWage * order.orderedDays;
            if (creditBack > 0) ModifyCredit(creditBack);
            CelesFD_Settlement.SettlePersonnel(this, def, baseline,
                new List<Pawn>(), new Dictionary<string, float>(),
                baseline, isEarlyRecall: true, recallRefundCredit: creditBack,
                isWiped: false, isNoTarget: true);
            order.phase = CelesFD_PersonnelOrder.Phase.Done;
            Log.Message("[CelesFD] Personnel no-target settle (invalid map): " + def.defName + " refund=" + creditBack);
        }

        // ═══ 开局任务链：道歉信定时触发（3700 比较式——dev 快进跳变后条件立即成立；250 tick 节流精度足够） ═══
        private void TryCheckApologyLetter()
        {
            if (!CrystalEventTriggered || ApologyTriggered || ApologySkipped || apologyAbandoned) return;
            if (Find.TickManager.TicksGame - CrystalEventTick < ApologyDelayTicks) return;
            // 敌对截断（用户裁决：确认星铃非敌对，敌对则截断后续事件——该局道歉信永久不触发，彩蛋树兜底）
            // 2026-08-29 修复：置 ApologySkipped 终止检查（此前每 250 tick 空转 + 刷日志；且和解后补触发不符"截断"语义）
            var beacon = CelesFD_BeaconUtility.BeaconFaction;
            if (beacon == null || beacon.HostileTo(Faction.OfPlayer))
            {
                ApologySkipped = true;
                Log.Message("[CelesFD] Apology letter skipped (beacon faction missing or hostile)");
                return;
            }
            var parms = new IncidentParms { target = Find.World, faction = beacon };
            if (!CelesFD_DefOf.CelesFD_ApologyLetter.Worker.TryExecute(parms))
            {
                // FD-G09 修复（2026-09-02 用户裁决）：重试上限 3 + 日志上限（首次失败与放弃各一条；
                // 原为每 250 tick 无限重试 + Warning 刷屏）。ApologyTriggered 由 Worker 成功时置位
                apologyFails++;
                if (apologyFails == 1)
                    Log.Warning("[CelesFD] Apology letter execution failed; retrying (up to 3 attempts)");
                if (apologyFails >= 3)
                {
                    apologyAbandoned = true;
                    Log.Warning("[CelesFD] Apology letter failed 3 times; giving up this session (research path fallback remains)");
                }
            }
        }

        // ═══ 开局任务链：首建发信器监听（原 F3 第 7 天信标站事件改挂载——持续监听直到首建，研究路径兜底） ═══
        private void TryCheckFirstAntenna()
        {
            if (BeaconEstablished || firstAntennaAbandoned) return;
            ThingDef antennaDef = CelesFD_DefOf.CelesFD_Antenna;
            if (antennaDef == null) return;
            foreach (Map map in Find.Maps)
                if (map.listerBuildings.AllBuildingsColonistOfDef(antennaDef).Count > 0)
                {
                    var parms = new IncidentParms { target = Find.World, faction = CelesFD_BeaconUtility.BeaconFaction };
                    if (!CelesFD_DefOf.CelesFD_BeaconSignal.Worker.TryExecute(parms))
                    {
                        // FD-G08 修复（2026-09-02 用户裁决）：失败不置位 BeaconEstablished——随 250 tick 轮询重试，
                        // 3 次失败后放弃本会话并 Warning（原：先置位 → 单次失败 = 该局信标站永久丢失）
                        firstAntennaFails++;
                        if (firstAntennaFails >= 3)
                        {
                            firstAntennaAbandoned = true;
                            Log.Warning("[CelesFD] Beacon signal failed 3 times after first antenna; giving up this session");
                        }
                        return;
                    }
                    BeaconEstablished = true;
                    Log.Message("[CelesFD] First antenna built → beacon signal triggered");
                    return;
                }
        }

        // ═══ 开局任务链：打开发信器 UI 前刷新对话变量（根树 conditions 依赖；forcePause 下打开时稳定） ═══
        // 单向同步（GameComponent 字段 → 对话变量）：ApologyTriggered/ApologyScenario（Worker 写字段，对话树只读）
        // ApologyViewed/EasterEggDone 为纯对话变量（道歉树/彩蛋树 entryComps/sets 写，引擎持久化——防双向不同步）
        // 动态计算：Level（有效等级 v4.7）/ Relation（PlayerRelationKind 三档——用户裁决仅看关系不看好感度 int）/ ApologyExpired（比较式）
        public void RefreshDialogueVars()
        {
            var engine = DialogueEngine;
            if (engine == null) return;
            engine.SetVariable("ApologyTriggered", ApologyTriggered ? 1f : 0f);
            engine.SetVariable("ApologyScenario", ApologyScenario);
            // 判定结构全部在树 XML（RPN：Not/And/Or 引擎内求值，2026-08-25）——此处仅同步状态变量
            engine.SetVariable("Level", GetEffectiveLevel());
            engine.SetStringVariable("playerFactionName", Faction.OfPlayer?.Name);   // 主树问候插值（无派系 → null 移除）
            var beacon = CelesFD_BeaconUtility.BeaconFaction;
            float relation = 0f;   // 0 敌 / 1 中 / 2 盟
            if (beacon != null)
            {
                switch (beacon.PlayerRelationKind)
                {
                    case FactionRelationKind.Hostile: relation = 0f; break;
                    case FactionRelationKind.Neutral: relation = 1f; break;
                    case FactionRelationKind.Ally: relation = 2f; break;
                }
            }
            engine.SetVariable("Relation", relation);
            // 过期标志（比较式计算——动态时间差不存持久化变量；2 象 = 1,800,000 ticks；ApologyViewed 读对话变量）
            engine.SetVariable("ApologyExpired",
                ApologyTriggered && engine.GetVariable("ApologyViewed") == 0f
                    && Find.TickManager.TicksGame - ApologyTriggerTick >= ApologyExpireTicks ? 1f : 0f);
        }

        // ═══ M7-3 + W-4：在途物流三阶段交付（运输→即将抵达倒计时→落地） ═══
        // W-4 新增：到期后不再直接降落——先 5 秒倒计时（Alert"货物购买 即将抵达：xx秒"），结束后才 DeliverLogistics
        private void TryCheckLogisticsArrivals()
        {
            long now = Find.TickManager.TicksGame;
            for (int i = InTransitList.Count - 1; i >= 0; i--)
            {
                CelesFD_LogisticsOrder lo = InTransitList[i];
                if (now < lo.arrivalTick) continue;   // 阶段 1：运输中
                if (lo.countdownStartTick < 0)
                {
                    lo.countdownStartTick = now;   // 进入阶段 2：启动倒计时（同 tick 不交付）
                    continue;
                }
                if (now < lo.countdownStartTick + CelesFD_LogisticsOrder.CountdownTicks) continue;   // 阶段 2：倒计时中
                if (DeliverLogistics(lo))   // 阶段 3：交付（失败→保留下轮重试）
                    InTransitList.RemoveAt(i);
            }
        }

        // ═══ 无效地图判据：Map 对象移出 Find.Maps 后仍存活（仅判 null 不足）——Disposed + 列表成员双重确认 ═══
        private static bool MapUsable(Map m) => m != null && !m.Disposed && Find.Maps.Contains(m);

        // 品名清单拼接（正常到货/虚空到货 letter 共用——复用三律②：第二消费者出现即提取并迁移全部消费者）
        private static string LogisticsItemNames(CelesFD_LogisticsOrder lo)
            => string.Join(", ", lo.items.Where(i => i.ThingDef != null).Select(i => i.ThingDef.label + "×" + i.amount));

        // 空投交付（G7 链路先例：DropPodUtility.DropThingsNear + TradeDropSpot，DebugActions.cs:96-113）
        // M7 合并：一批物品一次空投（同批次 = 一个物流单）
        private bool DeliverLogistics(CelesFD_LogisticsOrder lo)
        {
            Map map = lo.targetMap != null ? lo.targetMap : Find.CurrentMap;   // C1 修复：下单图优先（旧档 null 回落）
            if (map == null) map = Find.Maps.FirstOrDefault();   // 兜底：太空层无当前地图 → 任意殖民地地图
            if (map == null) return false;
            if (!MapUsable(map))   // 订单地图已失效 → 投向虚空：不落地不退款，订单终结（letter 受 notifyArrival 门控）
            {
                if (lo.notifyArrival)
                    Find.LetterStack.ReceiveLetter("CelesFD_Keyed_OrderArrivedTitle".Translate(),
                        "CelesFD_Keyed_LogisticsVoidArrivedDesc".Translate(LogisticsItemNames(lo)), LetterDefOf.PositiveEvent);
                Log.Message("[CelesFD] Logistics void-delivered (invalid map): " + lo.items.Count + " item(s)");
                return true;
            }
            var things = new List<Thing>();
            foreach (CelesFD_LogisticsItem item in lo.items)
            {
                ThingDef td = item.ThingDef;
                if (td == null) continue;   // 物品 Def 缺失（删档）→ 跳过该物品
                int left = item.amount;
                while (left > 0)
                {
                    // 材质/品质快照发货（2026-08-15：修复 madeFromStuff 报错——MakeThing 显式传材质；品质 SetQuality）
                    // 快照在 item 级（CelesFD_LogisticsItem——订单复制时随件保存）
                    Thing t = ThingMaker.MakeThing(td, item.StuffDef);
                    if (item.qualitySet && t.TryGetComp<CompQuality>() is CompQuality cq)
                        cq.SetQuality(item.qualityCategory, ArtGenerationContext.Colony);
                    t.stackCount = Mathf.Min(left, td.stackLimit);
                    left -= t.stackCount;
                    things.Add(t);
                }
            }
            if (things.Count > 0)
                DropPodUtility.DropThingsNear(CelesFD_PawnDeliveryUtility.ResolveDropCenter(map, CelesFD_PersonnelLandingMode.TradeBeacon), map, things,
                    faction: CelesFD_BeaconUtility.BeaconFaction);
            if (lo.notifyArrival)
            {
                Find.LetterStack.ReceiveLetter("CelesFD_Keyed_OrderArrivedTitle".Translate(),
                    "CelesFD_Keyed_OrderArrivedDesc".Translate(LogisticsItemNames(lo)), LetterDefOf.PositiveEvent);
            }
            Log.Message($"[CelesFD] Logistics arrived: {lo.items.Count} item(s) ({(lo.expedited ? "expedited" : "standard")})");
            return true;
        }

        // ═══ M6-2：逾期检测（deadlineTick 已过 → A5 违约惩罚，逾期文案；倒序遍历防索引错位） ═══
        // M6-6 突发豁免（§5.7）：Urgent 未介入 → 超时无惩罚自动消失；介入后（开始装载/首次发射）→ 正常违约
        private void TryCheckOrderDeadlines()
        {
            for (int i = MarketOrders.Count - 1; i >= 0; i--)
            {
                CelesFD_Order o = MarketOrders[i];
                if (o.state != CelesFD_OrderState.Accepted || o.deadlineTick < 0
                    || Find.TickManager.TicksGame <= o.deadlineTick) continue;
                if (o.TemplateDef != null && o.TemplateDef.category == CelesFD_MarketCategory.Urgent && !o.intervened)
                {
                    MarketOrders.RemoveAt(i);
                    Log.Message("[CelesFD] Urgent order expired without intervention (auto removed, no penalty)");
                    continue;
                }
                ApplyPenalty(o, overdue: true);
            }
        }

        // ═══ M2：市场刷新触发（B3 合并规则——核心 = 每象第三日；保底 = 超一象防快进跳过） ═══
        private void TryCheckMarketRefresh()
        {
            long now = Find.TickManager.TicksGame;
            if (LastMarketRefreshTick < 0)   // 开局无条件一次（无论日期）
            {
                CelesFD_MarketGenerator.RefreshMarket();
                return;
            }
            long next = (LastMarketRefreshTick / 900000L) * 900000L + 3L * 60000L;   // 上次刷新所在象限的第三日时刻
            if (next <= LastMarketRefreshTick) next += 900000L;                       // 已过 → 推到下象
            if (now >= next || now - LastMarketRefreshTick >= 900000L)                // 快进跳过后条件立即成立，补刷
                CelesFD_MarketGenerator.RefreshMarket();
        }

        private void TryCheckRelocation()
        {
            var playerSettlements = Find.WorldObjects.Settlements
                .Where(s => s.Faction != null && s.Faction == Faction.OfPlayer)
                .Select(s => s.ID)
                .ToList();
            bool hasNew = playerSettlements.Any(id => !knownPlayerSettlements.Contains(id));
            knownPlayerSettlements = new HashSet<int>(playerSettlements);   // 先同步基准（无副作用）
            if (!hasNew) return;
            // 前提：存在至少一个信标站时才询问（裁决 2026-08-08）
            if (CelesFD_BeaconUtility.GetStations().Count == 0) return;
            TriggerRelocation();
        }

        public void TriggerRelocation()
        {
            RelocationAsked = true;
            SendRelocationLetter();
            Log.Message("[CelesFD] Relocation asked (once)");
        }

        private void SendRelocationLetter()
        {
            // 触发搬迁询问事件：执行逻辑（查最早站→ChoiceLetter）内聚于 CelesFD_IncidentWorker_RelocationOffer
            var parms = new IncidentParms
            {
                target = Find.World,
                faction = CelesFD_BeaconUtility.BeaconFaction
            };
            CelesFD_DefOf.CelesFD_RelocationOfferSignal.Worker.TryExecute(parms);
        }

        // 轮询基准重建：Start/Load 时以当前玩家 Settlement 集合为基准
        private void RebuildKnownSettlements()
        {
            knownPlayerSettlements = new HashSet<int>(Find.WorldObjects.Settlements
                .Where(s => s.Faction != null && s.Faction == Faction.OfPlayer)
                .Select(s => s.ID));
        }

        // ═══ F6 信标站冷却状态机：复合风险 flag → 倒计时（隐藏）→ 期满随机消失 ═══
        public bool BeaconCooldownActive => BeaconCooldownStartTick >= 0;

        private void TryTickBeaconCooldown()
        {
            bool risk = EvaluateBeaconRisk();
            if (!risk)
            {
                // 风险解除（和解等）→ 清空冷却，下次风险重新读条
                if (BeaconCooldownActive)
                {
                    BeaconCooldownStartTick = -1;
                    Log.Message("[CelesFD] Beacon cooldown cleared (risk resolved)");
                }
                return;
            }
            if (!BeaconCooldownActive)
            {
                // 风险触发 → 开始倒计时（隐藏，不告知玩家）
                BeaconCooldownStartTick = Find.TickManager.TicksGame;
                Log.Message("[CelesFD] Beacon cooldown started (risk active)");
                return;
            }
            // 冷却中：期满后进入随机判定窗口
            int ticksElapsed = Find.TickManager.TicksGame - BeaconCooldownStartTick;
            if (ticksElapsed < BeaconCooldownTicks) return;
            if (Find.TickManager.TicksGame % BeaconCooldownCheckInterval != 0) return;
            if (Rand.Chance(BeaconDisappearChance))
                TryDisappearRandomStation();
        }

        // 复合风险评估（扩展预留——检测点单一收敛，状态机不感知因素构成）：
        //   因素1（当前生效）：与星铃信标站派系敌对（保底语义，正式敌对必撤）
        //   因素2（预留）：派系好感低于阈值（BeaconFaction.PlayerGoodwill < -50）
        //   因素3（预留）：针对星铃的袭击次数（原版 statsRecord.numRaidsEnemy 为全球计数，针对星铃需自记录计数器）
        //   因素4（预留）：外勤部声望过低（Fame < 阈值）
        //   合成：当前"任一命中"；未来可扩展加权评分/阈值截断
        private bool EvaluateBeaconRisk()
        {
            var beacon = CelesFD_BeaconUtility.BeaconFaction;
            return beacon != null && beacon.HostileTo(Faction.OfPlayer);
        }

        // 冷却期满命中：触发"信标站消失"事件（随机选站销毁 + letter，逻辑内聚于 CelesFD_IncidentWorker_BeaconMoved）
        public void TryDisappearRandomStation()
        {
            var parms = new IncidentParms
            {
                target = Find.World,
                faction = CelesFD_BeaconUtility.BeaconFaction
            };
            if (!CelesFD_DefOf.CelesFD_BeaconMovedSignal.Worker.TryExecute(parms))
                BeaconCooldownStartTick = -1;   // 无站可消失 → 冷却无意义，清空
        }

        public static CelesFD_GameComponent Instance
        {
            get
            {
                if (Current.Game == null) return null;
                return Current.Game.GetComponent<CelesFD_GameComponent>();
            }
        }
        
        public void AddDialogue(bool isPlayer, string text)
        {
            DialogueHistory.Add(new CelesFD_DialogueEntry { IsPlayer = isPlayer, Text = text });
            if (DialogueHistory.Count > 300)
                DialogueHistory.RemoveRange(0, DialogueHistory.Count - 300);
        }

        // ═══ M2：手动刷新（§5.5：5s 真实时间冷却 + n 花费公式；余额不足拒绝——裁决 2026-08-14） ═══
        public bool TryManualRefresh()
        {
            if (Time.realtimeSinceStartup - LastManualRefreshRealTime < 5f) return false;   // 冷却 5s 真实时间（不随暂停）
            int n = ManualRefreshCount;
            if (n < 3)
            {
                int cost = 500 + 500 * n * n;
                if (Credit < cost)
                {
                    Messages.Message("CelesFD_Keyed_NoCreditRefresh".Translate(cost), MessageTypeDefOf.RejectInput);
                    return false;
                }
                ModifyCredit(-cost);
            }
            else
            {
                int cost = 2 * n - 4;
                if (QuantumKey < cost)
                {
                    Messages.Message("CelesFD_Keyed_NoKeyRefresh".Translate(cost), MessageTypeDefOf.RejectInput);
                    return false;
                }
                ModifyQuantumKey(-cost);
            }
            ManualRefreshCount++;
            LastManualRefreshRealTime = Time.realtimeSinceStartup;
            CelesFD_MarketGenerator.RefreshMarket(isManual: true);   // 手动刷新不归零（价格递增 500→1500→3500→密钥）
            return true;
        }

        // ═══ v4.7 等级三概念：有效等级（派生，替代 EffectiveLevelValue 存储字段——v4.3 D2 提前落地） ═══
        public int GetEffectiveLevel()
        {
            int level = UnlockLevelValue;
            if (Credit < -2000) return 0;                       // 大额欠款：直降 0 级（§5.1）
            if (Credit < 0) level = Math.Max(0, level - 2);     // 欠款：降 2 级至 0
            return level;
        }

        // 首次升级蓝信（§5.3）：LevelCap 提升 → 该等级及更低去除未获取标签 + 蓝信；AcquiredLevels 存档持久化
        public void NotifyLevelAcquired(int level)
        {
            if (level <= 0)
            {
                AcquiredLevels.Add(0);   // 基础等级 L0 开档默认拥有——只补记不发蓝信（修复：开档误发"提升至 1 级"）
                return;
            }
            int maxAcquired = AcquiredLevels.Count == 0 ? -1 : AcquiredLevels.Max();
            if (level <= maxAcquired) return;
            for (int i = 0; i <= level; i++) AcquiredLevels.Add(i);
            Find.LetterStack.ReceiveLetter(
                "CelesFD_Keyed_LevelUpTitle".Translate(),
                "CelesFD_Keyed_LevelUpDesc".Translate(level + 1),
                LetterDefOf.NeutralEvent);
            Log.Message($"[CelesFD] Level {level} acquired (first time)");
        }

        // 接取入口（§5.2 maxTradeOrder + §5.7 FIFO 全局序；杂项规则只写函数不验证——验证随 M6 接单触发器）
        public bool TryAcceptOrder(CelesFD_Order order)
        {
            if (order == null || order.state != CelesFD_OrderState.Available) return false;
            if (MarketOrders.Count(o => o.state == CelesFD_OrderState.Accepted) >= GetMaxTradeOrder()) return false;
            CelesFD_MarketClassDef def = order.TemplateDef;
            int quadrums = def != null && def.orderDurationInQuadrums > 0 ? def.orderDurationInQuadrums : 1;
            order.state = CelesFD_OrderState.Accepted;
            order.deadlineTick = Find.TickManager.TicksGame + quadrums * 900000L;
            order.acceptOrderIndex = NextAcceptOrderIndex;
            NextAcceptOrderIndex++;
            return true;
        }

        // 锁定切换（§6.3 + v4.3 P1：锁定数 ≥ 该类别方向槽位数 → 满员禁用；仅 Available 单可锁）
        public bool TryToggleLock(CelesFD_Order order)
        {
            if (order == null || order.state != CelesFD_OrderState.Available) return false;
            if (!order.IsLocked)
            {
                CelesFD_MarketClassDef def = order.TemplateDef;
                if (def == null) return false;
                int slots = GetSlotsFor(def.category, def.isBuy);
                int locked = MarketOrders.Count(o => o.IsLocked && o.state == CelesFD_OrderState.Available
                    && o.TemplateDef != null && o.TemplateDef.category == def.category && o.TemplateDef.isBuy == def.isBuy);
                if (locked >= slots) return false;   // 满员禁用（P1）
            }
            order.IsLocked = !order.IsLocked;
            return true;
        }

        // 该类别方向的槽位（有效等级——负债缩水后槽位同步缩小）
        private int GetSlotsFor(CelesFD_MarketCategory cat, bool isBuy)
        {
            CelesFD_UnlockLevelConfigDef config = CelesFD_DefOf.CelesFD_UnlockLevelConfigDefault;
            int eff = GetEffectiveLevel();
            if (config == null || config.unlockLevel == null || eff < 0 || eff >= config.unlockLevel.Count) return 0;
            CelesFD_UnlockLevelDef def = DefDatabase<CelesFD_UnlockLevelDef>.GetNamedSilentFail(config.unlockLevel[eff]);
            if (def == null) return 0;
            switch (cat)
            {
                case CelesFD_MarketCategory.Open:
                    return isBuy ? (def.openSlots?.buy ?? 0) : (def.openSlots?.sell ?? 0);
                case CelesFD_MarketCategory.Internal:
                    return isBuy ? (def.internalSlots?.buy ?? 0) : (def.internalSlots?.sell ?? 0);
                case CelesFD_MarketCategory.Precious:
                    return isBuy ? (def.preciousSlots?.buy ?? 0) : (def.preciousSlots?.sell ?? 0);
                default:
                    return 0;   // EasterEgg/Urgent 无槽位 → 不可锁（彩蛋过期优先，锁定无意义）
            }
        }

        // 手动刷新花费（显示用，§5.5：n<3 → 信用额 500+500n²；n>=3 → 密钥 2n-4）
        public void GetManualRefreshCost(out int creditCost, out int keyCost)
        {
            int n = ManualRefreshCount;
            if (n < 3)
            {
                creditCost = 500 + 500 * n * n;
                keyCost = 0;
            }
            else
            {
                creditCost = 0;
                keyCost = 2 * n - 4;
            }
        }

        // M7-2 购物车下单（用户裁决 2026-08-15）：
        //   分币种扣款——信用额侧（货 credit + 税 + 运费）扣 Credit、密钥侧（货 key）扣 QuantumKey；
        //   "均需足够才能支付"（拒绝欠款，不折算互抵）；每条目一物流单（独立空投）；TradeVolume 同收购公式计入
        public bool TryPlaceShoppingOrder(bool expedited, bool notifyArrival)
        {
            if (ShoppingCart.Count == 0) return false;
            // FD-G11 修复（2026-09-02）：先过滤有效订单（FirstThingDef 非空）再计价扣款——
            // 原：按全部购物车求和扣款，null-def 订单付款不发货且不移出市场（可重复购买重复扣款）；全 null 时已扣款无退款
            List<CelesFD_Order> validOrders = new List<CelesFD_Order>();
            foreach (CelesFD_Order o in ShoppingCart)
                if (o.FirstThingDef != null) validOrders.Add(o);
            if (validOrders.Count == 0) return false;   // 全部无效（Def 已删）：不扣款直接失败
            float creditTotal = validOrders.Sum(o => o.CalcPriceCredit());
            float keyTotal = validOrders.Sum(o => o.CalcPriceKey());
            float silverV = CelesFD_ShippingUtility.CalcTotalValue(validOrders);
            float massW = CelesFD_ShippingUtility.CalcTotalMass(validOrders);
            float shipping = CelesFD_ShippingUtility.CalcShippingCost(silverV, massW) * (expedited ? 2f : 1f);   // 加急 2x（确认）
            float creditBase = creditTotal + keyTotal * KeyToCredit;
            float tax = CelesFD_ShippingUtility.CalcTax(creditBase);   // 税归信用额侧（用户裁决：密钥非特殊高价值货币）
            int creditPay = Mathf.RoundToInt(creditTotal) + Mathf.FloorToInt(tax) + Mathf.FloorToInt(shipping);
            int keyPay = Mathf.RoundToInt(keyTotal);
            // 均需足够才能支付（拒绝欠款）
            if (Credit < creditPay)
            {
                Messages.Message("CelesFD_Keyed_NoCreditOrder".Translate(creditPay), MessageTypeDefOf.RejectInput);
                return false;
            }
            if (QuantumKey < keyPay)
            {
                Messages.Message("CelesFD_Keyed_NoKeyOrder".Translate(keyPay), MessageTypeDefOf.RejectInput);
                return false;
            }
            ModifyCredit(-creditPay);
            if (keyPay > 0) ModifyQuantumKey(-keyPay);
            // TradeVolume（挂账裁决：成功结算按 信用额+密钥×汇率，收购/贩售统一）
            ModifyTradeVolume(Mathf.RoundToInt(creditTotal + keyTotal * KeyToCredit));
            // 生成物流单（同批次合并：一个物流单多物品——一次空投、一个物流卡片，用户裁决；时长 2026-08-15 定稿：加急 1h=2500 / 标准 24h=60000）
            long now = Find.TickManager.TicksGame;
            long duration = expedited ? 2500L : 60000L;
            var lo = new CelesFD_LogisticsOrder
            {
                startTick = now,
                arrivalTick = now + duration,
                expedited = expedited,
                notifyArrival = notifyArrival,
                targetMap = Find.CurrentMap ?? Find.Maps.FirstOrDefault()   // 审查 C1 存量修复：到货图下单锁定
            };
            foreach (CelesFD_Order o in validOrders)
            {
                // 材质/品质快照随订单复制（2026-08-15：发货正确性——修复 madeFromStuff 报错）
                lo.items.Add(new CelesFD_LogisticsItem(o.FirstThingDef.defName, o.amount,
                    o.stuffDefName, o.qualityCategory, o.qualitySet));
                MarketOrders.Remove(o);   // 购买后卡片移出市场（用户裁决：不可重复购买）
            }
            InTransitList.Add(lo);
            ShoppingCart.Clear();
            if (notifyArrival)
                Find.LetterStack.ReceiveLetter("CelesFD_Keyed_OrderPlacedTitle".Translate(),
                    "CelesFD_Keyed_OrderPlacedDesc".Translate(expedited ? "1 小时" : "24 小时"), LetterDefOf.NeutralEvent);
            Log.Message($"[CelesFD] Order placed: {lo.items.Count} item(s) merged in transit, credit -{creditPay}, key -{keyPay}");
            return true;
        }

        // 放弃订单（M6-2：A5 违约惩罚定稿 §5.8——基础惩罚 + 三级概率惩罚 + letter + 音效 + 归档）
        public bool TryAbandonOrder(CelesFD_Order order)
        {
            if (order == null || order.state != CelesFD_OrderState.Accepted) return false;
            ApplyPenalty(order, overdue: false);
            return true;
        }

        // A5 违约结算（放弃/逾期共用，overdue 决定 letter 文案；§5.8 定稿）：
        //   基础惩罚 = 罚声望 round(fameReward×factor×类型倍率) + 罚信用额 round(creditReward×类型倍率)（不罚密钥）
        //   三级概率惩罚（独立机制，不累计于公式）：生涯第二次失败起 0.5 概率按当前 L 倍率（1.0/2.0/5.0）追加，触发后 L+1 封顶 3
        private void ApplyPenalty(CelesFD_Order order, bool overdue)
        {
            CelesFD_MarketClassDef def = order.TemplateDef;
            if (def == null)
            {
                MarketOrders.Remove(order);   // Def 缺失（模板被删）→ 无法取惩罚基数，安全移除
                return;
            }
            float mult = PenaltyMultiplier(def.category);
            int famePenalty = Mathf.RoundToInt(def.fameReward * order.thingExtraFameFactor * mult);
            int creditPenalty = Mathf.RoundToInt(order.CalcPriceCredit() * mult);
            ModifyFame(-famePenalty);
            ModifyCredit(-creditPenalty);
            string extra = null;
            FailCount++;
            if (FailCount >= 2 && Rand.Chance(0.5f))
            {
                int lvl = PunishmentLevel;   // 触发时等级（letter 显示）
                int extraFame = Mathf.RoundToInt(famePenalty * lvl);
                int extraCredit = Mathf.RoundToInt(creditPenalty * lvl);
                ModifyFame(-extraFame);
                ModifyCredit(-extraCredit);
                PunishmentLevel = Mathf.Min(3, PunishmentLevel + 1);
                extra = "CelesFD_Keyed_PenaltyExtraDesc".Translate(lvl, extraFame, extraCredit);
            }
            string title = overdue ? "CelesFD_Keyed_OverdueTitle" : "CelesFD_Keyed_AbandonTitle";
            string body = (overdue ? "CelesFD_Keyed_OverdueDesc" : "CelesFD_Keyed_AbandonDesc")
                .Translate(order.ResolveLabel(), famePenalty, creditPenalty);
            if (extra != null) body += "\n\n" + extra;
            Find.LetterStack.ReceiveLetter(title.Translate(), body, LetterDefOf.NegativeEvent);
            SoundDefOf.Quest_Failed.PlayOneShotOnCamera();
            ArchiveAdd(order, success: false);
            MarketOrders.Remove(order);
            Log.Message($"[CelesFD] Order penalty ({(overdue ? "overdue" : "abandon")}): fame -{famePenalty}, credit -{creditPenalty}" + (extra != null ? " + extra" : ""));
        }

        // A5 类型倍率（§5.8 定稿：开放 0.2 / 内部 0.35 / 珍贵 0.5 / 突发 1.0 / 彩蛋 0.2）
        private static float PenaltyMultiplier(CelesFD_MarketCategory cat)
        {
            switch (cat)
            {
                case CelesFD_MarketCategory.Open: return 0.2f;
                case CelesFD_MarketCategory.Internal: return 0.35f;
                case CelesFD_MarketCategory.Precious: return 0.5f;
                case CelesFD_MarketCategory.Urgent: return 1.0f;
                case CelesFD_MarketCategory.EasterEgg: return 0.2f;
                default: return 0.2f;
            }
        }

        // 外勤档案收纳（§5.7：超 20 条删最旧；M7 物流页历史卡片显示）
        // 2026-08-15：完整信息快照（历史卡片"类同交易页"渲染；absTick = 完成时间"年.象.日"）
        // 类别模式（用户裁决）：名称 = categories 类别名（ResolveLabel 既有逻辑）、icon = 字母序首物（FirstThingDef）——成功/违约统一复用
        private void ArchiveAdd(CelesFD_Order order, bool success)
        {
            OrderArchive.Add(new CelesFD_OrderArchiveEntry(
                order.ResolveLabel(), success, Find.TickManager.TicksGame, Find.TickManager.TicksAbs,
                order.FirstThingDef?.defName, order.ResolveLabel(), order.amount,
                Mathf.RoundToInt(order.CalcPriceCredit()), Mathf.RoundToInt(order.CalcPriceKey())));
            if (OrderArchive.Count > 20)
                OrderArchive.RemoveRange(0, OrderArchive.Count - 20);
        }

        // 订单成功结算（M6-5 结算端调用）：A5 L 重置为 1（§5.8）+ 归档
        public void NotifyOrderSucceeded(CelesFD_Order order)
        {
            PunishmentLevel = 1;
            ArchiveAdd(order, success: true);
        }

        // 当前有效等级的最大承接订单量（§5.2）
        public int GetMaxTradeOrder()
        {
            CelesFD_UnlockLevelConfigDef config = CelesFD_DefOf.CelesFD_UnlockLevelConfigDefault;
            int eff = GetEffectiveLevel();
            if (config == null || config.unlockLevel == null || eff < 0 || eff >= config.unlockLevel.Count) return 0;
            CelesFD_UnlockLevelDef def = DefDatabase<CelesFD_UnlockLevelDef>.GetNamedSilentFail(config.unlockLevel[eff]);
            return def != null ? def.maxTradeOrder : 0;
        }

        // ═══ v4.3 D2 纪律前置：全局量修改收敛 setter ═══
        public void ModifyCredit(int delta) => Credit += delta;
        public void ModifyFame(int delta) => Fame += delta;
        public void ModifyTradeVolume(int delta) => TradeVolume += delta;
        public void ModifyQuantumKey(int delta) => QuantumKey += delta;

        // 随机选取本季度要闻（防重仿欢迎页 PickRandomTicker：池≥2 排除上一条；M2 每象刷新点调用同一方法）
        public void RefreshNews()
        {
            CelesFD_SubPageDef def = CelesFD_DefOf.CelesFD_SubPageWelcome;
            int count = def?.newsPool?.Count ?? 0;
            if (count <= 0) { CurrentNewsIndex = -1; return; }
            if (count == 1) { CurrentNewsIndex = 0; lastNewsIndex = 0; return; }
            int idx;
            do { idx = Rand.RangeInclusive(0, count - 1); }
            while (idx == lastNewsIndex);
            CurrentNewsIndex = idx;
            lastNewsIndex = idx;
            Log.Message("[CelesFD] News refreshed -> index " + idx + " (" + def.newsPool[idx] + ")");
        }

        // ═══ W-1：支援系统（§2.3 三态模型；审批延迟到期转可用；人员侧 R 系列扩展） ═══

        // 状态查询（懒创建：GetSupportState(def, true) 建新条目）
        public CelesFD_SupportState GetSupportState(CelesFD_SupportDef def, bool createIfMissing = true)
        {
            for (int i = 0; i < SupportStates.Count; i++)
                if (SupportStates[i].defName == def.defName) return SupportStates[i];
            if (!createIfMissing) return null;
            CelesFD_SupportState s = new CelesFD_SupportState { defName = def.defName };
            SupportStates.Add(s);
            return s;
        }

        public int AvailableCount(CelesFD_SupportDef def) => GetSupportState(def, false)?.available ?? 0;
        public int PendingCount(CelesFD_SupportDef def) => GetSupportState(def, false)?.pending ?? 0;

        // 施放消耗（Verb_SupportCall.TryCastShot 调用）：前摇走完后原子检测 + 扣减——防两系统并发调用后数量不足
        public bool TryConsumeSupport(CelesFD_SupportDef def)
        {
            CelesFD_SupportState s = GetSupportState(def, false);
            if (s == null || s.available <= 0) return false;
            s.available--;
            return true;
        }

        // ═══ W-3 N1/N3：冷却 + 每象额度（2026-09-04 用户三项新需求）═══

        // 每象已用武备额度（Scribe；自动刷新归零——见 ResetQuadrumCounters）
        public int quotaUsedThisQuadrum;
        // W-5 顺带（2026-09-04）：初次购买赠送呼叫器（一次性 flag——武备方案 §2.1）
        public bool FirstSupportPurchaseDone;
        // 本象武备额度刷新计次（Scribe；自动刷新归零——价格 2n+2 密钥递增）
        public int weaponRefreshCount;
        // 刷新冷却（真实时间 5s——同市场刷新 TryManualRefresh :494 模式）
        [Unsaved]
        private float lastWeaponRefreshRealTime = -1f;

        // 冷却中查询
        public bool IsCooldownActive(CelesFD_SupportDef def)
        {
            CelesFD_SupportState s = GetSupportState(def, false);
            return s != null && s.cooldownUntilTick > Find.TickManager.TicksGame;
        }

        // 冷却剩余（tick；非冷却返回 0）
        public long CooldownTicksRemaining(CelesFD_SupportDef def)
        {
            CelesFD_SupportState s = GetSupportState(def, false);
            if (s == null || s.cooldownUntilTick < 0) return 0;
            return Math.Max(0, s.cooldownUntilTick - Find.TickManager.TicksGame);
        }

        // 当前等级的每象武备上限（查 UnlockLevelDef）
        public int QuotaCap
        {
            get
            {
                int level = GetEffectiveLevel();
                // 按等级查 UnlockLevelDef 的 weaponQuotaPerQuadrum
                CelesFD_UnlockLevelConfigDef config = DefDatabase<CelesFD_UnlockLevelConfigDef>.AllDefsListForReading.FirstOrDefault();
                if (config == null || config.unlockLevel.NullOrEmpty()) return 4;   // 兜底
                string defName = level < config.unlockLevel.Count ? config.unlockLevel[level] : config.unlockLevel[config.unlockLevel.Count - 1];
                CelesFD_UnlockLevelDef levelDef = DefDatabase<CelesFD_UnlockLevelDef>.GetNamedSilentFail(defName);
                return levelDef?.weaponQuotaPerQuadrum ?? 4;
            }
        }

        // 额度剩余
        public int QuotaRemaining => Math.Max(0, QuotaCap - quotaUsedThisQuadrum);

        // 额度可用（不占额度的支援恒可用）
        public bool IsQuotaAvailable(CelesFD_SupportDef def)
        {
            return !def.occupiesQuota || QuotaRemaining > 0;
        }

        // R1 拆两层（交叉评审 2026-09-04）：编排方法——冷却→额度→扣次→副作用（冷却启动+额度计数）
        // Projectile_SupportCaller.Impact 改调此方法；纯扣次 TryConsumeSupport 保留不改（dev 工具/未来路径）
        public bool TryUseSupport(CelesFD_SupportDef def)
        {
            if (IsCooldownActive(def)) return false;
            if (!IsQuotaAvailable(def)) return false;
            if (!TryConsumeSupport(def)) return false;
            // 副作用：启动冷却 + 占用额度
            CelesFD_SupportState s = GetSupportState(def, false);
            if (def.cooldownTicks > 0 && s != null)
                s.cooldownUntilTick = Find.TickManager.TicksGame + def.cooldownTicks;
            if (def.occupiesQuota)
                quotaUsedThisQuadrum++;
            return true;
        }

        // R2 收口（交叉评审）：象限计数器重置——仅在市场自动刷新路径调用（每象第三日）
        // 手动市场刷新（isManual=true）不触发（R3 用户裁决 2026-09-04：否——手动刷市场不白送武备额度）
        public void ResetQuadrumCounters()
        {
            quotaUsedThisQuadrum = 0;
            weaponRefreshCount = 0;
        }

        // 武备额度刷新（N3）：价格 2n+2 密钥（2/4/6/8...递增）+ 5s 真实时间冷却 + 重置 quotaUsed
        // 模式参照 TryManualRefresh（:492-520——5s 冷却/计价/余额拒绝/message 反馈同构）
        public bool TryManualWeaponRefresh()
        {
            if (Time.realtimeSinceStartup - lastWeaponRefreshRealTime < 5f) return false;
            int cost = 2 * weaponRefreshCount + 2;   // n=0→2, 1→4, 2→6...（用户裁决：仅密钥段，无信用额段）
            if (QuantumKey < cost)
            {
                Messages.Message("CelesFD_Keyed_ArmoryNoKeyRefresh".Translate(cost), MessageTypeDefOf.RejectInput);
                return false;
            }
            ModifyQuantumKey(-cost);
            weaponRefreshCount++;
            lastWeaponRefreshRealTime = Time.realtimeSinceStartup;
            quotaUsedThisQuadrum = 0;
            Log.Message("[CelesFD] Weapon quota refreshed: used=0/" + QuotaCap + " cost=" + cost + " keys");
            return true;
        }

        // W-3 武备页批量提交：逐项校验（等级/上限/余额）→ 计总价 → 扣款 → 逐项 pending += draft
        // 模式参照 TryPlaceShoppingOrder（:620-650——先校验后扣款、余额不足零扣款回退）
        public bool TrySubmitSupportOrder(Dictionary<string, int> drafts)
        {
            if (drafts == null || drafts.Count == 0) return false;
            // 1. 校验 + 计价
            int totalCredit = 0, totalKey = 0;
            var validItems = new List<KeyValuePair<CelesFD_SupportDef, int>>();
            foreach (var kv in drafts)
            {
                if (kv.Value <= 0) continue;
                CelesFD_SupportDef def = DefDatabase<CelesFD_SupportDef>.GetNamedSilentFail(kv.Key);
                if (def == null) continue;
                // 等级校验
                if (def.unlockLevel > GetEffectiveLevel()) return false;
                // 上限校验
                CelesFD_SupportState s = GetSupportState(def, false);
                int avail = s?.available ?? 0, pend = s?.pending ?? 0;
                if (avail + pend + kv.Value > def.maxTotal) return false;
                validItems.Add(new KeyValuePair<CelesFD_SupportDef, int>(def, kv.Value));
                totalCredit += def.creditCost * kv.Value;
                totalKey += def.keyCost * kv.Value;
            }
            if (validItems.Count == 0) return false;
            // 2. 余额校验（零扣款回退）
            if (Credit < totalCredit || QuantumKey < totalKey)
            {
                Messages.Message("CelesFD_Keyed_ArmoryInsufficientFunds".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }
            // 3. 扣款
            ModifyCredit(-totalCredit);
            ModifyQuantumKey(-totalKey);
            // 4. 入账 pending
            foreach (var item in validItems)
            {
                CelesFD_SupportState s = GetSupportState(item.Key, true);
                s.pending += item.Value;
                s.pendingSinceTick = Find.TickManager.TicksGame;
            }
            // 5. 初次购买赠送呼叫器（武备方案 §2.1——一次性，flag 短路零开销）
            if (!FirstSupportPurchaseDone)
            {
                FirstSupportPurchaseDone = true;
                DeliverSupportCallerGift();
            }
            return true;
        }

        // 初次购买武备支援：空投赠送一台支援呼叫信标（DeliverLogistics 同款链——TradeDropSpot + BeaconFaction）
        private void DeliverSupportCallerGift()
        {
            Map map = Find.CurrentMap ?? Find.Maps.FirstOrDefault();
            if (map == null)
            {
                Log.Warning("[CelesFD] First support caller gift: no map available");
                return;
            }
            Thing caller = ThingMaker.MakeThing(CelesFD_DefOf.CelesFD_SupportCaller);
            DropPodUtility.DropThingsNear(CelesFD_PawnDeliveryUtility.ResolveDropCenter(map, CelesFD_PersonnelLandingMode.TradeBeacon), map,
                new List<Thing> { caller },
                faction: CelesFD_BeaconUtility.BeaconFaction);
            Find.LetterStack.ReceiveLetter(
                "CelesFD_Keyed_SupportCallerGiftTitle".Translate(),
                "CelesFD_Keyed_SupportCallerGiftDesc".Translate(),
                LetterDefOf.PositiveEvent,
                new LookTargets(new TargetInfo(caller.Position, map)));
            Log.Message("[CelesFD] First support purchase: caller gifted via air drop at " + caller.Position);
        }

        // W-3 武备页提交：draft（尝试申请的）入账 pending + 审批计时起点；上限 maxTotal
        // ═══ 提前召回（k 终版 2026-09-06）：±区域替换按钮 + 确认态；退款 = 雇佣金 × 剩余天数(向下取整)；
        //   R-1 临时 = quest 提前 End（Cleanup→边缘走出）+ 退款 message；R-2 替换正式离场序列并把退款行
        //   附加结算 letter 末行（PersonnelRecallRefund 键复用）。单份锁保底保留（防 UI 强行双开）═══
        public void TryEarlyRecall(CelesFD_SupportDef def)
        {
            CelesFD_PersonnelOrder order = null;
            foreach (CelesFD_PersonnelOrder o in PersonnelOrders)
                if (o.supportDefName == def.defName
                    && (o.phase == CelesFD_PersonnelOrder.Phase.Deployed || o.phase == CelesFD_PersonnelOrder.Phase.LeavingSoon))
                { order = o; break; }
            if (order == null)
            {
                Log.Message("[CelesFD] Early recall: no active order (" + def.defName + ")");
                return;
            }
            int daysLeft = (int)Mathf.Max(0f, (order.stayUntilTick - Find.TickManager.TicksGame) / (float)CelesFD_PersonnelOrder.DayTicks);
            int creditBack = def.dailyWage * daysLeft;
            if (creditBack > 0) ModifyCredit(creditBack);
            order.recallRefundCredit = creditBack;
            Log.Message("[CelesFD] Early recall: " + def.defName + " daysLeft=" + daysLeft + " refund=" + creditBack);
            // R-2：直接找到 QuestPart 并调用公共方法（绕过信号路由——Bug1 附带修复）
            if (order.boundQuestId >= 0)
            {
                foreach (Quest q in Find.QuestManager.QuestsListForReading)
                    if (q.id == order.boundQuestId && q.State == QuestState.Ongoing)
                    {
                        foreach (QuestPart part in q.PartsListForReading)
                            if (part is CelesFD_QuestPart_SupportLeave supportLeave)
                            {
                                supportLeave.isEarlyRecall = true;
                                supportLeave.recallRefundCredit = creditBack;
                                supportLeave.ExecuteLeaveSequence();   // 公共方法直接调用
                                break;
                            }
                        break;
                    }
            }
        }

        // ═══ R-2：结算体系已分离至 CelesFD_Settlement.cs（避免大文件 sed 损坏）═══

        public bool TrySubmitSupportRequest(CelesFD_SupportDef def, int draft)
        {
            if (draft <= 0) return false;
            CelesFD_SupportState s = GetSupportState(def, true);
            if (s.available + s.pending + draft > def.maxTotal) return false;
            s.pending += draft;
            s.pendingSinceTick = Find.TickManager.TicksGame;
            return true;
        }

        // 审批到期轮询（250 tick 节流内）：pendingSinceTick 起算 applyDelayHours 到期 → pending 全部转 available
        // （比较式——dev 快进跳变后条件立即成立，G 系列纪律）
        private void TickSupportApprovals()
        {
            for (int i = 0; i < SupportStates.Count; i++)
            {
                CelesFD_SupportState s = SupportStates[i];
                if (s.pending <= 0 || s.pendingSinceTick < 0) continue;
                CelesFD_SupportDef def = DefDatabase<CelesFD_SupportDef>.GetNamedSilentFail(s.defName);
                if (def == null) continue;   // Def 缺失（删档）→ 状态保留待补
                long delayTicks = (long)(def.applyDelayHours * 2500L);   // 1 游戏小时 = 2500 ticks
                if (delayTicks <= 0 || Find.TickManager.TicksGame - s.pendingSinceTick >= delayTicks)
                {
                    s.available += s.pending;
                    s.pending = 0;
                    s.pendingSinceTick = -1;
                    Log.Message("[CelesFD] Support approved: " + def.defName + " available=" + s.available);
                }
            }
        }
    }
}