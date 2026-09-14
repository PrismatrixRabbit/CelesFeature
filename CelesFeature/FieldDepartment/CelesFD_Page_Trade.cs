using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;   // PlayOneShotOnCamera 扩展方法（SoundStarter.cs:8，Verse.Sound 命名空间）

namespace CelesFeature
{
    // 交易页（M4 §6 UI 设计定稿；M5）
    // 布局：左 50% = 顶栏（搜索+9筛选）→ 已接取区（条件显示）→ 订单区（双列卡片）→ 刷新按钮；右 50% = 购物车 70% + 结算 30%
    public class CelesFD_Page_Trade : CelesFD_IPage, CelesFD_ISubPage
    {
        private const float TopBarHeight = 40f;
        private const float CardGap = CelesFD_UIConfig.CardGap;   // 别名（集中 UIConfig，解耦 2026-08-14）
        private const float CartFraction = 0.6f;   // 用户裁决：购物车内容 60% / 结算区 40%
        private const float MaxCartMass = 1200f;      // 超重阈值（§6.4，kg）
        private const float RefreshCooldown = 5f;     // 手动刷新冷却（§5.5，真实时间秒）

        private readonly QuickSearchWidget searchWidget = new QuickSearchWidget();
        private readonly bool[] toggles = new bool[9];   // 9 筛选：0-4 类别 / 5-6 形态 / 7-8 方向（默认亮起=显示）
        private CelesFD_Order selectedOrder;            // 选中订单（上方子页 description）
        private CelesFD_Order pendingAcceptOrder;       // 接取"确认？"态（失焦回退）
        private CelesFD_Order pendingAbandonOrder;      // 放弃确认态
        private bool pendingRefresh;                    // 刷新确认态
        private Rect acceptConfirmRect;                 // 确认按钮区域（失焦判定）
        private Rect abandonConfirmRect;
        private Rect refreshConfirmRect;

        private Vector2 orderScrollPos;
        private Vector2 acceptedScrollPos;
        private Vector2 cartScrollPos;
        private Vector2 checkoutScrollPos;   // ② 重构：结算区文本滚动

        // 协议（§6.5）
        private bool agreeToTerms = true;               // 用户协议（默认勾选；不勾禁用下单）
        private bool subscribeArrival = true;           // 到货提醒（M7：控制抵达 letter）
        private bool expediteOrder;                     // 加急（按钮切换 + 运费×2）
        private bool infoActive;                        // ？帮助（R-1 UI 改造：子页显示交易页机制介绍）
        private bool fastStartup = true;                // 风味勾选项（纯 UI 状态，无功能）
        private bool allowAds = true;

        public string Title => "CelesFD_Keyed_Tab_Trade".Translate();

        public CelesFD_Page_Trade()
        {
            for (int i = 0; i < toggles.Length; i++) toggles[i] = true;
        }

        public void Notify_Deactivated()
        {
            selectedOrder = null;
            pendingAcceptOrder = null;
            pendingAbandonOrder = null;
            pendingRefresh = false;
            infoActive = false;   // R-1 风格批：切页重置 ？态（默认文本恢复）
        }

        // ═══ 主页面 ═══
        public void Draw(Rect inRect)
        {
            HandleFocusLoss(inRect);

            Rect left = new Rect(inRect.x, inRect.y, inRect.width * 0.5f, inRect.height);
            Rect right = new Rect(inRect.x + inRect.width * 0.5f, inRect.y, inRect.width * 0.5f, inRect.height);

            Widgets.DrawMenuSection(left);
            Widgets.DrawMenuSection(right);
            Rect leftInner = left.ContractedBy(4f);
            Rect rightInner = right.ContractedBy(4f);

            DrawLeftPane(leftInner);
            DrawRightPane(rightInner);
        }

        // 失焦回退（定稿 v4：点击其他位置 → 回退）
        // 修复（坐标空间）：接取/放弃按钮位于 BeginScrollView 内（内容坐标）——其失焦检测必须在滚动区域内执行
        // （BeginScrollView 内 Event.current.mousePosition 被 Unity 转换为内容坐标，与 acceptConfirmRect 同空间）；
        // 原在 Draw 开头检测 → 窗口坐标 vs 内容坐标错位 → 确认态被误判取消（点确认实际按到"接取"，循环）。
        // 此处仅保留窗口坐标域的对象：刷新按钮（非滚动区）+ 页面外取消选中。
        private void HandleFocusLoss(Rect inRect)
        {
            if (Event.current.type != EventType.MouseDown) return;
            Vector2 mouse = Event.current.mousePosition;
            if (pendingRefresh && !refreshConfirmRect.Contains(mouse)) pendingRefresh = false;
            if (!inRect.Contains(mouse)) selectedOrder = null;   // 点击页面外（切页等）取消选中
        }

