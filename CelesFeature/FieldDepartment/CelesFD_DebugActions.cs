using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace CelesFeature
{
    public static class CelesFD_DebugActions
    {
        [DebugAction("CelesFD", "Modify global values")]
        private static void ModifyGlobalValues()
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return;

            List<DebugMenuOption> options = new List<DebugMenuOption>();
            AddIntOptions(options, "Fame", gc.Fame, v => gc.Fame = v);
            AddIntOptionsBig(options, "Credit", gc.Credit, v => gc.Credit = v);
            AddIntOptions(options, "QuantumKey", gc.QuantumKey, v => gc.QuantumKey = v);
            AddIntOptionsFine(options, "UnlockLevelValue", gc.UnlockLevelValue, v => gc.UnlockLevelValue = v);
            // EffectiveLevelValue 直改项已删除（v4.7：改派生 GetEffectiveLevel()——只读展示，Credit 联动）
            AddIntOptionsBig(options, "TradeVolume", gc.TradeVolume, v => gc.TradeVolume = v);
            AddIntOptions(options, "KeyToCredit", gc.KeyToCredit, v => gc.KeyToCredit = v);

            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        // F5 验证辅助：跳过条件直接触发搬迁询问（置位 RelocationAsked，符合"全局仅一次"语义）
        [DebugAction("CelesFD", "Trigger Relocation Letter")]
        private static void TriggerRelocation()
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return;
            gc.TriggerRelocation();
            Log.Message("[CelesFD] Relocation letter sent (debug trigger)");
        }

        // F6 验证辅助：真实敌对星铃信标站派系（goodwill 路径——SetRelationDirect 对 useGoodwill 派系直接报错并无效，Faction.cs:643）
        [DebugAction("CelesFD", "Set beacon faction hostile")]
        private static void MakeBeaconHostile()
        {
            var beacon = CelesFD_BeaconUtility.BeaconFaction;
            if (beacon == null) { Log.Error("[CelesFD] Beacon faction not found"); return; }
            Faction.OfPlayer.TryAffectGoodwillWith(beacon, Faction.OfPlayer.GoodwillToMakeHostile(beacon),
                canSendMessage: false, canSendHostilityLetter: false);
            Log.Message("[CelesFD] Beacon faction set hostile (debug, goodwill=" + beacon.PlayerGoodwill + ")");
        }

        // F6 验证辅助：恢复信标站派系关系为中立（好感回 0 → Neutral，验证风险解除 → 冷却清空）
        [DebugAction("CelesFD", "Set beacon faction neutral")]
        private static void MakeBeaconNeutral()
        {
            var beacon = CelesFD_BeaconUtility.BeaconFaction;
            if (beacon == null) { Log.Error("[CelesFD] Beacon faction not found"); return; }
            beacon.TryAffectGoodwillWith(Faction.OfPlayer, -beacon.PlayerGoodwill,
                canSendMessage: false, canSendHostilityLetter: false);
            Log.Message("[CelesFD] Beacon faction set neutral (debug, goodwill=" + beacon.PlayerGoodwill + ")");
        }

        // F6 验证辅助：跳过冷却期直接触发消失判定（随机一站消失 + letter）
        [DebugAction("CelesFD", "Force beacon disappear check")]
        private static void ForceBeaconDisappear()
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return;
            gc.BeaconCooldownStartTick = Find.TickManager.TicksGame - CelesFD_GameComponent.BeaconCooldownTicks;
            gc.TryDisappearRandomStation();
            Log.Message("[CelesFD] Beacon disappear check forced (debug)");
        }

        // F6 排障：输出冷却状态（startTick/elapsed/risk/站数），确认实操未触发的环节
        [DebugAction("CelesFD", "Log beacon cooldown state")]
        private static void LogBeaconCooldownState()
        {
            var gc = CelesFD_GameComponent.Instance;
            if (gc == null) { Log.Message("[CelesFD] GC null"); return; }
            var beacon = CelesFD_BeaconUtility.BeaconFaction;
            bool hostile = beacon != null && beacon.HostileTo(Faction.OfPlayer);
            string elapsed;
            bool expired = false;
            if (gc.BeaconCooldownActive)
            {
                int rawElapsed = Find.TickManager.TicksGame - gc.BeaconCooldownStartTick;
                expired = rawElapsed >= CelesFD_GameComponent.BeaconCooldownTicks;
                elapsed = System.Math.Min(rawElapsed, CelesFD_GameComponent.BeaconCooldownTicks).ToString();   // 封顶显示：满期后不再增长
            }
            else
            {
                elapsed = "-";
            }
            Log.Message($"[CelesFD] cooldown: startTick={gc.BeaconCooldownStartTick} ({(gc.BeaconCooldownActive ? "ACTIVE" : "inactive")}) elapsed={elapsed}/{CelesFD_GameComponent.BeaconCooldownTicks}{(expired ? " [EXPIRED]" : "")} hostile={hostile} beaconFaction={beacon?.Name ?? "NULL"} stations={CelesFD_BeaconUtility.GetStations().Count}");
        }

        // G7 验证辅助：生成星铃空投（TradeDropSpot 落点 + B 派系机械外观）——进场动画正常链路验证入口
        [DebugAction("CelesFD", "Spawn Celestia drop pod")]
        private static void SpawnCelestiaDrop()
        {
            Map map = Find.CurrentMap;
            var beacon = CelesFD_BeaconUtility.BeaconFaction;
            if (beacon == null)
            {
                Log.Error("[CelesFD] Beacon faction not found (debug drop aborted)");
                return;
            }
            var things = new List<Thing>();
            Thing steel = ThingMaker.MakeThing(ThingDefOf.Steel);
            steel.stackCount = 100;
            things.Add(steel);
            Thing meal = ThingMaker.MakeThing(ThingDefOf.MealSimple);
            meal.stackCount = 50;
            things.Add(meal);
            DropPodUtility.DropThingsNear(DropCellFinder.TradeDropSpot(map), map, things, faction: beacon);
            Log.Message("[CelesFD] Debug drop spawned near trade spot (steel x100, meal x50)");
        }

        private static void AddIntOptions(List<DebugMenuOption> options, string label, int current, System.Action<int> setter)
        {
            options.Add(new DebugMenuOption(label + " +100 (" + current + ")", DebugMenuOptionMode.Action,
                delegate { setter(current + 100); Log.Message(label + " -> " + (current + 100)); }));
            options.Add(new DebugMenuOption(label + " -100 (" + current + ")", DebugMenuOptionMode.Action,
                delegate { setter(current - 100); Log.Message(label + " -> " + (current - 100)); }));
            options.Add(new DebugMenuOption(label + " = 0", DebugMenuOptionMode.Action,
                delegate { setter(0); Log.Message(label + " -> 0"); }));
        }

        // 等级等小值字段：±1 粒度（v4.7）
        private static void AddIntOptionsFine(List<DebugMenuOption> options, string label, int current, System.Action<int> setter)
        {
            options.Add(new DebugMenuOption(label + " +1 (" + current + ")", DebugMenuOptionMode.Action,
                delegate { setter(current + 1); Log.Message(label + " -> " + (current + 1)); }));
            options.Add(new DebugMenuOption(label + " -1 (" + current + ")", DebugMenuOptionMode.Action,
                delegate { setter(current - 1); Log.Message(label + " -> " + (current - 1)); }));
            options.Add(new DebugMenuOption(label + " = 0", DebugMenuOptionMode.Action,
                delegate { setter(0); Log.Message(label + " -> 0"); }));
        }

        // Credit/TradeVolume 等大值字段：±100 基础上增 ±1000/±10000（v4.7）
        private static void AddIntOptionsBig(List<DebugMenuOption> options, string label, int current, System.Action<int> setter)
        {
            AddIntOptions(options, label, current, setter);
            options.Add(new DebugMenuOption(label + " +1000 (" + current + ")", DebugMenuOptionMode.Action,
                delegate { setter(current + 1000); Log.Message(label + " -> " + (current + 1000)); }));
            options.Add(new DebugMenuOption(label + " -1000 (" + current + ")", DebugMenuOptionMode.Action,
                delegate { setter(current - 1000); Log.Message(label + " -> " + (current - 1000)); }));
            options.Add(new DebugMenuOption(label + " +10000 (" + current + ")", DebugMenuOptionMode.Action,
                delegate { setter(current + 10000); Log.Message(label + " -> " + (current + 10000)); }));
            options.Add(new DebugMenuOption(label + " -10000 (" + current + ")", DebugMenuOptionMode.Action,
                delegate { setter(current - 10000); Log.Message(label + " -> " + (current - 10000)); }));
        }

        // G9 验证辅助：星铃殖民者离场测试（Farskip 特效 + 移出地图 + KeepForever 世界化，CelesFD_PawnDeparture_Test）
        [DebugAction("CelesFD", "Departure test: leave map (G9)")]
        private static void DepartureLeaveTest()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Messages.Message("CelesFD_DepartureFailNoMap".Translate(), MessageTypeDefOf.NeutralEvent);
                return;
            }
            List<Pawn> candidates = map.mapPawns.AllPawns
                .Where(p => !p.Dead && p.kindDef != null && p.kindDef.defName == "Celes_Colonist").ToList();
            if (candidates.Count == 0)
            {
                Messages.Message("CelesFD_DepartureFailNoCandidate".Translate(), MessageTypeDefOf.NeutralEvent);
                return;
            }
            List<DebugMenuOption> options = new List<DebugMenuOption>();
            foreach (Pawn p in candidates)
            {
                Pawn pawn = p; // 闭包捕获
                options.Add(new DebugMenuOption(pawn.LabelShort, DebugMenuOptionMode.Action,
                    delegate { CelesFD_PawnDeparture_Test.Departure(pawn); }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        // G9 验证辅助：星铃殖民者返回测试（世界取回 → 地图生成，数据保留验证）
        [DebugAction("CelesFD", "Departure test: return to map (G9)")]
        private static void DepartureReturnTest()
        {
            List<Pawn> candidates = Find.WorldPawns.AllPawnsAlive
                .Where(p => p.kindDef != null && p.kindDef.defName == "Celes_Colonist").ToList();
            if (candidates.Count == 0)
            {
                Messages.Message("CelesFD_DepartureFailNoWorldPawn".Translate(), MessageTypeDefOf.NeutralEvent);
                return;
            }
            List<DebugMenuOption> options = new List<DebugMenuOption>();
            foreach (Pawn p in candidates)
            {
                Pawn pawn = p; // 闭包捕获
                options.Add(new DebugMenuOption(pawn.LabelShort, DebugMenuOptionMode.Action,
                    delegate { CelesFD_PawnDeparture_Test.ReturnToMap(pawn); }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        // M0 验证辅助：重新随机本季度要闻（M2 刷新点挂接同一方法）
        [DebugAction("CelesFD", "Refresh news")]
        private static void RefreshNews()
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return;
            gc.RefreshNews();
        }

        // M1 验证辅助：打印全部市场订单模板 Def 的解析结果（filter 展开 AllowedThingDefs 数量 + 计价 + 保底/等级状态）
        [DebugAction("CelesFD", "Log market class defs")]
        private static void LogMarketClassDefs()
        {
            var defs = DefDatabase<CelesFD_MarketClassDef>.AllDefsListForReading;
            Log.Message("[CelesFD] Market class defs: " + defs.Count);
            foreach (CelesFD_MarketClassDef def in defs)
            {
                string entryInfo = "";
                if (def.includeThings != null)
                {
                    var parts = def.includeThings.Select(e =>
                        (e.filter != null ? e.filter.AllowedThingDefs.Count() + " items" : "no filter")
                        + " amt=" + e.thingAmount
                        + " pC=" + (e.thingPriceCredit.HasValue ? e.thingPriceCredit.ToString() : "-")
                        + " pK=" + (e.thingPriceKey.HasValue ? e.thingPriceKey.ToString() : "-")
                        + " tC=" + (e.orderTotalPriceCredit.HasValue ? e.orderTotalPriceCredit.ToString() : "-")
                        + " tK=" + (e.orderTotalPriceKey.HasValue ? e.orderTotalPriceKey.ToString() : "-"));
                    entryInfo = string.Join(" | ", parts);
                }
                Log.Message(string.Format("[CelesFD] {0}: cat={1} form={2} buy={3} guar={4} fame={5} validLevel={6} entries[{7}]",
                    def.defName, def.category, def.form, def.isBuy, def.isGuaranteed, def.fameReward,
                    def.validLevel != null ? string.Join(",", def.validLevel) : "(null)", entryInfo));
            }
        }

        // M2 验证辅助：无条件刷新市场（七阶段流水线全链路）
        [DebugAction("CelesFD", "Refresh market")]
        private static void DebugRefreshMarket()
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return;
            CelesFD_MarketGenerator.RefreshMarket();
        }

        // M2 验证辅助：手动刷新（冷却 5s + 花费公式 + 余额拒绝）
        [DebugAction("CelesFD", "Manual refresh market (cost)")]
        private static void DebugManualRefreshMarket()
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return;
            Log.Message(gc.TryManualRefresh()
                ? "[CelesFD] Manual refresh OK"
                : "[CelesFD] Manual refresh rejected (cooldown / insufficient funds)");
        }
    }
}