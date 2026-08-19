using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace CelesFeature
{
    // 抵达结算（M6-5 替换 G6 占位，§5.7 定稿）：
    //   舱内物品按需求物匹配（thingDef 级，材质/耐久装填侧校验裁决不做）→ acceptOrderIndex 全局 FIFO 队首优先
    //   → 扣减 remaining → 归零发奖（fame + 计价报酬 + 音效 Quest_Succeded + letter）→ 队列耗尽仍有剩余 → 丢弃（玩家自负）
    public class CelesFD_ArrivalAction_BeaconOrderTest : TransportersArrivalAction
    {
        private Settlement station;

        public CelesFD_ArrivalAction_BeaconOrderTest()
        {
        }

        public CelesFD_ArrivalAction_BeaconOrderTest(Settlement station)
        {
            this.station = station;
        }

        public override bool GeneratesMap => false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref station, "station");
        }

        public override FloatMenuAcceptanceReport StillValid(IEnumerable<IThingHolder> pods, PlanetTile destinationTile)
        {
            FloatMenuAcceptanceReport r = base.StillValid(pods, destinationTile);
            if (!r)
            {
                return r;
            }
            // 信标站失效（被摧毁/换派系）→ 拒绝 → 原版兜底内容丢失（MessageTransportPodsArrivedAndLost）
            if (station == null || !station.Spawned || station.Faction != CelesFD_BeaconUtility.BeaconFaction)
            {
                return FloatMenuAcceptanceReport.WithFailMessage("CelesFD_ShipmentLost".Translate());
            }
            return true;
        }

        public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            var gc = CelesFD_GameComponent.Instance;
            List<CelesFD_Order> completed = new List<CelesFD_Order>();
            if (gc != null)
            {
                // 已接取订单按全局接单顺序（FIFO，跨类交错）
                List<CelesFD_Order> accepted = gc.MarketOrders
                    .Where(o => o.state == CelesFD_OrderState.Accepted && o.remaining > 0)
                    .OrderBy(o => o.acceptOrderIndex).ToList();
                foreach (ActiveTransporterInfo t in transporters)
                {
                    foreach (Thing thing in t.innerContainer.ToList())   // 快照遍历（循环内 Destroy 安全）
                    {
                        int left = thing.stackCount;
                        while (left > 0)
                        {
                            // 匹配升级（2026-08-15）：filter.Allows(Thing) 含材质/品质/耐久校验（收购端装填限定一致，ThingFilter.cs:874）
                            CelesFD_Order target = accepted.FirstOrDefault(o => o.remaining > 0
                                && o.EntryFilter != null && o.EntryFilter.Allows(thing));   // v2 候选集匹配
                            if (target == null) break;   // 无订单可收 → 剩余丢弃
                            int take = Mathf.Min(left, target.remaining);
                            target.remaining -= take;
                            left -= take;
                            if (target.remaining <= 0)
                            {
                                CompleteOrder(target, gc, completed);
                                accepted.Remove(target);   // 完成后退出匹配池（同 def 下一订单继续接）
                            }
                        }
                        if (left > 0) thing.Destroy();   // 失配/超量丢弃（§5.7 定稿：玩家自负判断，不折算）
                    }
                }
            }
            else
            {
                foreach (ActiveTransporterInfo t in transporters) t.innerContainer.ClearAndDestroyContents();
            }
            // 星铃侧无地图：舱体内容已逐物处理；兜底清空（pawn 保护缺省——装填白名单已禁 pawn，M6-4）
            foreach (ActiveTransporterInfo t in transporters) t.innerContainer.ClearAndDestroyContents();
            if (completed.Count > 0)
            {
                SoundDefOf.Quest_Succeded.PlayOneShotOnCamera();
                var lines = new List<string>();
                foreach (CelesFD_Order o in completed)
                {
                    int f = o.TemplateDef != null ? Mathf.RoundToInt(o.TemplateDef.fameReward * o.thingExtraFameFactor) : 0;
                    int c = Mathf.RoundToInt(o.CalcPriceCredit());
                    int k = Mathf.RoundToInt(o.CalcPriceKey());
                    lines.Add("CelesFD_Keyed_OrderSuccessDesc"
                        .Translate(o.ResolveLabel(), f, c, k > 0 ? "，密钥 +" + k : "").ToString());
                }
                Find.LetterStack.ReceiveLetter("CelesFD_Keyed_OrderSuccessTitle".Translate(),
                    string.Join("\n\n", lines), LetterDefOf.PositiveEvent, new GlobalTargetInfo(tile));
                Log.Message($"[CelesFD] Shipment settled: {completed.Count} order(s) completed");
            }
        }

        // 完成结算：发奖（fame=round(fameReward×factor，§3.2) + 计价报酬 credit/key）+ A5 L 重置/归档 + 移出市场队列
        private static void CompleteOrder(CelesFD_Order order, CelesFD_GameComponent gc, List<CelesFD_Order> completed)
        {
            CelesFD_MarketClassDef def = order.TemplateDef;
            if (def == null)
            {
                gc.MarketOrders.Remove(order);   // Def 缺失（模板被删）→ 无法发奖，安全移除
                return;
            }
            int fame = Mathf.RoundToInt(def.fameReward * order.thingExtraFameFactor);
            int credit = Mathf.RoundToInt(order.CalcPriceCredit());
            int key = Mathf.RoundToInt(order.CalcPriceKey());
            gc.ModifyFame(fame);
            gc.ModifyCredit(credit);
            if (key > 0) gc.ModifyQuantumKey(key);
            // 交易额（用户裁决 2026-08-15：成功结算按 信用额额度 + 密钥额度×汇率 计入，收购/贩售统一；M7 贩售结算同公式）
            int tradeVolume = Mathf.RoundToInt(order.CalcPriceCredit() + order.CalcPriceKey() * gc.KeyToCredit);
            gc.ModifyTradeVolume(tradeVolume);
            gc.NotifyOrderSucceeded(order);   // A5 L 重置为 1 + 外勤档案
            gc.MarketOrders.Remove(order);
            completed.Add(order);
            Log.Message($"[CelesFD] Order completed: fame +{fame}, credit +{credit}, key +{key}, tradeVolume +{tradeVolume}");
        }
    }
}
