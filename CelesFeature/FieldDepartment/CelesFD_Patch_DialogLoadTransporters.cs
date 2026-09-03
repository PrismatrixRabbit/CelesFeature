using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // M6-4 装填白名单（方案 A-Harmony，用户裁决 2026-08-15；实证见下）：
    //   Postfix Dialog_LoadTransporters.CalculateAndRecacheTransferables——候选重建后过滤 + 上限：
    //   白名单 = 已接取订单需求物并集（thingDef 级）；上限 = 各 thingDef 跨全部已接取订单剩余量之和
    //   仅作用于我们的货运单元（transporters[0] is CelesFD_CompTransporter）——原版运输舱不受影响（用户实测反馈修正 2026-08-15）
    //   无已接取订单 → 候选清空（全禁用——用户裁决：不允许装填任何东西）
    // 实证：
    //   - 原版无类继承扩展点（Dialog_LoadTransporters 全 private；AddToTheToLoadList 非 virtual，CompTransporter.cs:502）
    //   - widget 惰性引用 transferables（TransferableOneWayWidget.cs:18,207-211；AddPawnsSections 同惰性 CaravanUIUtility.cs:126-142）
    //     → Postfix 改列表实时反映；pawn 条目 def 不在需求物集合 → 自动清空 Pawns 标签页（防结算 ClearAndDestroyContents 杀 pawn）
    //   - TryAccept 按 CountToTransfer 写 leftToLoad（Dialog_LoadTransporters.cs:429-468）→ clamp 生效
    //   - ClampAmount 非 virtual 且上限 = 可用物品数（Transferable.cs:75）→ 打开瞬间 clamp 被 widget 按钮绕过
    //     → 追加 CountToTransferChanged Postfix 实时 clamp（用户实测反馈修正 2026-08-15）
    [HarmonyPatch(typeof(Dialog_LoadTransporters), "CalculateAndRecacheTransferables")]
    public static class CelesFD_Patch_DialogLoadTransporters
    {
        // internal：兄弟 Patch 类（_Clamp）共用——private 为类级隔离，跨类访问需 internal
        internal static readonly System.Reflection.FieldInfo transferablesField =
            AccessTools.Field(typeof(Dialog_LoadTransporters), "transferables");
        internal static readonly System.Reflection.FieldInfo transportersField =
            AccessTools.Field(typeof(Dialog_LoadTransporters), "transporters");

        public static void Postfix(Dialog_LoadTransporters __instance)
        {
            if (!IsOwnDialog(__instance)) return;   // 原版运输舱装载窗口不受影响
            var list = (List<TransferableOneWay>)transferablesField.GetValue(__instance);
            if (list == null) return;
            var wanted = WantedRemaining();
            if (wanted == null || wanted.Count == 0)
            {
                list.Clear();   // 无已接取订单 → 全禁用（候选空，不允许装填任何东西）
                return;
            }
            FilterAndClamp(list, wanted);
        }

        // 是否我们的货运单元打开的装载窗口（原版 TransportPod 等一律跳过）
        internal static bool IsOwnDialog(Dialog_LoadTransporters __instance)
        {
            var transporters = (List<CompTransporter>)transportersField.GetValue(__instance);
            return transporters != null && transporters.Count > 0 && transporters[0] is CelesFD_CompTransporter;
        }

        // 需求物集合 + 跨订单剩余量汇总（同一 ThingDef 多订单求和；无已接取订单 → null）
        internal static Dictionary<ThingDef, int> WantedRemaining()
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return null;
            var wanted = new Dictionary<ThingDef, int>();
            foreach (CelesFD_Order o in gc.MarketOrders)
            {
                if (o.state != CelesFD_OrderState.Accepted || o.remaining <= 0) continue;
                // v2：候选集展开（"任意此类商品"——同一 ThingDef 跨订单求和）
                foreach (ThingDef d in o.ThingDefs)
                {
                    if (wanted.TryGetValue(d, out int cur)) wanted[d] = cur + o.remaining;
                    else wanted[d] = o.remaining;
                }
            }
            return wanted.Count > 0 ? wanted : null;
        }

        // 过滤（移除非需求物条目，含 Pawn 类——货运不装人；2026-08-15 升级：filter.Matches 含材质/品质/耐久限定）
        // + 逐物 clamp + 订单池约束（2026-08-15 用户裁决）
        internal static void FilterAndClamp(List<TransferableOneWay> list, Dictionary<ThingDef, int> wanted)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Thing t = list[i].AnyThing;
                if (t == null || !MatchesAnyOrder(t)) list.RemoveAt(i);
            }
            // 逐物上限（跨订单同物求和——用户边界：两单 2000 素食 → 单物上限 4000）
            foreach (TransferableOneWay t in list)
            {
                if (t.AnyThing != null && wanted.TryGetValue(t.AnyThing.def, out int max)
                    && t.CountToTransfer > max)
                    t.AdjustTo(max);   // AdjustTo 走原版 ClampAmount 校验链（CountToTransfer 为 protected set）
            }
            // 订单池约束：多候选订单 Σ_{候选物} CountToTransfer ≤ remaining（x+y+z ≤ 总量，跨物品共享池）
            EnforceOrderPools(list);
        }

        // 装填匹配校验（2026-08-15 升级）：物品须匹配任一已接取订单的 filter——原版 ThingFilter.Allows(Thing)
        //   （ThingFilter.cs:874 virtual——含 ThingDef/类别 + 材质 + 品质 + 耐久；StorageSettings.cs:99 同款标准实例匹配）
        private static bool MatchesAnyOrder(Thing thing)
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return false;
            foreach (CelesFD_Order o in gc.MarketOrders)
            {
                if (o.state != CelesFD_OrderState.Accepted || o.remaining <= 0) continue;
                if (o.EntryFilterAllows(thing)) return true;   // FD-G36（2026-09-02）：含材质类别实例级检查（原版 Allows(Thing) 无材质段）
            }
            return false;
        }

        // 订单池约束（用户裁决 2026-08-15）：类别订单"累计装填 vs 总需求"——
        //   贪心归属：物品量按包含它的订单（剩余降序）分配；任一物品无订单可收 → 削减到已分配量（列表序）
        private static void EnforceOrderPools(List<TransferableOneWay> list)
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return;
            var orders = gc.MarketOrders
                .Where(o => o.state == CelesFD_OrderState.Accepted && o.remaining > 0 && o.ThingDefs.Count > 1)
                .OrderByDescending(o => o.remaining).ToList();   // 剩余降序（贪心优先大订单）
            if (orders.Count == 0) return;
            var assigned = new Dictionary<CelesFD_Order, int>();
            foreach (CelesFD_Order o in orders) assigned[o] = 0;
            foreach (TransferableOneWay t in list)
            {
                if (t.AnyThing == null || t.CountToTransfer <= 0) continue;
                ThingDef def = t.AnyThing.def;
                // FD-G32 修复（2026-09-02，测试 a/b 判别定位）：物品不属于任何池订单 → 不受池约束管辖，
                // 跳过（该类物品已由前段 per-def wanted 钳制正确治理——单物订单跨单求和上限）。
                // 原实现误入"无订单可收"分支被 AdjustTo(0) 清零：类别单+单物单共存时单物物品全部失效
                if (!orders.Any(o => o.ThingDefs.Contains(def))) continue;
                int left = t.CountToTransfer;
                foreach (CelesFD_Order o in orders)
                {
                    if (!o.ThingDefs.Contains(def) || assigned[o] >= o.remaining) continue;
                    int take = Mathf.Min(left, o.remaining - assigned[o]);
                    assigned[o] += take;
                    left -= take;
                    if (left <= 0) break;
                }
                if (left > 0)
                    t.AdjustTo(t.CountToTransfer - left);   // 无订单可收 → 削减到已分配总量
            }
        }
    }

    // 数量实时限制：widget 每次调整（CountToTransferChanged 触发点）后重新 clamp——
    // 初版仅打开瞬间 AdjustTo 且初始 CountToTransfer=0 恒不触发，被 widget 按钮（上限=可用物品数）绕过
    [HarmonyPatch(typeof(Dialog_LoadTransporters), "CountToTransferChanged")]
    public static class CelesFD_Patch_DialogLoadTransporters_Clamp
    {
        public static void Postfix(Dialog_LoadTransporters __instance)
        {
            if (!CelesFD_Patch_DialogLoadTransporters.IsOwnDialog(__instance)) return;
            var list = (List<TransferableOneWay>)CelesFD_Patch_DialogLoadTransporters.transferablesField.GetValue(__instance);
            var wanted = CelesFD_Patch_DialogLoadTransporters.WantedRemaining();
            if (list == null || wanted == null || wanted.Count == 0) return;
            CelesFD_Patch_DialogLoadTransporters.FilterAndClamp(list, wanted);
        }
    }
}
