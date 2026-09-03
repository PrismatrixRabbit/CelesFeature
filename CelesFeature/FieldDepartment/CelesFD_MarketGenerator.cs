using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace CelesFeature
{
    // 市场生成引擎（M4 §4.2 七阶段流水线；M2）
    // 触发：开局（LastMarketRefreshTick==-1）/ 周期（B3 合并规则）/ 手动（GameComponent.TryManualRefresh）
    public static class CelesFD_MarketGenerator
    {
        // filter 展开缓存（§3.4：filter 内容静态——thingDefs/categories 与等级无关，Def 级缓存一次；validLevel 变更不影响展开）
        private static readonly Dictionary<CelesFD_MarketEntry, List<ThingDef>> filterCache = new Dictionary<CelesFD_MarketEntry, List<ThingDef>>();

        // 池结构（每次刷新重建）
        private class MarketPools
        {
            public List<CelesFD_MarketClassDef> open = new List<CelesFD_MarketClassDef>();
            public List<CelesFD_MarketClassDef> internalDefs = new List<CelesFD_MarketClassDef>();
            public List<CelesFD_MarketClassDef> precious = new List<CelesFD_MarketClassDef>();
            // easterEgg 池已删（2026-08-15：彩蛋并入 open 池——前置满足入池）
            public List<CelesFD_MarketClassDef> guaranteed = new List<CelesFD_MarketClassDef>();
            public Dictionary<CelesFD_MarketEntry, int> repeatCount = new Dictionary<CelesFD_MarketEntry, int>();   // maxRepeat 运行期计数（每刷新重建）
        }

        // ═══ 主入口：七阶段流水线（周期/开局/手动刷新共用） ═══
        public static void RefreshMarket(bool isManual = false)
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return;
            gc.ShoppingCart.Clear();   // M7-2（用户裁决 2026-08-15）：刷新直接清空购物车且不补充（省去匹配代码）
            long now = Find.TickManager.TicksGame;

            Phase1Cleanup(gc, now);
            TrimOverflow(gc);   // 防御性上限（v4.7）：超阈值裁剪非特殊/非锁定/非已接取，防读档损坏/异常数据
            int level = Phase2Level(gc);            // 等级上限 LevelCap（Fame/Trade 判定）
            gc.NotifyLevelAcquired(level);          // 首次升级蓝信（§5.3，AcquiredLevels 持久化）
            int effLevel = gc.GetEffectiveLevel();  // 有效等级（负债修正派生——市场内容/槽位基准，v4.7 三概念）
            MarketPools pools = Phase3BuildPools(gc, effLevel);
            Phase4Guaranteed(gc, pools.guaranteed);
            Phase5Fill(gc, pools, effLevel);
            Phase7Sort(gc);   // Phase 6 彩蛋已并入 Open 池（2026-08-15 用户裁决——前置满足入池，无独立 Phase 6）

            gc.LastMarketRefreshTick = now;
            if (!isManual) gc.ManualRefreshCount = 0;   // 自动刷新（开局/周期）归零——草案 §5.5"每象第三日刷新点归零"（2026-08-15 修正）
            gc.RefreshNews();   // M0 挂接点：要闻随市场刷新重随机
            Log.Message($"[CelesFD] Market refreshed at tick {now}: {gc.MarketOrders.Count(o => o.state == CelesFD_OrderState.Available)} available orders");
        }

        // ═══ Phase 1 清理 ═══
        private static void Phase1Cleanup(CelesFD_GameComponent gc, long now)
        {
            for (int i = gc.MarketOrders.Count - 1; i >= 0; i--)
            {
                CelesFD_Order o = gc.MarketOrders[i];
                CelesFD_MarketClassDef def = o.TemplateDef;

                // 1. 彩蛋过期先行（裁决 2026-08-14：过期优先，锁定不豁免过期）
                if (o.state == CelesFD_OrderState.Available && def != null
                    && def.category == CelesFD_MarketCategory.EasterEgg
                    && o.deadlineTick >= 0 && now >= o.deadlineTick)
                {
                    gc.MarketOrders.RemoveAt(i);
                    continue;
                }

                // 2. 清理：非特殊/非锁定/非已接取 → 删除（v4.4：保底不豁免，每次刷新重置）
                //    TemplateDef null（Def 缺失）→ 视为非特殊可删（无法履约）
                bool isSpecial = def != null && def.isSpecial;
                if (!isSpecial && !o.IsLocked && o.state == CelesFD_OrderState.Available)
                {
                    gc.MarketOrders.RemoveAt(i);
                    continue;
                }

                // 3. 锁定单保留且锁当场消耗（豁免仅一次，防永久豁免）
                if (o.IsLocked) o.IsLocked = false;
            }
        }

        // 防御性上限（v4.7）：队列超阈值裁剪非特殊/非锁定/非已接取（正常流程 Phase 1 已重置 Available，此为读档损坏/异常数据兜底）
        private const int MaxOrders = 300;

        private static void TrimOverflow(CelesFD_GameComponent gc)
        {
            if (gc.MarketOrders.Count <= MaxOrders) return;
            int removed = gc.MarketOrders.RemoveAll(o =>
                (o.TemplateDef == null || !o.TemplateDef.isSpecial) && !o.IsLocked && o.state == CelesFD_OrderState.Available);
            Log.Warning($"[CelesFD] Market orders exceeded {MaxOrders}, trimmed {removed} non-locked available orders");
        }

        // ═══ Phase 2 等级（v4.7：每次刷新从最高向下重算——升级+降级统一；首项强制兜底） ═══
        private static int Phase2Level(CelesFD_GameComponent gc)
        {
            CelesFD_UnlockLevelConfigDef config = CelesFD_DefOf.CelesFD_UnlockLevelConfigDefault;
            if (config == null || config.unlockLevel == null || config.unlockLevel.Count == 0)
            {
                Log.Error("[CelesFD] UnlockLevelConfig missing or empty");
                return 0;
            }
            // v4.7 修正：每次刷新从最高向下重算（升级+降级统一）——原"当前等级 AND 满足→跳过"使 L0(0/0) 永远满足导致永不升级
            for (int i = config.unlockLevel.Count - 1; i >= 0; i--)
            {
                CelesFD_UnlockLevelDef def = DefDatabase<CelesFD_UnlockLevelDef>.GetNamedSilentFail(config.unlockLevel[i]);
                if (def == null) continue;
                if (i == 0) { gc.UnlockLevelValue = 0; return 0; }
                if (gc.Fame >= def.fameRequire && gc.TradeVolume >= def.tradeRequire)
                {
                    gc.UnlockLevelValue = i;
                    return i;
                }
            }
            gc.UnlockLevelValue = 0;
            return 0;
        }

        // ═══ Phase 3 池构建（分池 + 保底分离 + 特殊需求校验 C5 + repeat 初始化） ═══
        private static MarketPools Phase3BuildPools(CelesFD_GameComponent gc, int level)
        {
            MarketPools pools = new MarketPools();
            CelesFD_UnlockLevelConfigDef config = CelesFD_DefOf.CelesFD_UnlockLevelConfigDefault;
            string levelDefName = (config != null && config.unlockLevel != null && level < config.unlockLevel.Count)
                ? config.unlockLevel[level] : null;

            foreach (CelesFD_MarketClassDef def in DefDatabase<CelesFD_MarketClassDef>.AllDefsListForReading)
            {
                if (def.includeThings == null || def.includeThings.Count == 0) continue;
                if (def.isSpecial) continue;   // B5 预留：未来剧情特殊订单，当前无实例

                // 保底分离（v4.4：不进常规池；validLevel 显式填全部等级为推荐实践，留空=全等级防御）
                if (def.isGuaranteed)
                {
                    if (ValidForLevel(def, levelDefName)) pools.guaranteed.Add(def);
                    continue;
                }

                if (!ValidForLevel(def, levelDefName)) continue;

                // 特殊需求校验（C5 全文）
                if (def.haveSpecialRequire)
                {
                    CelesFD_UnlockLevelDef lvlDef = levelDefName != null
                        ? DefDatabase<CelesFD_UnlockLevelDef>.GetNamedSilentFail(levelDefName) : null;
                    // 校验1（结构）：special 值须均严格大于当前等级基础值 → 不满足 Error 剔除
                    if (lvlDef == null
                        || def.marketSpecialFameRequire <= lvlDef.fameRequire
                        || def.marketSpecialTradeRequire <= lvlDef.tradeRequire)
                    {
                        Log.Error($"[CelesFD] Def {def.defName} special require ({def.marketSpecialFameRequire}/{def.marketSpecialTradeRequire}) not strictly greater than level {levelDefName} base ({lvlDef?.fameRequire}/{lvlDef?.tradeRequire})");
                        continue;
                    }
                    // 校验2（玩家值）：不满足 → 不报错，仅剔除（每次刷新用玩家当前值）
                    if (gc.Fame < def.marketSpecialFameRequire || gc.TradeVolume < def.marketSpecialTradeRequire)
                        continue;
                }

                // maxRepeat 运行期计数初始化（v4.7：必须在 HasUsableEntryIn 之前——IsUsable 依赖计数存在）
                foreach (CelesFD_MarketEntry e in def.includeThings)
                    pools.repeatCount[e] = e.thingMaxRepeat;

                // 至少一个 entry 有展开物品（空展开 Def 无效，跳过——XML 自由哲学作者自查）
                if (!HasUsableEntryIn(def, pools)) continue;

                switch (def.category)
                {
                    case CelesFD_MarketCategory.Open: pools.open.Add(def); break;
                    case CelesFD_MarketCategory.Internal: pools.internalDefs.Add(def); break;
                    case CelesFD_MarketCategory.Precious: pools.precious.Add(def); break;
                    case CelesFD_MarketCategory.EasterEgg:
                        // 彩蛋 = 带前置条件的 Open 订单（2026-08-15 用户裁决：满足前置 → 并入 Open 池复用权重/槽位机制；
                        //   不满足 → 不入池；不再独立生成/不占槽语义作废；无独立过期——与普通订单一致）。
                        // FD-G35（2026-09-02 用户裁决"小概率出现"）：前置满足后还需 easterEggChance 概率掷骰才入池——
                        //   原实现前置满足即必入池，测试池小+槽位≥候选数时每次刷新必出（如 L1 三出售槽对三出售候选 = 100%）
                        if (CheckConditionsAgainstGlobals(def.prerequisiteConditions, gc) && Rand.Chance(def.easterEggChance))
                            pools.open.Add(def);
                        break;
                    // Urgent → 模块 D（M6）不池化
                }
            }
            return pools;
        }

        private static bool ValidForLevel(CelesFD_MarketClassDef def, string levelDefName)
        {
            if (def.validLevel == null || def.validLevel.Count == 0) return true;   // 留空=全等级（保底豁免防御；推荐显式填全部等级）
            return levelDefName != null && def.validLevel.Contains(levelDefName);
        }

        // ═══ Phase 4 保底（v4.4：每次刷新抽样 guaranteedItemCount 个 entry，每 entry 一份独立订单） ═══
        private static void Phase4Guaranteed(CelesFD_GameComponent gc, List<CelesFD_MarketClassDef> guaranteed)
        {
            foreach (CelesFD_MarketClassDef def in guaranteed)
            {
                List<CelesFD_MarketEntry> candidates = def.includeThings.Where(e => IsExpandable(e)).ToList();
                if (candidates.Count == 0) continue;

                int n = def.guaranteedItemCount?.RandomInRange ?? Rand.RangeInclusive(2, 3);
                if (n > candidates.Count)
                {
                    Log.Warning($"[CelesFD] {def.defName} guaranteedItemCount {n} exceeds entry count {candidates.Count}, taking all");
                    n = candidates.Count;
                }

                var remaining = new List<CelesFD_MarketEntry>(candidates);
                for (int i = 0; i < n && remaining.Count > 0; i++)
                {
                    if (!remaining.TryRandomElementByWeight(e => e.thingWeight, out CelesFD_MarketEntry entry))
                        break;
                    remaining.Remove(entry);   // 移除式不重复抽样
                    MakeOrderAndAdd(gc, def, entry);
                }
            }
        }

        // ═══ Phase 5 填充（v4.1 槽位 buy/sell 独立 + v4.3 P1 填充扣除锁定单数 + 珍贵随机挤占） ═══
        private static void Phase5Fill(CelesFD_GameComponent gc, MarketPools pools, int level)
        {
            CelesFD_UnlockLevelConfigDef config = CelesFD_DefOf.CelesFD_UnlockLevelConfigDefault;
            if (config == null || config.unlockLevel == null || level >= config.unlockLevel.Count) return;
            CelesFD_UnlockLevelDef lvl = DefDatabase<CelesFD_UnlockLevelDef>.GetNamedSilentFail(config.unlockLevel[level]);
            if (lvl == null) return;

            int openBuy = lvl.openSlots?.buy ?? 0;
            int openSell = lvl.openSlots?.sell ?? 0;
            int internalBuy = lvl.internalSlots?.buy ?? 0;
            int internalSell = lvl.internalSlots?.sell ?? 0;
            int preciousBuy = lvl.preciousSlots?.buy ?? 0;
            int preciousSell = lvl.preciousSlots?.sell ?? 0;

            // P1 边界：锁定单数本身 > 槽位数 → dev 断言（UI 满员禁用后不可达）
            AssertLockedWithinSlots(gc, CelesFD_MarketCategory.Precious, true, preciousBuy);
            AssertLockedWithinSlots(gc, CelesFD_MarketCategory.Precious, false, preciousSell);
            AssertLockedWithinSlots(gc, CelesFD_MarketCategory.Internal, true, internalBuy);
            AssertLockedWithinSlots(gc, CelesFD_MarketCategory.Internal, false, internalSell);
            AssertLockedWithinSlots(gc, CelesFD_MarketCategory.Open, true, openBuy);
            AssertLockedWithinSlots(gc, CelesFD_MarketCategory.Open, false, openSell);

            // 1. 珍贵固定槽（独立预算；P1：填充数 = max(0, 槽位 - 该方向锁定单数)）
            FillFromPool(gc, pools.precious, true, Max0(preciousBuy - CountLocked(gc, CelesFD_MarketCategory.Precious, true)), pools);
            FillFromPool(gc, pools.precious, false, Max0(preciousSell - CountLocked(gc, CelesFD_MarketCategory.Precious, false)), pools);

            // 2. 珍贵随机（marketPreciousChance 每轮 -0.35，命中 → 抽一单挤占对应方向开放槽——须在开放填充前）
            float chance = lvl.marketPreciousChance;
            while (chance >= 0f && pools.precious.Count > 0)
            {
                float r = Rand.Range(0f, 100f);
                if (r < chance * 100f)
                {
                    CelesFD_MarketClassDef def = PickDef(pools.precious, null, pools);
                    if (def != null)
                    {
                        CelesFD_MarketEntry entry = PickEntry(def, pools);
                        if (entry != null)
                        {
                            MakeOrderAndAdd(gc, def, entry);
                            if (def.isBuy) openBuy--; else openSell--;
                        }
                    }
                }
                chance -= 0.35f;
            }

            // 3. 内部池按 internalSlots.buy/sell 分别填满
            FillFromPool(gc, pools.internalDefs, true, Max0(internalBuy - CountLocked(gc, CelesFD_MarketCategory.Internal, true)), pools);
            FillFromPool(gc, pools.internalDefs, false, Max0(internalSell - CountLocked(gc, CelesFD_MarketCategory.Internal, false)), pools);

            // 4. 开放池按 openSlots.buy/sell 分别填满（已扣珍贵随机挤占；池空不互借）
            FillFromPool(gc, pools.open, true, Max0(openBuy - CountLocked(gc, CelesFD_MarketCategory.Open, true)), pools);
            FillFromPool(gc, pools.open, false, Max0(openSell - CountLocked(gc, CelesFD_MarketCategory.Open, false)), pools);

            // CheckOverflow dev 断言（v4.3 P1 修正后不可达——触发即填充逻辑 bug）
            AssertNotOverflow(gc, CelesFD_MarketCategory.Precious, true, preciousBuy);
            AssertNotOverflow(gc, CelesFD_MarketCategory.Precious, false, preciousSell);
            AssertNotOverflow(gc, CelesFD_MarketCategory.Internal, true, internalBuy);
            AssertNotOverflow(gc, CelesFD_MarketCategory.Internal, false, internalSell);
            AssertNotOverflow(gc, CelesFD_MarketCategory.Open, true, openBuy);
            AssertNotOverflow(gc, CelesFD_MarketCategory.Open, false, openSell);
        }

        private static int Max0(int v) => v > 0 ? v : 0;

        private static int CountLocked(CelesFD_GameComponent gc, CelesFD_MarketCategory cat, bool isBuy)
            => gc.MarketOrders.Count(o => o.IsLocked && o.state == CelesFD_OrderState.Available
                && o.TemplateDef != null && !o.TemplateDef.isGuaranteed   // 保底不占槽（v4.7）
                && o.TemplateDef.category == cat && o.TemplateDef.isBuy == isBuy);

        private static void AssertLockedWithinSlots(CelesFD_GameComponent gc, CelesFD_MarketCategory cat, bool isBuy, int slots)
        {
            int locked = CountLocked(gc, cat, isBuy);
            if (locked > slots)
                Log.Error($"[CelesFD] CheckOverflow(locked): {cat} {isBuy} locked {locked} > slot {slots} — locked cap violated");
        }

        private static void AssertNotOverflow(CelesFD_GameComponent gc, CelesFD_MarketCategory cat, bool isBuy, int slots)
        {
            int count = gc.MarketOrders.Count(o => o.state == CelesFD_OrderState.Available
                && o.TemplateDef != null && !o.TemplateDef.isGuaranteed   // 保底不占槽（v4.7 修正——排除后断言恢复不可达）
                && o.TemplateDef.category == cat && o.TemplateDef.isBuy == isBuy);
            if (count > slots)
                Log.Error($"[CelesFD] CheckOverflow: {cat} {isBuy} {count} > slot {slots} — fill logic bug");
        }

        // 通用填充：target 为 P1 扣除锁定后的新增额度（循环 target 次；池空不互借；Def 耗尽出池）
        private static void FillFromPool(CelesFD_GameComponent gc, List<CelesFD_MarketClassDef> pool, bool isBuy, int target, MarketPools pools)
        {
            if (target <= 0 || pool.Count == 0) return;
            for (int i = 0; i < target; i++)
            {
                CelesFD_MarketClassDef def = PickDef(pool, isBuy, pools);
                if (def == null) break;
                CelesFD_MarketEntry entry = PickEntry(def, pools);
                if (entry == null)
                {
                    pool.Remove(def);   // 全部 entry 耗尽 → Def 出池
                    i--;
                    continue;
                }
                MakeOrderAndAdd(gc, def, entry);
            }
        }

        // ═══ OP_ExtractItem（M4 §4.3 两步定稿）：Def 权重（validLevelWeightFactor）→ entry 权重（thingWeight）→ filter 内等权重 ═══
        private static CelesFD_MarketClassDef PickDef(List<CelesFD_MarketClassDef> pool, bool? isBuy, MarketPools pools)
        {
            var candidates = pool.Where(d => (!isBuy.HasValue || d.isBuy == isBuy.Value) && HasUsableEntryIn(d, pools)).ToList();
            if (candidates.Count == 0) return null;
            return candidates.TryRandomElementByWeight(d => d.validLevelWeightFactor, out var def) ? def : null;
        }

        private static CelesFD_MarketEntry PickEntry(CelesFD_MarketClassDef def, MarketPools pools)
        {
            var usable = def.includeThings.Where(e => IsUsable(e, pools)).ToList();
            if (usable.Count == 0) return null;
            if (!usable.TryRandomElementByWeight(e => e.thingWeight, out CelesFD_MarketEntry entry)) return null;
            pools.repeatCount[entry] = pools.repeatCount[entry] - 1;   // maxRepeat - 1（归零后 IsUsable 排除）
            return entry;
        }

        private static bool HasUsableEntryIn(CelesFD_MarketClassDef def, MarketPools pools)
            => def.includeThings.Any(e => IsUsable(e, pools));

        private static bool IsUsable(CelesFD_MarketEntry e, MarketPools pools)
        {
            if (!IsExpandable(e)) return false;
            return pools.repeatCount.TryGetValue(e, out int r) && r > 0;
        }

        private static bool IsExpandable(CelesFD_MarketEntry e)
        {
            var ex = GetExpand(e);
            return ex != null && ex.Count > 0;
        }

        private static List<ThingDef> GetExpand(CelesFD_MarketEntry entry)
        {
            if (entry.filter == null) return null;
            if (!filterCache.TryGetValue(entry, out var list))
            {
                list = entry.filter.AllowedThingDefs.ToList();
                filterCache[entry] = list;
            }
            return list;
        }

        // ═══ Phase 6 彩蛋（2026-08-15 作废并入 Open 池——前置满足入池权重竞争；letter 迁移至 MakeOrderAndAdd；无独立过期） ═══

        // 彩蛋前置评估器（结构复用 DialogueEngine.CheckConditions 的 Equal/Gte 语义；取值源 = GameComponent 全局量）
        private static bool CheckConditionsAgainstGlobals(List<CelesFD_VarOperationDef> conds, CelesFD_GameComponent gc)
        {
            if (conds == null || conds.Count == 0) return true;
            foreach (CelesFD_VarOperationDef c in conds)
            {
                float val = GetGlobalVariable(c.varName, gc);
                bool pass;
                if (c.Equal.HasValue) pass = val == c.Equal.Value;
                else if (c.Gte.HasValue) pass = val >= c.Gte.Value;
                else { Log.Error($"[CelesFD] Condition on '{c.varName}' has no Equal/Gte"); pass = true; }
                if (!pass) return false;
            }
            return true;
        }

        private static float GetGlobalVariable(string varName, CelesFD_GameComponent gc)
        {
            switch (varName)
            {
                case "Fame": return gc.Fame;
                case "Credit": return gc.Credit;
                case "TradeVolume": return gc.TradeVolume;
                case "QuantumKey": return gc.QuantumKey;
                case "UnlockLevel": return gc.UnlockLevelValue;
                default:
                    Log.Warning($"[CelesFD] Unknown global variable '{varName}' in prerequisite condition");
                    return 0f;
            }
        }

        // ═══ Phase 7 排序（价值降序；UI 展示层序另定：已接取→锁定→突发→价值） ═══
        private static void Phase7Sort(CelesFD_GameComponent gc)
        {
            gc.MarketOrders.Sort((a, b) => b.orderValue.CompareTo(a.orderValue));
        }

        // ═══ M6-6 突发订单生成（v4.3 B1：IncidentWorker 调用；复用 MakeOrderAndAdd + 立即接取） ═══
        // Urgent 类别 + validLevel 含有效等级的模板 Def 等权随机 → 生成 → 默认已接取（短期限 2 天 + FIFO 序）
        // 计入 maxTradeOrder 上限（满则跳过——防绕过接取限制）；无模板 → 返回 false（下个 interval 重试）
        public static bool TryGenerateUrgentOrder(CelesFD_GameComponent gc)
        {
            if (gc == null) return false;
            if (gc.MarketOrders.Count(o => o.state == CelesFD_OrderState.Accepted) >= gc.GetMaxTradeOrder())
                return false;
            CelesFD_UnlockLevelConfigDef config = CelesFD_DefOf.CelesFD_UnlockLevelConfigDefault;
            int eff = gc.GetEffectiveLevel();
            string levelDefName = config != null && config.unlockLevel != null
                && eff >= 0 && eff < config.unlockLevel.Count ? config.unlockLevel[eff] : null;
            var candidates = new List<(CelesFD_MarketClassDef def, CelesFD_MarketEntry entry)>();
            foreach (CelesFD_MarketClassDef def in DefDatabase<CelesFD_MarketClassDef>.AllDefsListForReading)
            {
                if (def.category != CelesFD_MarketCategory.Urgent || def.includeThings == null) continue;
                if (!ValidForLevel(def, levelDefName)) continue;
                foreach (CelesFD_MarketEntry e in def.includeThings)
                    if (GetExpand(e) is List<ThingDef> exp && exp.Count > 0)
                        candidates.Add((def, e));
            }
            if (candidates.Count == 0) return false;
            var (selDef, selEntry) = candidates.RandomElement();   // 简化等权（Phase 3 权重体系留作突发增强）
            MakeOrderAndAdd(gc, selDef, selEntry);
            CelesFD_Order order = gc.MarketOrders[gc.MarketOrders.Count - 1];
            order.state = CelesFD_OrderState.Accepted;
            order.deadlineTick = Find.TickManager.TicksGame + GenDate.TicksPerDay * 2;   // 突发期限 2 天（"时间极短"建议值，可调）
            order.acceptOrderIndex = gc.NextAcceptOrderIndex;
            gc.NextAcceptOrderIndex++;
            return true;
        }

        // ═══ 订单生成封装（MakeOrder：快照/价值/fame factor；v2 候选集全量快照——"任意此类商品"语义，不再抽单物） ═══
        private static void MakeOrderAndAdd(CelesFD_GameComponent gc, CelesFD_MarketClassDef def, CelesFD_MarketEntry entry)
        {
            List<ThingDef> expand = GetExpand(entry);
            if (expand == null || expand.Count == 0) return;

            CelesFD_Order order = new CelesFD_Order
            {
                templateDefName = def.defName,
                // v2 语义切换（2026-08-15 用户裁决）：出售（isBuy=false）类别单 = 生成时抽一物 × amount（仍单物品交付）；
                //   收购（isBuy=true）类别单 = 候选集全量（任意混合交付）；单物模式两者等价（单元素）
                thingDefNames = !def.isBuy
                    ? new List<string> { expand.RandomElement().defName }
                    : expand.Select(d => d.defName).ToList(),
                entryIndex = def.includeThings != null ? def.includeThings.IndexOf(entry) : -1,
                amount = entry.thingAmount.RandomInRange,
                priceCredit = entry.thingPriceCredit,
                priceKey = entry.thingPriceKey,
                totalPriceCredit = entry.orderTotalPriceCredit,
                totalPriceKey = entry.orderTotalPriceKey,
                thingExtraFameFactor = entry.thingExtraFameFactor,
                state = CelesFD_OrderState.Available
            };
            // 材质/品质快照（2026-08-15 用户裁决：filter 约束内随机；filter 空 → 随机分配，必须分配）
            //   单物品订单（出售抽定 + 收购单物）分配——出售用于发货（修复 madeFromStuff 报错）、收购用于显示/校验；
            //   类别模式（收购多候选混合）不分配——材质/品质经装填/结算 filter.Matches 校验
            if (order.thingDefNames.Count == 1)
                AssignStuffQuality(order, entry);
            order.remaining = order.amount;
            order.orderValue = CalcOrderValue(order);
            gc.MarketOrders.Add(order);

            // 彩蛋 letter（2026-08-15：彩蛋并入 Open 池生成——letter 迁移至此；letterDef 定制或默认 NeutralEvent）
            if (def.category == CelesFD_MarketCategory.EasterEgg)
            {
                LetterDef letterDef = null;
                if (!def.letterDef.NullOrEmpty())
                    letterDef = DefDatabase<LetterDef>.GetNamedSilentFail(def.letterDef);
                if (letterDef == null) letterDef = LetterDefOf.NeutralEvent;
                Find.LetterStack.ReceiveLetter(
                    "CelesFD_Keyed_EasterEggTitle".Translate(),
                    "CelesFD_Keyed_EasterEggDesc".Translate(),
                    letterDef);
            }
        }

        // 材质/品质分配（filter 约束内随机；空约束 → 随机分配）：材质 = def 可用材质 ∩ filter.stuffCategoriesToAllow；
        // 品质 = QualityUtility.AllQualityCategories ∩ filter.allowedQualities（min/max 区间）；非材质/无 CompQuality 物品跳过对应项
        // 注意：stuffCategoriesToAllow/allowedQualities 为 ThingFilter private 字段（ThingFilter.cs:36,69）——反射读取（同 Order.filterCategoriesField 模式）
        private static readonly System.Reflection.FieldInfo filterStuffCatsField =
            HarmonyLib.AccessTools.Field(typeof(ThingFilter), "stuffCategoriesToAllow");
        private static readonly System.Reflection.FieldInfo filterQualitiesField =
            HarmonyLib.AccessTools.Field(typeof(ThingFilter), "allowedQualities");

        private static void AssignStuffQuality(CelesFD_Order order, CelesFD_MarketEntry entry)
        {
            ThingDef td = DefDatabase<ThingDef>.GetNamedSilentFail(order.thingDefNames[0]);
            if (td == null) return;
            // 材质
            List<ThingDef> allowedStuffs = GenStuff.AllowedStuffsFor(td).ToList();   // def 可用材质（含 stuffCategories）
            if (entry.filter != null)
            {
                var cats = (System.Collections.Generic.List<StuffCategoryDef>)filterStuffCatsField.GetValue(entry.filter);
                if (cats != null && cats.Count > 0)
                {
                    allowedStuffs = allowedStuffs.Where(s => s.stuffProps != null && s.stuffProps.categories != null
                        && s.stuffProps.categories.Any(c => cats.Contains(c))).ToList();
                }
            }
            if (allowedStuffs.Count > 0)
                order.stuffDefName = allowedStuffs.RandomElement().defName;   // filter 空 → 全可用材质随机（必须分配）
            // 品质
            if (td.HasComp<CompQuality>())
            {
                var qs = QualityUtility.AllQualityCategories;
                if (entry.filter != null)
                {
                    var range = (QualityRange)filterQualitiesField.GetValue(entry.filter);
                    qs = qs.Where(q => (int)q >= (int)range.min && (int)q <= (int)range.max).ToList();
                }
                if (qs.Count > 0)
                {
                    order.qualityCategory = qs.RandomElement();
                    order.qualitySet = true;
                }
            }
        }

        // 价值（§3.2）：creditPart + keyPart × KeyToCredit（单价制 = price×amount；总价制 = 固定值）
        private static float CalcOrderValue(CelesFD_Order o)
        {
            float creditPart = o.priceCredit.HasValue ? o.priceCredit.Value * o.amount : (o.totalPriceCredit ?? 0f);
            float keyPart = o.priceKey.HasValue ? o.priceKey.Value * o.amount : (o.totalPriceKey ?? 0f);
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            float keyToCredit = gc != null ? gc.KeyToCredit : CelesFD_GameComponent.KeyToCreditDefault;
            return creditPart + keyPart * keyToCredit;
        }
    }
}