        // ═══ 左栏 ═══
        private void DrawLeftPane(Rect rect)
        {
            float curY = rect.y;
            // 顶栏 40px：搜索（左 100px）+ 9 筛选（右）
            Rect topBar = new Rect(rect.x, curY, rect.width, TopBarHeight);
            DrawTopBar(topBar);
            curY += TopBarHeight + CardGap;

            // 已接取区（条件显示，A6-2）
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            List<CelesFD_Order> accepted = gc != null
                ? gc.MarketOrders.Where(o => o.state == CelesFD_OrderState.Accepted).ToList()
                : new List<CelesFD_Order>();
            float acceptedHeight = 0f;
            if (accepted.Count > 0)
            {
                // 双列公式 + 上限两行（用户裁决：第二卡不触发换行；第三卡起才两行；超限滚动）
                acceptedHeight = Mathf.Min(216f, 44f + Mathf.Ceil(accepted.Count / 2f) * 86f);
                DrawAcceptedArea(new Rect(rect.x, curY, rect.width, acceptedHeight), accepted);
                curY += acceptedHeight + CardGap;
            }

            // 订单区（双列卡片滚动）——剩余高度
            float ordersHeight = rect.yMax - curY - TopBarHeight - CardGap;   // 底部预留刷新按钮
            DrawOrderList(new Rect(rect.x, curY, rect.width, ordersHeight));

            // 刷新按钮（左下角，A6-1）
            DrawRefreshButton(new Rect(rect.x, rect.yMax - TopBarHeight, rect.width, TopBarHeight - CardGap));
        }

        private void DrawTopBar(Rect rect)
        {
            Rect searchRect = new Rect(rect.x, rect.y, 100f, rect.height);
            searchWidget.OnGUI(searchRect);

            // 9 筛选 toggle（靠右）
            float btnSize = Mathf.Min(26f, rect.height - 6f);
            float gap = 2f;
            float totalW = 9 * btnSize + 8 * gap;
            float x = rect.xMax - totalW;
            float y = rect.y + (rect.height - btnSize) / 2f;
            DrawToggle(x, y, btnSize, 0, "CelesFD_Keyed_TradeFilterOpen");
            x += btnSize + gap;
            DrawToggle(x, y, btnSize, 1, "CelesFD_Keyed_TradeFilterInternal");
            x += btnSize + gap;
            DrawToggle(x, y, btnSize, 2, "CelesFD_Keyed_TradeFilterPrecious");
            x += btnSize + gap;
            DrawToggle(x, y, btnSize, 3, "CelesFD_Keyed_TradeFilterEasterEgg");
            x += btnSize + gap;
            DrawToggle(x, y, btnSize, 4, "CelesFD_Keyed_TradeFilterUrgent");
            x += btnSize + gap;
            DrawToggle(x, y, btnSize, 5, "CelesFD_Keyed_TradeFilterScattered");
            x += btnSize + gap;
            DrawToggle(x, y, btnSize, 6, "CelesFD_Keyed_TradeFilterBulk");
            x += btnSize + gap;
            DrawToggle(x, y, btnSize, 7, "CelesFD_Keyed_TradeFilterBuy");
            x += btnSize + gap;
            DrawToggle(x, y, btnSize, 8, "CelesFD_Keyed_TradeFilterSell");
        }

        private void DrawToggle(float x, float y, float size, int index, string key)
        {
            Rect r = new Rect(x, y, size, size);
            GUI.color = toggles[index] ? Color.white : new Color(0.4f, 0.4f, 0.4f, 0.7f);
            if (Widgets.ButtonText(r, key.Translate()))
                toggles[index] = !toggles[index];
            GUI.color = Color.white;
        }

