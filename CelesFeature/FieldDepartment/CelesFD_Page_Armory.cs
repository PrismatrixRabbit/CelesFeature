using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // W-3 武备页（§2.2 布局 + §2.3 三态 + N1 冷却 + N3 额度）
    // 布局：上 70% = 左武备（额度条带 + 卡片网格）/ 右人员占位；下 30% = 统计页
    // 卡片：icon + 品名 + "- X(Y) +"（无色条带/角标——区别于交易页）
    public class CelesFD_Page_Armory : CelesFD_IPage, CelesFD_ISubPage
    {
        private const float BottomBarFraction = 0.3f;   // 底侧统计页 30%
        private const float TopStatusBarHeight = 30f;   // 额度条带 + 刷新按钮
        private const float CardGap = CelesFD_UIConfig.CardGap;

        // 三态草稿（defName → draft 增量——UI 状态，不存档，关窗即弃）
        private readonly Dictionary<string, int> drafts = new Dictionary<string, int>();
        private CelesFD_SupportDef selectedSupport;
        private Vector2 supportScrollPos;
        private Vector2 checkoutScrollPos;
        private bool pendingRefresh;                    // 刷新确认态
        private Rect refreshConfirmRect;

        public string Title => "CelesFD_Keyed_Tab_Armory".Translate();

        public void Notify_Deactivated()
        {
            drafts.Clear();
            selectedSupport = null;
            pendingRefresh = false;
        }

        // ═══ 主页面 ═══
        public void Draw(Rect inRect)
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return;

            // 上 70% / 下 30%
            Rect main = new Rect(inRect.x, inRect.y, inRect.width, inRect.height * (1f - BottomBarFraction));
            Rect bottom = new Rect(inRect.x, main.yMax, inRect.width, inRect.height * BottomBarFraction);
            Widgets.DrawMenuSection(main);
            Widgets.DrawMenuSection(bottom);

            // 主区域：左武备 / 右人员占位（修订 1：子区域各绘背景）
            Rect left = new Rect(main.x, main.y, main.width * 0.5f, main.height);
            Rect right = new Rect(main.x + main.width * 0.5f, main.y, main.width * 0.5f, main.height);
            Widgets.DrawMenuSection(left);
            Widgets.DrawMenuSection(right);
            DrawLeftPane(left.ContractedBy(4f), gc);
            DrawRightPlaceholder(right.ContractedBy(4f));

            // 底侧统计页
            DrawCheckout(bottom.ContractedBy(4f), gc);
        }

        // ═══ 左栏：额度条带 + 支援卡片网格 ═══
        private void DrawLeftPane(Rect rect, CelesFD_GameComponent gc)
        {
            float curY = rect.y;

            // 额度条带（30px）
            Rect statusBar = new Rect(rect.x, curY, rect.width, TopStatusBarHeight);
            DrawQuotaBar(statusBar, gc);
            curY += TopStatusBarHeight + CardGap;

            // 按等级分组（仅显示到当前等级 +1）
            int currentLevel = gc.GetEffectiveLevel();
            int maxShowLevel = currentLevel + 1;
            var levelGroups = DefDatabase<CelesFD_SupportDef>.AllDefsListForReading
                .Where(d => d.supportType == CelesFD_SupportType.CombatEquipment && d.unlockLevel <= maxShowLevel)
                .GroupBy(d => d.unlockLevel)
                .OrderBy(g => g.Key)
                .ToList();

            // 预计算内容高度（等级头 + 卡片行）
            float cardW = (rect.width - CardGap - 16f - 4f) / 2f;   // 预留滚动条 16px + 间隙
            float cardH = Mathf.Max(cardW / CelesFD_UIConfig.CardAspect, CelesFD_UIConfig.CardMinHeight);
            float levelHeaderH = cardH * 0.5f;
            float contentH = 0f;
            foreach (var group in levelGroups)
            {
                contentH += levelHeaderH;
                int rowCount = Mathf.CeilToInt(group.Count() / 2f);
                contentH += rowCount * (cardH + CardGap);
            }
            contentH += 10f;   // 底部余量

            Rect listRect = new Rect(rect.x, curY, rect.width, rect.yMax - curY);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, contentH);
            Widgets.BeginScrollView(listRect, ref supportScrollPos, viewRect);

            float cardY = 0f;
            foreach (var group in levelGroups)
            {
                // 等级头（半卡片高）：文字 + 横线
                var headerStyle = CelesFD_UIConfig.GetScaledStyle(CelesFD_UIConfig.FontSub);
                Color prevColor = GUI.color;
                GUI.color = group.Key <= currentLevel ? Color.white : Color.gray;
                Widgets.Label(new Rect(0f, cardY, viewRect.width, levelHeaderH * 0.5f),
                    string.Format("等级 {0}", group.Key));
                GUI.color = prevColor;
                // 分割线
                float lineY = cardY + levelHeaderH * 0.5f;
                Widgets.DrawLineHorizontal(0f, lineY, viewRect.width);
                cardY += levelHeaderH;

                // 该等级的卡片（双列）
                var supports = group.ToList();
                for (int i = 0; i < supports.Count; i++)
                {
                    int col = i % 2;
                    float cardX = col * (cardW + CardGap);
                    Rect cardRect = new Rect(cardX, cardY, cardW, cardH);
                    DrawSupportCard(cardRect, supports[i], gc);
                    if (col == 1 || i == supports.Count - 1) cardY += cardH + CardGap;
                }
            }
            Widgets.EndScrollView();
        }

        // 额度条带 + 刷新按钮
        private void DrawQuotaBar(Rect rect, CelesFD_GameComponent gc)
        {
            int remaining = gc.QuotaRemaining;
            int cap = gc.QuotaCap;
            // 文本（左）
            Rect labelRect = new Rect(rect.x, rect.y, rect.width * 0.6f, rect.height);
            Widgets.Label(labelRect, "CelesFD_Keyed_ArmoryQuotaLine".Translate(remaining, cap));
            // 刷新按钮（右）
            int cost = 2 * gc.weaponRefreshCount + 2;
            string btnLabel = "CelesFD_Keyed_ArmoryRefreshButton".Translate(cost);
            Rect btnRect = new Rect(rect.xMax - 140f, rect.y, 140f, rect.height);
            if (pendingRefresh)
            {
                // 确认态
                refreshConfirmRect = btnRect;
                if (Widgets.ButtonText(btnRect, "CelesFD_Keyed_ArmoryRefreshConfirm".Translate(), true, true, Color.yellow))
                {
                    if (gc.TryManualWeaponRefresh()) pendingRefresh = false;
                }
                // 失焦回退
                if (Event.current.type == EventType.MouseDown && !btnRect.Contains(Event.current.mousePosition))
                    pendingRefresh = false;
            }
            else
            {
                if (Widgets.ButtonText(btnRect, btnLabel))
                {
                    pendingRefresh = true;
                }
            }
        }

        // 支援卡片（终版：克隆 GUIStyle 模式——G22 缓存不变异 + 自定义字号 + 独立 alignment）
        private void DrawSupportCard(Rect rect, CelesFD_SupportDef def, CelesFD_GameComponent gc)
        {
            bool levelLocked = def.unlockLevel > gc.GetEffectiveLevel();
            int avail = gc.AvailableCount(def);
            int pend = gc.PendingCount(def);
            int draft = drafts.TryGetValue(def.defName, out int d) ? d : 0;
            int displayY = pend + draft;

            Widgets.DrawBoxSolid(rect, new Color(0.13f, 0.13f, 0.13f, 0.6f));

            // 克隆样式（每次独立实例——不污染 G22 缓存）
            GUIStyle nameStyle = new GUIStyle(CelesFD_UIConfig.GetScaledStyle(CelesFD_UIConfig.FontSub));
            GUIStyle priceStyle = new GUIStyle(CelesFD_UIConfig.GetScaledStyle(CelesFD_UIConfig.FontDir));
            GUIStyle numStyle = new GUIStyle(CelesFD_UIConfig.GetScaledStyle(CelesFD_UIConfig.FontSub));

            // ── 左区：icon ──
            float iconSize = rect.height * 0.7f;
            Rect iconRect = new Rect(rect.x + 4f, rect.y + 2f, iconSize, iconSize);
            if (!def.uiIcon.NullOrEmpty())
            {
                Texture2D icon = ContentFinder<Texture2D>.Get(def.uiIcon, false);
                if (icon != null)
                {
                    GUI.color = levelLocked ? Color.gray : Color.white;
                    GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
                    GUI.color = Color.white;
                }
            }

            // ── 品名：整宽·LowerLeft（底部）──
            float nameH = CelesFD_UIConfig.ScaledLineHeight(CelesFD_UIConfig.FontSub);
            Rect nameRect = new Rect(rect.x + 4f, rect.yMax - nameH - 2f, rect.width - 8f, nameH);
            nameStyle.alignment = TextAnchor.LowerLeft;
            GUI.color = levelLocked ? Color.gray : Color.white;
            GUI.Label(nameRect, def.LabelCap, nameStyle);
            GUI.color = Color.white;

            // ── 右区 ──
            float rightX = rect.x + iconSize + 10f;
            float rightW = rect.xMax - rightX - 4f;
            Rect rightArea = new Rect(rightX, rect.y + 2f, rightW, rect.height - 4f);

            if (levelLocked)
            {
                GUIStyle lockStyle = new GUIStyle(nameStyle);
                lockStyle.alignment = TextAnchor.MiddleCenter;
                lockStyle.wordWrap = true;
                GUI.color = new Color(0.7f, 0.7f, 0.7f, 0.8f);
                GUI.Label(rightArea, "CelesFD_Keyed_ArmoryLevelLocked".Translate(), lockStyle);
                GUI.color = Color.white;
                return;
            }

            // ── 价格：两行·MiddleCenter（右区上方）──
            float priceH = CelesFD_UIConfig.ScaledLineHeight(CelesFD_UIConfig.FontDir);
            priceStyle.alignment = TextAnchor.MiddleCenter;
            string priceText = "";
            if (def.creditCost > 0) priceText += def.creditCost + "信用额";
            if (def.keyCost > 0) priceText += (priceText.Length > 0 ? " " : "") + def.keyCost + "密钥";
            GUI.Label(new Rect(rightArea.x, rightArea.y + 2f, rightArea.width, priceH), "单价：", priceStyle);
            if (priceText.Length > 0)
                GUI.Label(new Rect(rightArea.x, rightArea.y + 2f + priceH, rightArea.width, priceH), priceText, priceStyle);

            // ── ±按钮行：MiddleCenter（右区中部·下移后）──
            float btnSize = 18f;
            float numW = 50f;
            float ctrlW = btnSize * 2 + numW + 12f;
            float ctrlX = rightArea.center.x - ctrlW / 2f;
            float ctrlY = rightArea.y + rightArea.height * 0.55f - btnSize / 2f;

            Rect minusRect = new Rect(ctrlX, ctrlY, btnSize, btnSize);
            Rect numRect = new Rect(minusRect.xMax + 4f, ctrlY, numW, btnSize);
            Rect plusRect = new Rect(numRect.xMax + 4f, ctrlY, btnSize, btnSize);

            numStyle.alignment = TextAnchor.MiddleCenter;
            GUI.Label(numRect, string.Format("{0}({1})", avail, displayY), numStyle);

            bool minusDisabled = draft <= 0;
            if (minusDisabled) GUI.enabled = false;
            if (Widgets.ButtonText(minusRect, "-", true, true, minusDisabled ? Color.gray : Color.white))
            {
                if (draft > 0) { drafts[def.defName] = draft - 1; if (drafts[def.defName] <= 0) drafts.Remove(def.defName); }
            }
            GUI.enabled = true;

            bool plusDisabled = avail + pend + draft >= def.maxTotal;
            if (plusDisabled) GUI.enabled = false;
            if (Widgets.ButtonText(plusRect, "+", true, true, plusDisabled ? Color.gray : Color.white))
            {
                if (avail + pend + draft < def.maxTotal) drafts[def.defName] = draft + 1;
            }
            GUI.enabled = true;

            if (Widgets.ButtonInvisible(rect, false))
            {
                selectedSupport = def;
            }
        }

        // ═══ 右栏占位（R 系列） ═══
        private void DrawRightPlaceholder(Rect rect)
        {
            Rect center = new Rect(rect.center.x - 80f, rect.center.y - 20f, 160f, 40f);
            Widgets.Label(center, "人员支援\n（即将上线）");
        }

        // ═══ 底侧统计页（D1 三区拆分：品名居左 / ······ 居中 / 价格居右） ═══
        private void DrawCheckout(Rect rect, CelesFD_GameComponent gc)
        {
            float curY = rect.y;
            Rect scrollRect = new Rect(rect.x, curY, rect.width, rect.height * 0.6f);
            var draftItems = drafts.Where(kv => kv.Value > 0).ToList();
            float lineH = CelesFD_UIConfig.ScaledLineHeight(CelesFD_UIConfig.FontSub) + 2f;
            float contentH = draftItems.Count * lineH + 10f;
            Rect viewRect = new Rect(0f, 0f, scrollRect.width - 16f, contentH);
            Widgets.BeginScrollView(scrollRect, ref checkoutScrollPos, viewRect);

            // 三区样式（克隆实例——独立 alignment 不污染缓存）
            GUIStyle leftStyle = new GUIStyle(CelesFD_UIConfig.GetScaledStyle(CelesFD_UIConfig.FontSub));
            leftStyle.alignment = TextAnchor.LowerLeft;
            GUIStyle centerStyle = new GUIStyle(leftStyle);
            centerStyle.alignment = TextAnchor.MiddleCenter;
            GUIStyle rightStyle = new GUIStyle(leftStyle);
            rightStyle.alignment = TextAnchor.LowerRight;

            float lineY = 0f;
            int totalCredit = 0, totalKey = 0;
            foreach (var kv in draftItems)
            {
                CelesFD_SupportDef def = DefDatabase<CelesFD_SupportDef>.GetNamedSilentFail(kv.Key);
                if (def == null) continue;
                int credit = def.creditCost * kv.Value;
                int key = def.keyCost * kv.Value;
                totalCredit += credit;
                totalKey += key;

                // 组装右侧文本（时长 + 价格——无模板硬编码后缀）
                string right = "";
                if (def.applyDelayHours > 0)
                    right += "CelesFD_Keyed_ArmoryApplyTime".Translate(def.applyDelayHours);
                string price = "";
                if (key > 0) price += key + "密钥";
                if (credit > 0) price += (price.Length > 0 ? " " : "") + credit + "信用额";
                if (right.Length > 0 && price.Length > 0) right += " ";
                right += price;

                // 三区独立绘制
                float w = viewRect.width;
                GUI.Label(new Rect(0f, lineY, w * 0.4f, lineH), def.LabelCap, leftStyle);
                GUI.Label(new Rect(w * 0.4f, lineY, w * 0.2f, lineH), "··················", centerStyle);
                GUI.Label(new Rect(w * 0.6f, lineY, w * 0.4f, lineH), right, rightStyle);
                lineY += lineH;
            }
            Widgets.EndScrollView();
            curY += scrollRect.height + 4f;

            // 下半：总计 + 确认支付
            Rect totalRect = new Rect(rect.x, curY, rect.width * 0.6f, rect.height * 0.3f);
            Widgets.Label(totalRect, "CelesFD_Keyed_ArmoryTotal".Translate(totalKey, totalCredit));
            Rect btnRect = new Rect(rect.xMax - 140f, curY, 140f, rect.height * 0.3f);
            if (draftItems.Count > 0 && Widgets.ButtonText(btnRect, "CelesFD_Keyed_ArmoryCheckout".Translate()))
            {
                if (gc.TrySubmitSupportOrder(drafts))
                {
                    drafts.Clear();
                    Messages.Message("CelesFD_Keyed_ArmoryDraftCleared".Translate(), MessageTypeDefOf.PositiveEvent);
                }
            }
        }

        // ═══ 正上子页：选中支援 description（修订 1：补背景 + 未选中文本） ═══
        public void DrawSubPage(Rect rect)
        {
            // 背景绘制（Page_Trade 同款——缺失此调用则边框/底色不渲染）
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(4f);

            if (selectedSupport == null)
            {
                Widgets.Label(inner, "未选中武备");
                return;
            }
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(selectedSupport.LabelCap);
            sb.AppendLine("CelesFD_Keyed_CreditAmount".Translate(selectedSupport.creditCost));
            if (selectedSupport.keyCost > 0) sb.AppendLine("CelesFD_Keyed_KeyAmount".Translate(selectedSupport.keyCost));
            if (selectedSupport.applyDelayHours > 0)
                sb.AppendLine("CelesFD_Keyed_ArmoryApplyTime".Translate(selectedSupport.applyDelayHours));
            if (selectedSupport.cooldownTicks > 0)
                sb.AppendLine("CelesFD_Keyed_ArmoryCooldown".Translate(selectedSupport.cooldownTicks / 60));
            sb.AppendLine("CelesFD_Keyed_ArmoryOccupiesQuota".Translate(
                selectedSupport.occupiesQuota ? "是" : "否"));
            if (gc != null)
            {
                sb.AppendLine("可用：" + gc.AvailableCount(selectedSupport) + " 审批中：" + gc.PendingCount(selectedSupport));
            }
            if (!selectedSupport.description.NullOrEmpty()) sb.AppendLine(selectedSupport.description);
            Widgets.Label(inner, sb.ToString().TrimEnd('\n'));
        }
    }
}
