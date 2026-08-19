using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
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
        
        public CelesFD_GameComponent(Game game) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref Fame, "CFD_Fame", 0);
            Scribe_Values.Look(ref Credit, "CFD_Credit", 0);
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
            if (MarketOrders == null) MarketOrders = new List<CelesFD_Order>();
            if (AcquiredLevels == null) AcquiredLevels = new HashSet<int>();
            if (ShoppingCart == null) ShoppingCart = new List<CelesFD_Order>();
            if (DialogueHistory == null) DialogueHistory = new List<CelesFD_DialogueEntry>();
            if (OrderArchive == null) OrderArchive = new List<CelesFD_OrderArchiveEntry>();
            if (InTransitList == null) InTransitList = new List<CelesFD_LogisticsOrder>();
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
            if (Find.TickManager.TicksGame % 250 != 0) return; // 节流：~4 秒一次
            TryCheckRelocation();
            TryTickBeaconCooldown();
            TryCheckMarketRefresh();
            TryCheckOrderDeadlines();   // M6-2：逾期自动违约
            TryCheckLogisticsArrivals();   // M7-3：在途物流到期交付
        }

        // ═══ M7-3：在途物流到期交付（自建 tick 计时——天然 dev 快进兼容；倒序遍历防索引错位） ═══
        private void TryCheckLogisticsArrivals()
        {
            long now = Find.TickManager.TicksGame;
            for (int i = InTransitList.Count - 1; i >= 0; i--)
            {
                CelesFD_LogisticsOrder lo = InTransitList[i];
                if (now < lo.arrivalTick) continue;
                if (DeliverLogistics(lo))   // 交付失败（无地图等）→ 保留下轮重试
                    InTransitList.RemoveAt(i);
            }
        }

        // 空投交付（G7 链路先例：DropPodUtility.DropThingsNear + TradeDropSpot，DebugActions.cs:96-113）
        // M7 合并：一批物品一次空投（同批次 = 一个物流单）
        private bool DeliverLogistics(CelesFD_LogisticsOrder lo)
        {
            Map map = Find.CurrentMap;
            if (map == null) map = Find.Maps.FirstOrDefault();   // 太空层无当前地图 → 任意殖民地地图
            if (map == null) return false;
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
                DropPodUtility.DropThingsNear(DropCellFinder.TradeDropSpot(map), map, things,
                    faction: CelesFD_BeaconUtility.BeaconFaction);
            if (lo.notifyArrival)
            {
                string names = string.Join(", ", lo.items
                    .Where(i => i.ThingDef != null).Select(i => i.ThingDef.label + "×" + i.amount));
                Find.LetterStack.ReceiveLetter("CelesFD_Keyed_OrderArrivedTitle".Translate(),
                    "CelesFD_Keyed_OrderArrivedDesc".Translate(names), LetterDefOf.PositiveEvent);
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
            float creditTotal = ShoppingCart.Sum(o => o.CalcPriceCredit());
            float keyTotal = ShoppingCart.Sum(o => o.CalcPriceKey());
            float silverV = CelesFD_ShippingUtility.CalcTotalValue(ShoppingCart);
            float massW = CelesFD_ShippingUtility.CalcTotalMass(ShoppingCart);
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
            // 生成物流单（每条目一单——各自独立空投；标准 24h = 1440000 / 加急 1h = 60000）
            long now = Find.TickManager.TicksGame;
            long duration = expedited ? 2500L : 60000L;   // 2026-08-15 修正：加急 1h=2500 / 标准 24h=60000（草案为准）
            // 同批次合并：一个物流单（多物品）——一次空投、一个物流卡片（用户裁决）
            var lo = new CelesFD_LogisticsOrder
            {
                startTick = now,
                arrivalTick = now + duration,
                expedited = expedited,
                notifyArrival = notifyArrival
            };
            foreach (CelesFD_Order o in ShoppingCart)
            {
                ThingDef td = o.FirstThingDef;
                if (td == null) continue;
                // 材质/品质快照随订单复制（2026-08-15：发货正确性——修复 madeFromStuff 报错）
                lo.items.Add(new CelesFD_LogisticsItem(td.defName, o.amount,
                    o.stuffDefName, o.qualityCategory, o.qualitySet));
                MarketOrders.Remove(o);   // 购买后卡片移出市场（用户裁决：不可重复购买）
            }
            if (lo.items.Count == 0) return false;
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
    }
}