        // ═══ 已接取区（A6-2：独立分区；进度静态显示——倒计时随 M6） ═══
        private void DrawAcceptedArea(Rect rect, List<CelesFD_Order> accepted)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(4f);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 24f), "CelesFD_Keyed_AcceptedOrders".Translate());

            // ② 已接取双列卡片（同订单区布局——UIConfig 统一网格）
            Rect listRect = new Rect(inner.x, inner.y + 26f, inner.width, inner.height - 26f);
            CelesFD_UIConfig.CalcCardGrid(listRect, accepted.Count, out float cardW, out float cardH, out float contentH);
            Widgets.BeginScrollView(listRect, ref acceptedScrollPos, new Rect(0f, 0f, listRect.width - 16f, contentH));
            // 放弃确认失焦检测（坐标空间修复：同 BeginScrollView 内容坐标）
            if (Event.current.type == EventType.MouseDown && pendingAbandonOrder != null
                && !abandonConfirmRect.Contains(Event.current.mousePosition))
                pendingAbandonOrder = null;
            for (int i = 0; i < accepted.Count; i++)
            {
                int col = i % 2;
                int row = i / 2;
                Rect card = new Rect(col * (cardW + CardGap), row * (cardH + CardGap), cardW, cardH);
                DrawAcceptedCard(card, accepted[i]);
            }
            Widgets.EndScrollView();
        }

        // 已接取卡片（②：委托 CelesFD_OrderCard.DrawAccepted 变体——进度/无锁钮/品名右延伸滚动）
        private void DrawAcceptedCard(Rect rect, CelesFD_Order o)
        {
            CelesFD_OrderCard.DrawAccepted(rect, o,
                abandonPending: pendingAbandonOrder == o,
                abandonBtnRect: ref abandonConfirmRect,
                onAbandon: OnAbandonClicked);
            // 卡片点击选中（同 DrawOrderCard——已接取订单也可查看 description）
            if (Widgets.ButtonInvisible(rect)) { selectedOrder = o; infoActive = false; }
        }

        private void OnAbandonClicked(CelesFD_Order o)
        {
            if (pendingAbandonOrder == o)
            {
                CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
                if (gc != null) gc.TryAbandonOrder(o);   // M6-2：A5 违约结算（音效已统一放 ApplyPenalty，此处不重复）
                pendingAbandonOrder = null;
            }
            else
            {
                pendingAbandonOrder = o;
            }
        }

        // ═══ 订单区（双列卡片，1:3 高宽，4px 间距，滚动） ═══
        private void DrawOrderList(Rect rect)
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            List<CelesFD_Order> visible = gc != null
                ? gc.MarketOrders.Where(o => o.state == CelesFD_OrderState.Available && MatchesFilter(o)
                    && !gc.ShoppingCart.Contains(o)).ToList()   // ②：购买后卡片移出（购物车中的订单不再显示于市场区）
                : new List<CelesFD_Order>();

            if (visible.Count == 0)
            {
                Widgets.Label(new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, 30f), "CelesFD_Keyed_NoOrders".Translate());
                return;
            }

            CelesFD_UIConfig.CalcCardGrid(rect, visible.Count, out float cardW, out float cardH, out float totalH);   // 解耦：统一网格算法
            Widgets.BeginScrollView(rect, ref orderScrollPos, new Rect(0f, 0f, rect.width - 16f, totalH));
            // 接取确认失焦检测（坐标空间修复：BeginScrollView 内 mousePosition 为内容坐标，与 acceptConfirmRect 同空间）
            if (Event.current.type == EventType.MouseDown && pendingAcceptOrder != null
                && !acceptConfirmRect.Contains(Event.current.mousePosition))
                pendingAcceptOrder = null;
            for (int i = 0; i < visible.Count; i++)
            {
                int col = i % 2;
                int row = i / 2;
                Rect card = new Rect(col * (cardW + CardGap), row * (cardH + CardGap), cardW, cardH);
                DrawOrderCard(card, visible[i]);
            }
            Widgets.EndScrollView();
        }

        // 卡片渲染委托（② 重构：独立类 CelesFD_OrderCard——布局/文本/交互渲染解耦；本方法仅注入交互状态与回调）
        private void DrawOrderCard(Rect rect, CelesFD_Order o)
        {
            CelesFD_OrderCard.Draw(rect, o,
                acceptPending: pendingAcceptOrder == o,
                acceptBtnRect: ref acceptConfirmRect,
                onAccept: OnAcceptClicked,
                onBuy: TryAddToCart,
                onToggleLock: OnToggleLockClicked);
            // 卡片点击选中（M5b 接线遗漏修复 2026-08-15）：ButtonInvisible 在按钮绘制之后——IMGUI 先绘先消费，不吞按钮点击
            if (Widgets.ButtonInvisible(rect)) { selectedOrder = o; infoActive = false; }
        }

        private void OnAcceptClicked(CelesFD_Order o)
        {
            if (pendingAcceptOrder == o)
            {
                CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
                if (gc != null && gc.TryAcceptOrder(o))
                {
                    SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
                    selectedOrder = null;
                }
                else if (gc != null)
                {
                    // 修复（2026-08-15）：接取失败（maxTradeOrder 超限等）静默无反馈 → 与原版 RejectInput 提示模式统一
                    Messages.Message("CelesFD_Keyed_AcceptLimit".Translate(gc.GetMaxTradeOrder()), MessageTypeDefOf.RejectInput);
                }
                pendingAcceptOrder = null;
            }
            else
            {
                pendingAcceptOrder = o;
            }
        }

        private void OnToggleLockClicked(CelesFD_Order o)
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc != null && !gc.TryToggleLock(o))
                Messages.Message("CelesFD_Keyed_LockFull".Translate(), MessageTypeDefOf.RejectInput);
        }

        // ═══ 过滤谓词（§6.2 互斥：搜索有内容 → 筛选失效） ═══
        private bool MatchesFilter(CelesFD_Order o)
        {
            CelesFD_MarketClassDef def = o.TemplateDef;
            if (def == null) return false;
            if (searchWidget.filter.Active)
                return searchWidget.filter.Matches(o.ResolveLabel());
            bool catShow = (def.category == CelesFD_MarketCategory.Open && toggles[0])
                || (def.category == CelesFD_MarketCategory.Internal && toggles[1])
                || (def.category == CelesFD_MarketCategory.Precious && toggles[2])
                || (def.category == CelesFD_MarketCategory.EasterEgg && toggles[3])
                || (def.category == CelesFD_MarketCategory.Urgent && toggles[4]);
            if (!catShow) return false;
            bool formShow = (def.form == CelesFD_MarketForm.Scattered && toggles[5])
                || (def.form == CelesFD_MarketForm.Bulk && toggles[6]);
            if (!formShow) return false;
            bool dirShow = (def.isBuy && toggles[7]) || (!def.isBuy && toggles[8]);
            return dirShow;
        }

        // ═══ 刷新按钮（A6-1：左下角 + 冷却灰显 + 花费提示 + 确认） ═══
        private void DrawRefreshButton(Rect rect)
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return;
            bool cooldown = Time.realtimeSinceStartup - gc.LastManualRefreshRealTime < RefreshCooldown;
            gc.GetManualRefreshCost(out int creditCost, out int keyCost);
            string costText = creditCost > 0
                ? "CelesFD_Keyed_RefreshCostCredit".Translate(creditCost)
                : "CelesFD_Keyed_RefreshCostKey".Translate(keyCost);

            Rect btnRect = new Rect(rect.x, rect.y, 170f, rect.height - 2f);
            // 冷却灰显（2026-08-15 用户裁决：原版禁用视觉——GUI.color 灰染 + 可点提示"冷却中"）
            if (cooldown)
                GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.9f);
            if (pendingRefresh)
            {
                refreshConfirmRect = btnRect;
                // 修复：ConfirmRefresh 的 {0} 传纯数字（原误传 costText 完整句 → 嵌套文本"花费 刷新市场（花费 X 信用额） 刷新市场？"）
                // 禁用视觉与既有模式统一（2026-08-15 实证：drawBackground=false 不画背景=纯文本 Widgets.cs:1471-1474；
                //   既有禁用 = GUI.color 灰染 + 背景恒绘 + 可点提示——下单按钮同款 Page_Trade.cs:487）
                if (Widgets.ButtonText(btnRect, "CelesFD_Keyed_ConfirmRefresh".Translate(creditCost > 0 ? creditCost.ToString() : keyCost.ToString()), drawBackground: true))
                {
                    if (!gc.TryManualRefresh())
                        Messages.Message("CelesFD_Keyed_RefreshRejected".Translate(), MessageTypeDefOf.RejectInput);
                    pendingRefresh = false;
            infoActive = false;   // R-1 风格批：切页重置 ？态（默认文本恢复）
                }
            }
            else if (Widgets.ButtonText(btnRect, "CelesFD_Keyed_RefreshMarket".Translate().ToString() + (cooldown ? " (" + "CelesFD_Keyed_RefreshCooldown".Translate().ToString() + ")" : ""), drawBackground: true))   // 背景恒绘（同确认态——灰染表禁用，实证 Widgets.cs:1471-1474）
            {
                if (cooldown)
                    Messages.Message("CelesFD_Keyed_RefreshCooldown".Translate(), MessageTypeDefOf.RejectInput);
                else
                    pendingRefresh = true;
            }
            GUI.color = Color.white;
            Widgets.Label(new Rect(btnRect.xMax + 6f, rect.y + 4f, rect.width - btnRect.width - 12f, rect.height - 8f), costText);
        }

        // ═══ 右栏：购物车 + 结算 ═══
        private void DrawRightPane(Rect rect)
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return;

            float cartH = rect.height * CartFraction;
            DrawCartArea(new Rect(rect.x, rect.y, rect.width, cartH));
            DrawCheckoutArea(new Rect(rect.x, rect.y + cartH + CardGap, rect.width, rect.height - cartH - CardGap));
        }

        private void DrawCartArea(Rect rect, List<CelesFD_Order> cart = null)
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return;
            if (cart == null) cart = gc.ShoppingCart;

            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(4f);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width - 34f, 24f), "CelesFD_Keyed_Cart".Translate());
            if (CelesFD_UIConfig.DrawHelpButton(inner.xMax - 24f, inner.y, 24f)) infoActive = !infoActive;

            // 总重量（§6.4：运费 W 输入 + 超重警示）
            // 修复（2026-08-15）：原 Widgets.Label 默认 Small + 固定 20f/18f rect——Small 行高实测 ≈20，18f 必然溢出
            //   改为 DrawScaledLabel + 实测行高（massH），listTop 同步用实测值
            float mass = CelesFD_ShippingUtility.CalcTotalMass(cart);
            float silverV = CelesFD_ShippingUtility.CalcTotalValue(cart);   // 运费公式 V 输入（§5.4）——用户要求显示（2026-08-15）
            bool overweight = mass > MaxCartMass;
            float massH = CelesFD_UIConfig.ScaledLineHeight(CelesFD_UIConfig.FontMain);
            Color c = overweight ? Color.red : Color.white;
            GUI.color = c;
            DrawScaledLabel(new Rect(inner.x, inner.y + 24f, inner.width, massH),
                "CelesFD_Keyed_CartTotalMass".Translate(mass.ToString("0.#"))
                + "    " + "CelesFD_Keyed_CartValue".Translate(silverV.ToString("0.#")), CelesFD_UIConfig.FontMain);
            GUI.color = Color.white;
            if (overweight)
                DrawScaledLabel(new Rect(inner.x, inner.y + 24f + massH + 4f, inner.width, massH),
                    "CelesFD_Keyed_CartOverweight".Translate(), CelesFD_UIConfig.FontMain);

            float listTop = inner.y + 24f + massH + 4f + (overweight ? massH + 4f : 0f);
            Rect listRect = new Rect(inner.x, listTop, inner.width, inner.yMax - listTop);
            // ② 购物车双列卡片（同订单区布局——UIConfig 统一网格；DrawCart 变体：无锁钮/数量+报价/移除按钮/品名右延伸滚动）
            CelesFD_UIConfig.CalcCardGrid(listRect, cart.Count, out float cardW, out float cardH, out float contentH);
            Widgets.BeginScrollView(listRect, ref cartScrollPos, new Rect(0f, 0f, listRect.width - 16f, contentH));
            bool removed = false;
            for (int i = 0; i < cart.Count && !removed; i++)
            {
                int col = i % 2;
                int row = i / 2;
                Rect card = new Rect(col * (cardW + CardGap), row * (cardH + CardGap), cardW, cardH);
                CelesFD_OrderCard.DrawCart(card, cart[i], o =>
                {
                    gc.ShoppingCart.Remove(o);
                    removed = true;   // break 语义：BeginScrollView 内不 return，保证 EndScrollView 配对
                });
            }
            Widgets.EndScrollView();
        }

        private void DrawCheckoutArea(Rect rect)
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return;
            List<CelesFD_Order> cart = gc.ShoppingCart;

            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(4f);
            float curY = inner.y;

            // 金额明细（§6.5；M7-2 双币种应付——用户裁决 2026-08-15：显示双币种、扣款分币种各自足额、税/运费归信用额侧）
            float creditTotal = cart.Sum(o => o.CalcPriceCredit());
            float keyTotal = cart.Sum(o => o.CalcPriceKey());
            float silverV = CelesFD_ShippingUtility.CalcTotalValue(cart);
            float massW = CelesFD_ShippingUtility.CalcTotalMass(cart);
            float shipping = CelesFD_ShippingUtility.CalcShippingCost(silverV, massW) * (expediteOrder ? 2f : 1f);   // 加急 2x（确认）
            float creditBase = creditTotal + keyTotal * gc.KeyToCredit;
            float tax = CelesFD_ShippingUtility.CalcTax(creditBase);
            int taxFloor = Mathf.FloorToInt(tax);   // 用户裁决：总价计算用向下取整整数税金
            int shippingFloor = Mathf.FloorToInt(shipping);   // 运费取整（与扣款一致）
            const float discount = 0f;   // 折扣暂空占位（§6.5）
            int creditPay = Mathf.RoundToInt(creditTotal) + taxFloor + shippingFloor;   // 信用额应付（货+税+运费）
            int keyPay = Mathf.RoundToInt(keyTotal);   // 密钥应付（货 key；税/运费归信用额侧）

            // ═══ ② 重构：文本滚动区（明细双列 + 协议 2 列）+ 底部固定按钮（文本始终在按钮上方，不重叠不遮挡） ═══
            const float btnH = 34f;
            Rect textArea = new Rect(inner.x, inner.y, inner.width, inner.height - btnH - 4f);
            Rect btnRect = new Rect(inner.x, rect.yMax - btnH - 4f, inner.width, btnH - 4f);
            const float colGap = 4f;   // 两列间距（用户裁决：列间 ≥4px 不可紧贴）
            float colW = (textArea.width - 16f - colGap) / 2f;
            // 实测行高（修复 2026-08-15：明细行 rect 高度 = ScaledLineHeight 实测值——Widgets.Label/GUI.Label 无裁剪，
            //   固定 18f/20f 曾致文本底部溢出被遮盖；总价行字号 16f 单独实测）
            float lineH = CelesFD_UIConfig.ScaledLineHeight(CelesFD_UIConfig.FontMain);
            float subLineH = CelesFD_UIConfig.ScaledLineHeight(CelesFD_UIConfig.FontSub);
            // contentH 动态计算（解耦：消除硬编码估算——明细固定行 + 协议/风味动态行高）
            float agreeH = Mathf.Max(22f, CalcLabelHeight("CelesFD_Keyed_AgreeTerms".Translate(), colW - 26f));
            float subH = Mathf.Max(22f, CalcLabelHeight("CelesFD_Keyed_SubscribeArrival".Translate(), colW - 26f));
            float expH = Mathf.Max(22f, CalcLabelHeight("CelesFD_Keyed_Expedite".Translate(), colW - 26f));
            float fsH = Mathf.Max(22f, CalcLabelHeight("CelesFD_Keyed_FastStartup".Translate(), colW - 26f));
            float adH = Mathf.Max(22f, CalcLabelHeight("CelesFD_Keyed_AllowAds".Translate(), colW - 26f));
            float contentH = (lineH + 2f) * 3f + (subLineH + 2f) * 2f + 30f + 10f + 16f
                + Mathf.Max(agreeH, subH) + 2f + Mathf.Max(expH, fsH) + 2f + adH;
            Widgets.BeginScrollView(textArea, ref checkoutScrollPos, new Rect(0f, 0f, textArea.width - 16f, contentH));
            float cy = 0f;

            // 行1：货物报价（双币种各划线+floor；恒显密钥空间）
            // 修复：原 Widgets.Label 默认 Small（行高实测 ≈20）+ 18f rect → 必溢出；改 DrawScaledLabel + lineH
            DrawScaledLabel(new Rect(0f, cy, colW * 0.8f, lineH), "CelesFD_Keyed_GoodsQuote".Translate(), CelesFD_UIConfig.FontMain);
            DrawBothFloorAmount(new Rect(colW * 0.8f, cy, colW * 1.2f, lineH), creditTotal, keyTotal);
            cy += lineH + 2f;
            // 行2：运费（跨两列，划线+floor——易出小数）
            DrawDetailLine(0f, cy, colW * 2f, lineH, "CelesFD_Keyed_Shipping".Translate(), shipping);
            cy += lineH + 2f;
            // 行3-4：税分项 2×2（税名（比率）+ 税额普通 2 位小数右对齐——划线仅总项）
            DrawTaxGrid(0f, cy, colW, colGap, CelesFD_ShippingUtility.TaxItems, creditBase);
            cy += (subLineH + 2f) * 2f;
            // 行5：总税费（左划线+floor）| 折扣（右，无小数直显）
            DrawScaledLabel(new Rect(0f, cy, colW * 0.4f, lineH), "CelesFD_Keyed_Tax".Translate(), CelesFD_UIConfig.FontMain);
            DrawFloorAmount(new Rect(colW * 0.4f, cy, colW * 0.6f, lineH), tax);
            DrawDetailLine(colW + colGap, cy, colW, lineH, "CelesFD_Keyed_Discount".Translate(), discount);
            cy += lineH + 2f;
            // 行6：总价（双币种应付划线+floor——信用额侧=货+税+运费、密钥侧=货 key；与 M7-2 扣款一致）
            Widgets.Label(new Rect(0f, cy, colW * 0.8f, 24f), "CelesFD_Keyed_Total".Translate());
            DrawBothFloorAmount(new Rect(colW * 0.8f, cy, colW * 1.2f, CelesFD_UIConfig.ScaledLineHeight(16f)), creditPay, keyPay, 16f);
            cy += 30f;
            // 分隔线
            Widgets.DrawLineHorizontal(0f, cy, textArea.width - 16f, new Color(0.4f, 0.4f, 0.4f, 0.6f));
            cy += 10f;
            // 协议 2 列 + 风味勾选（②：DrawCheckboxLabel 动态行高——文本一行/半行自动排布，后续内容自行下移；风味项可勾选）
            bool agree = agreeToTerms;
            float h1 = DrawCheckboxLabel(new Rect(0f, cy, colW, 0f), "CelesFD_Keyed_AgreeTerms".Translate(), ref agree, colW);
            agreeToTerms = agree;
            bool sub = subscribeArrival;
            float h2 = DrawCheckboxLabel(new Rect(colW + colGap, cy, colW, 0f), "CelesFD_Keyed_SubscribeArrival".Translate(), ref sub, colW);
            subscribeArrival = sub;
            cy += Mathf.Max(h1, h2) + 2f;
            bool exp = expediteOrder;
            h1 = DrawCheckboxLabel(new Rect(0f, cy, colW, 0f), "CelesFD_Keyed_Expedite".Translate(), ref exp, colW);
            expediteOrder = exp;
            bool fs = fastStartup;
            h2 = DrawCheckboxLabel(new Rect(colW + colGap, cy, colW, 0f), "CelesFD_Keyed_FastStartup".Translate(), ref fs, colW);
            fastStartup = fs;
            cy += Mathf.Max(h1, h2) + 2f;
            bool ad = allowAds;
            DrawCheckboxLabel(new Rect(0f, cy, colW, 0f), "CelesFD_Keyed_AllowAds".Translate(), ref ad, colW);
            allowAds = ad;
            Widgets.EndScrollView();

            // 底部固定按钮（标准 ⇄ 加急；未同意协议 → 原版禁用模式：GUI.color 灰染 + 可点提示）
            // 反编译实证（Widgets.cs:1467-1521）：active=false 只禁点击视觉不变；原版禁用视觉 = GUI.color 灰染 + ButtonText
            string btnLabel = expediteOrder ? "CelesFD_Keyed_PlaceExpedite".Translate() : "CelesFD_Keyed_PlaceOrder".Translate();
            GUI.color = agreeToTerms && cart.Count > 0 ? Color.white : new Color(0.5f, 0.5f, 0.5f, 0.9f);   // 细节批：补空购物车灰显（武备页同模式——两条件联查）
            if (Widgets.ButtonText(btnRect, btnLabel, drawBackground: true))
            {
                if (cart.Count == 0)
                    Messages.Message("CelesFD_Keyed_TradeCheckoutEmpty".Translate(), MessageTypeDefOf.RejectInput, false);   // 空购物车提示（同武备模式——细节批）
                else if (agreeToTerms)
                    gc.TryPlaceShoppingOrder(expediteOrder, subscribeArrival);   // M7-2：分币种扣款 + 物流单
                else
                    Messages.Message("CelesFD_Keyed_NeedAgreeTerms".Translate(), MessageTypeDefOf.RejectInput);
            }
            GUI.color = Color.white;
        }

        // 税分项 2×2 双列（用户规格：税名（比率）+ 税额右对齐；金额 = 精确值 2 位小数带删除线 + 向下取整整数——总价计算取整数）
        // 修复（2026-08-15）：税格 rect 固定 16f + 18f 步进 → 实测行高驱动（subLineH 与 contentH 同步）
        private static void DrawTaxGrid(float x, float y, float colW, float colGap, (string key, float rate)[] items, float creditBase)
        {
            float subLineH = CelesFD_UIConfig.ScaledLineHeight(CelesFD_UIConfig.FontSub);
            for (int i = 0; i < items.Length; i++)
            {
                float cx = x + (i % 2) * (colW + colGap);
                float cy = y + (i / 2) * (subLineH + 2f);
                string label = items[i].key.Translate().ToString() + "（" + (items[i].rate * 100f).ToString("0.#") + "%）";
                DrawScaledLabel(new Rect(cx, cy, colW * 0.55f, subLineH), label, CelesFD_UIConfig.FontSub);
                Rect amountRect = new Rect(cx + colW * 0.55f, cy, colW * 0.45f, subLineH);
                // 税分项金额：普通 2 位小数右对齐（划线+floor 仅税额总项——用户裁决）
                Text.Anchor = TextAnchor.MiddleRight;
                DrawScaledLabel(amountRect, (items[i].rate * creditBase).ToString("0.00"), CelesFD_UIConfig.FontSub);
                Text.Anchor = TextAnchor.UpperLeft;
            }
        }

        // 缩放文本（结算区共用；lineHeight 修复在 UIConfig.GetScaledStyle）
        private static void DrawScaledLabel(Rect rect, string text, float size)
        {
            Text.Font = GameFont.Small;
            GUIStyle style = CelesFD_UIConfig.GetScaledStyle(size);
            // G22 缓存 alignment 冻结在创建时——非默认 Text.Anchor 需克隆（交易页右对齐修复）
            if (Text.Anchor != TextAnchor.UpperLeft)
            {
                var clone = new GUIStyle(style);
                clone.alignment = Text.Anchor;
                GUI.Label(rect, text, clone);
            }
            else
            {
                GUI.Label(rect, text, style);   // 常见路径零克隆（G22 优化保留）
            }
        }

        // 双币种金额格式化（②：恒显双币种——保留密钥显示空间，密钥为 0 时显示"0 密钥"）
        private static string FormatBoth(float credit, float key)
        {
            string s = "CelesFD_Keyed_CreditAmount".Translate(credit.ToString("0.##")).ToString();
            s += " + " + "CelesFD_Keyed_KeyAmount".Translate(key.ToString("0.##")).ToString();
            return s;
        }

        // 可勾选文本行（②：Checkbox + Label 动态行高——文本一行/半行自动排布；返回实际行高供后续内容下移）
        private static float DrawCheckboxLabel(Rect rect, string label, ref bool value, float width)
        {
            bool result = value;
            Widgets.Checkbox(new Vector2(rect.x, rect.y), ref result);   // 原版签名：Checkbox(Vector2 topLeft, ref bool, float size=24f)
            value = result;
            float h = CalcLabelHeight(label, width - 26f);
            Widgets.Label(new Rect(rect.x + 26f, rect.y, width - 26f, h), label);
            return Mathf.Max(22f, h);
        }

        // 动态行高（WordWrap 文本；与 DrawCheckboxLabel 共用——结算 contentH 预计算同款）
        private static float CalcLabelHeight(string text, float width)
        {
            bool prevWrap = Text.WordWrap;
            Text.WordWrap = true;
            float h = Text.CalcHeight(text, width);
            Text.WordWrap = prevWrap;
            return h;
        }

        // 明细行：标签左对齐（40%——缩短，用户裁决总价/货物报价占用过宽）+ 数值右对齐（60%，超宽往复滚动不换行）
        // 修复（结算区下半部遮挡根因）：原 Widgets.Label 用默认 Small/Medium（lineHeight ~20/23）而 rect 18f/22f 不足——
        // 统一 DrawScaledLabel（缩放样式 lineHeight 随 fontSize 自动 ~17），rect 高度 ≥ 自动 lineHeight
        // 明细行（结算区数值统一划线+floor 模式——用户裁决，税分项除外）：
        //   标签左 40% + 数值右 60%：无小数直显整数；有小数 → 两位小数划线 + 向下取整整数
        private void DrawDetailLine(float x, float y, float width, float height, string label, float value, float size = CelesFD_UIConfig.FontMain)
        {
            DrawScaledLabel(new Rect(x, y, width * 0.4f, height), label, size);
            DrawFloorAmount(new Rect(x + width * 0.4f, y, width * 0.6f, height), value, size);
        }

        // 单值金额划线+floor（运费/总税费等——用户裁决：无小数直显；有小数 → 两位小数划线 + 向下取整，总价计算取整数）
        private static void DrawFloorAmount(Rect rect, float amount, float size = CelesFD_UIConfig.FontMain)
        {
            int floor = Mathf.FloorToInt(amount);
            Text.Anchor = TextAnchor.MiddleRight;
            if (amount > floor)
            {
                string exact = amount.ToString("0.00");
                string floorText = floor.ToString();
                GUIStyle style = CelesFD_UIConfig.GetScaledStyle(size);
                DrawScaledLabel(rect, exact + "  " + floorText, size);
                float floorW = style.CalcSize(new GUIContent(floorText)).x;
                float exactW = style.CalcSize(new GUIContent(exact)).x;
                float exactRight = rect.xMax - floorW - 4f;
                Widgets.DrawLineHorizontal(exactRight - exactW, rect.y + rect.height / 2f, exactW,
                    new Color(0.55f, 0.55f, 0.55f, 0.9f));
            }
            else
            {
                DrawScaledLabel(rect, floor.ToString(), size);
            }
            Text.Anchor = TextAnchor.UpperLeft;
        }

        // 双币种金额划线+floor（货物报价/总价——各币种值独立划线；恒显双币种保留密钥空间；右对齐）
        private static void DrawBothFloorAmount(Rect rect, float credit, float key, float size = CelesFD_UIConfig.FontMain)
        {
            string cText = BuildFloorText(credit);
            string kText = BuildFloorText(key);
            string full = cText + " 信用额 + " + kText + " 密钥";
            // G22 缓存 alignment 冻结——克隆 + 显式右对齐（交易页对齐修复）
            GUIStyle style = CelesFD_UIConfig.CloneScaledStyle(size);
            style.alignment = TextAnchor.MiddleRight;
            GUI.Label(rect, full, style);
            // 划线段定位（右对齐：从右往左）
            float right = rect.xMax;
            float kSuffixW = style.CalcSize(new GUIContent(" 密钥")).x;
            float kTextW = style.CalcSize(new GUIContent(kText)).x;
            float sepW = style.CalcSize(new GUIContent(" + ")).x;
            float cSuffixW = style.CalcSize(new GUIContent(" 信用额")).x;
            float cTextW = style.CalcSize(new GUIContent(cText)).x;
            float kExactW = key > Mathf.FloorToInt(key) ? style.CalcSize(new GUIContent(key.ToString("0.00"))).x : 0f;
            float cExactW = credit > Mathf.FloorToInt(credit) ? style.CalcSize(new GUIContent(credit.ToString("0.00"))).x : 0f;
            if (kExactW > 0f)
            {
                float kExactRight = right - kSuffixW - (kTextW - kExactW) - 2f;
                Widgets.DrawLineHorizontal(kExactRight - kExactW, rect.y + rect.height / 2f, kExactW,
                    new Color(0.55f, 0.55f, 0.55f, 0.9f));
            }
            if (cExactW > 0f)
            {
                float cExactRight = right - kSuffixW - kTextW - sepW - cSuffixW - (cTextW - cExactW) - 2f;
                Widgets.DrawLineHorizontal(cExactRight - cExactW, rect.y + rect.height / 2f, cExactW,
                    new Color(0.55f, 0.55f, 0.55f, 0.9f));
            }
        }

        // 划线文本构建："1.25 1"（有小数）或 "1"（无小数）
        private static string BuildFloorText(float v)
        {
            int floor = Mathf.FloorToInt(v);
            return v > floor ? v.ToString("0.00") + "  " + floor : floor.ToString();
        }

        // ═══ 购物车添加（§6.4 超重规则：当前 ≤1200 可任意添加（首单任意重量）；>1200 禁添加） ═══
        private void TryAddToCart(CelesFD_Order o)
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null || gc.ShoppingCart.Contains(o)) return;
            if (CelesFD_ShippingUtility.CalcTotalMass(gc.ShoppingCart) > MaxCartMass)
            {
                Messages.Message("CelesFD_Keyed_CartOverweight".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            gc.ShoppingCart.Add(o);
        }

        // ═══ 上方子页（？帮助介绍优先 > 选中订单 description；未选中 = 空） ═══
        public void DrawSubPage(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            // ？帮助（R-1 UI 改造）：交易页机制介绍
            if (infoActive)
            {
                CelesFD_UIConfig.DrawInfoText(rect.ContractedBy(10f), "CelesFD_Keyed_InfoTrade".Translate());
                return;
            }
            if (selectedOrder == null) return;
            // 刷新后订单对象可能失效 → 检测并清除
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc != null && !gc.MarketOrders.Contains(selectedOrder)) { selectedOrder = null; return; }
            string desc = selectedOrder.ResolveDescription();
            if (desc.NullOrEmpty()) return;
            Rect inner = rect.ContractedBy(10f);
            bool prevWrap = Text.WordWrap;
            Text.WordWrap = true;
            GUI.Label(inner, desc, Text.CurFontStyle);
            Text.WordWrap = prevWrap;
        }

        // ═══ 工具 ═══
        // FD-G24④（2026-09-02）：FormatPrice / GetCategoryColor 死代码删除（分别被 OrderCard 报酬自拼接与
        // CelesFD_UIConfig.CategoryColor 取代；grep 复核全工程无引用后移除）
    }
}
