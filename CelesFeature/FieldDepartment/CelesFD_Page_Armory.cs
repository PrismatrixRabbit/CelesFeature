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
        private float weaponCardH = CelesFD_UIConfig.CardMinHeight;   // 左栏卡片高（右栏人员卡共用——高度一致裁决）

        public string Title => "CelesFD_Keyed_Tab_Armory".Translate();

        public void Notify_Deactivated()
        {
            drafts.Clear();
            selectedSupport = null;
            pendingRefresh = false;
            personnelDays.Clear();
            infoActive = false;
        }
        private string pendingRecallDef;   // 提前召回确认态（defName——刷新按钮确认模式）
        private bool agreeArmory = true;   // 协议勾选（交易页同模式——默认勾选）

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
            DrawRightPane(right.ContractedBy(4f), gc);   // R-1：人员单列（替占位）

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

            // 预计算内容高度（等级头 + 卡片行）——等级头高度与 helper 返回一致（实测行高 + 7f）
            float cardW = (rect.width - CardGap - 16f - 4f) / 2f;   // 预留滚动条 16px + 间隙
            float cardH = Mathf.Max(cardW / CelesFD_UIConfig.CardAspect, CelesFD_UIConfig.CardMinHeight);
            weaponCardH = cardH;   // 右栏人员卡共用（高度一致裁决）
            float levelHeaderH = CelesFD_UIConfig.ScaledLineHeight(CelesFD_UIConfig.FontSub) + 7f;
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
                // 等级头（共用 helper——ScaledLineHeight 实测驱动，修截断）
                cardY += CelesFD_UIConfig.DrawLevelHeader(cardY, viewRect.width, group.Key, currentLevel);

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
            GUIStyle nameStyle = CelesFD_UIConfig.CloneScaledStyle(CelesFD_UIConfig.FontSub);
            GUIStyle priceStyle = CelesFD_UIConfig.CloneScaledStyle(CelesFD_UIConfig.FontDir);
            GUIStyle numStyle = CelesFD_UIConfig.CloneScaledStyle(CelesFD_UIConfig.FontSub);

            // ── 左区：icon ──
            float iconSize = rect.height * 0.7f;
            Rect iconRect = new Rect(rect.x + 4f, rect.y + 2f, iconSize, iconSize);
            if (!def.uiIcon.NullOrEmpty())
            {
                Texture2D icon = ContentFinder<Texture2D>.Get(def.uiIcon, false);
                if (icon != null)
                {
                    // W-5 清理：不透明灰（Color.gray 含 alpha 0.5 会变透明——用纯灰 RGB 保留 alpha 1）
                    GUI.color = levelLocked ? new Color(0.4f, 0.4f, 0.4f, 1f) : Color.white;
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
                CelesFD_UIConfig.DrawLockBand(rect);   // 封锁线（细节批——同人员卡）
                return;
            }

            // ── 价格：两行·MiddleCenter（右区上方）──
            float priceH = CelesFD_UIConfig.ScaledLineHeight(CelesFD_UIConfig.FontDir);
            priceStyle.alignment = TextAnchor.MiddleCenter;
            string priceText = FormatSupportPrice(def);
            GUI.Label(new Rect(rightArea.x, rightArea.y + 2f, rightArea.width, priceH), "CelesFD_Keyed_ArmoryPriceLabel".Translate(), priceStyle);
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
            if (Widgets.ButtonText(minusRect, "-", true, true, minusDisabled ? Color.gray : Color.white))
            {
                if (draft > 0) { drafts[def.defName] = draft - 1; if (drafts[def.defName] <= 0) drafts.Remove(def.defName); }
            }

            bool plusDisabled = avail + pend + draft >= def.maxTotal;
            if (Widgets.ButtonText(plusRect, "+", true, true, plusDisabled ? Color.gray : Color.white))
            {
                if (avail + pend + draft < def.maxTotal) drafts[def.defName] = draft + 1;
            }

            if (Widgets.ButtonInvisible(rect, false))
            {
                selectedSupport = def;
            }
        }

        // ═══ 右栏：人员支援单列（R-1 UI 改造终版——严格武备页格式：30px 顶带+？按钮 / 等级头 helper /
        //     卡片骨架 helper / 五行价格明细 / 天数草稿默认 0，+1 入购物车统一结账）═══
        private readonly Dictionary<string, int> personnelDays = new Dictionary<string, int>();   // 天数草稿（默认 0=未选购）
        private Vector2 personnelScrollPos;
        private bool infoActive;   // ？帮助激活（子页显示武备+人员完整介绍；优先于 selectedSupport）

        private void DrawRightPane(Rect rect, CelesFD_GameComponent gc)
        {
            // 30px 顶部条带（同武备侧额度条带高度——空隙）+ 右上 ？按钮（正方 30px）
            if (CelesFD_UIConfig.DrawHelpButton(rect.xMax - 24f, rect.y, 24f))
                infoActive = !infoActive;

            int currentLevel = gc.GetEffectiveLevel();
            int maxShowLevel = currentLevel + 1;
            var levelGroups = DefDatabase<CelesFD_SupportDef>.AllDefsListForReading
                .Where(d => d.supportType == CelesFD_SupportType.Personnel && d.unlockLevel <= maxShowLevel)
                .GroupBy(d => d.unlockLevel)
                .OrderBy(g => g.Key)
                .ToList();
            if (levelGroups.Count == 0)
            {
                Widgets.Label(new Rect(rect.center.x - 80f, rect.y + 40f, 160f, 40f),
                    "CelesFD_Keyed_ArmoryPersonnelPlaceholder".Translate());
                return;
            }

            float cardH = weaponCardH;   // 卡高与武备页一致（左栏先绘已存）
            float headerH = CelesFD_UIConfig.ScaledLineHeight(CelesFD_UIConfig.FontSub) + 7f;
            float contentH = 10f;
            foreach (var group in levelGroups)
                contentH += headerH + group.Count() * (cardH + CardGap);

            Rect listRect = new Rect(rect.x, rect.y + 30f + CardGap, rect.width, rect.yMax - rect.y - 30f - CardGap);
            Widgets.BeginScrollView(listRect, ref personnelScrollPos, new Rect(0f, 0f, rect.width - 16f, contentH));
            float cardY = 0f;
            foreach (var group in levelGroups)
            {
                cardY += CelesFD_UIConfig.DrawLevelHeader(cardY, rect.width - 16f, group.Key, currentLevel);
                foreach (CelesFD_SupportDef def in group)
                {
                    DrawPersonnelCard(new Rect(0f, cardY, rect.width - 16f, cardH), def, gc, currentLevel);
                    cardY += cardH + CardGap;
                }
            }
            Widgets.EndScrollView();
        }

        // 人员卡片：骨架 helper + 三行二列（表头行+两数据行；1f 改版）+ 状态区（±/召回/拒绝）
        // 三态：等级不足（灰显+天数区拒绝文本）/ 支援已发出（天数区替换+提前召回）/ 正常（[- 0天 +]）
        private void DrawPersonnelCard(Rect rect, CelesFD_SupportDef def, CelesFD_GameComponent gc, int currentLevel)
        {
            bool levelLocked = def.unlockLevel > currentLevel;
            int days = personnelDays.TryGetValue(def.defName, out int d) ? d : 0;
            float rowH = CelesFD_UIConfig.ScaledLineHeight(CelesFD_UIConfig.FontDir);
            float nameH = CelesFD_UIConfig.ScaledLineHeight(CelesFD_UIConfig.FontSub);

            // 锁定：骨架（无点击）+ 全卡居中灰显拒绝（价格明细隐藏——风格批 1 补充）
            if (levelLocked)
            {
                CelesFD_UIConfig.DrawCardBackdrop(rect, def.uiIcon, def.LabelCap, true, out _, checkClick: false);
                CelesFD_UIConfig.DrawLockBand(rect);   // 封锁线（细节批——价格明细隐藏+居中略大字）
                return;
            }

            CelesFD_UIConfig.DrawCardBackdrop(rect, def.uiIcon, def.LabelCap, false, out Rect body, checkClick: false);
            GUIStyle rowStyle = CelesFD_UIConfig.CloneScaledStyle(CelesFD_UIConfig.FontDir);
            rowStyle.alignment = TextAnchor.LowerLeft;

            // 行 1：价格明细：（锁定隐藏已上行处理）
            GUI.Label(new Rect(body.x, body.y, body.width * 0.5f, rowH),
                "CelesFD_Keyed_PersonnelPriceHeader".Translate(), rowStyle);

            // 行 2-3：两列（燃油|雇佣 / 保险|额外——额外全 0 右列空）
            float colW = body.width / 2f;
            float y = body.y + rowH + 3f;
            GUI.Label(new Rect(body.x, y, colW, rowH),
                "CelesFD_Keyed_PersonnelFuelRow".Translate("CelesFD_Keyed_CreditAmount".Translate(def.fuelFeePerPawn)), rowStyle);
            GUI.Label(new Rect(body.x + colW, y, colW, rowH),
                "CelesFD_Keyed_PersonnelWageRow".Translate("CelesFD_Keyed_CreditAmount".Translate(def.dailyWage)), rowStyle);
            y += rowH + 2f;
            GUI.Label(new Rect(body.x, y, colW, rowH),
                "CelesFD_Keyed_PersonnelInsuranceRow".Translate("CelesFD_Keyed_CreditAmount".Translate(def.insuranceCost)), rowStyle);
            string extra = FormatSupportPrice(def);
            if (extra.Length > 0)
                GUI.Label(new Rect(body.x + colW, y, colW, rowH), "CelesFD_Keyed_PersonnelExtraRow".Translate(extra), rowStyle);

            // 底部状态行：水平 = 右列价格文本起点；垂直 = 紧贴下沿与品名同高（点位裁决 2026-09-06）
            Rect ctrl = new Rect(body.x + colW, rect.yMax - nameH - 2f, 100f, nameH);   // 宽 100px（用户裁决 2026-09-07）
            CelesFD_PersonnelOrder.Phase? orderPhase = gc.GetPersonnelPhase(def.defName);
            if (orderPhase == CelesFD_PersonnelOrder.Phase.Deployed || orderPhase == CelesFD_PersonnelOrder.Phase.LeavingSoon)
            {
                // 已在场：召回流程（确认态——刷新按钮模式；失焦回退）
                string btn = pendingRecallDef == def.defName
                    ? "CelesFD_Keyed_PersonnelRecallConfirm".Translate()
                    : "CelesFD_Keyed_PersonnelEarlyRecall".Translate();
                if (Widgets.ButtonText(ctrl, btn))
                {
                    if (pendingRecallDef == def.defName) { pendingRecallDef = null; gc.TryEarlyRecall(def); }
                    else pendingRecallDef = def.defName;
                }
                if (pendingRecallDef == def.defName && Event.current.type == EventType.MouseDown
                    && !ctrl.Contains(Event.current.mousePosition)) pendingRecallDef = null;
            }
            else if (orderPhase != null)   // Transit / Incoming——未抵达：灰显可点提示"即将抵达"
            {
                GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.9f);
                if (Widgets.ButtonText(ctrl, "CelesFD_Keyed_PersonnelEarlyRecall".Translate(), drawBackground: true))
                    Messages.Message("CelesFD_Keyed_PersonnelNotArrived".Translate(), MessageTypeDefOf.RejectInput, false);
                GUI.color = Color.white;
            }
            else
            {
                // 左-右+（18 宽 × 品名高）；±不查资金——结算时才确认（裁决）
                Rect minus = new Rect(ctrl.x, ctrl.y, 18f, nameH);
                Rect num = new Rect(minus.xMax + 4f, ctrl.y, 52f, nameH);
                Rect plus = new Rect(num.xMax + 4f, ctrl.y, 18f, nameH);
                if (Widgets.ButtonText(minus, "-") && days > 0)
                {
                    if (days <= 1) personnelDays.Remove(def.defName);
                    else personnelDays[def.defName] = days - 1;
                }
                GUIStyle numStyle = CelesFD_UIConfig.CloneScaledStyle(CelesFD_UIConfig.FontDir);
                numStyle.alignment = TextAnchor.MiddleCenter;
                GUI.Label(num, "CelesFD_Keyed_PersonnelDaysN".Translate(days), numStyle);
                if (Widgets.ButtonText(plus, "+") && days < def.maxStayDays)
                    personnelDays[def.defName] = days + 1;
            }

            // 整卡点击最后注册（先绘先消费——±/召回优先；武备卡同序）
            if (Widgets.ButtonInvisible(rect, false))
            {
                selectedSupport = def;
                infoActive = false;
            }
        }

        // ═══ 底侧统计页（D1 三区；R-1 扩展：人员行两行制 + 总计合并 + 空时灰显点击提示 + 即时审批）═══
        private void DrawCheckout(Rect rect, CelesFD_GameComponent gc)
        {
            float curY = rect.y;
            Rect scrollRect = new Rect(rect.x, curY, rect.width, rect.height * 0.6f);
            var draftItems = drafts.Where(kv => kv.Value > 0).ToList();
            var personnelItems = personnelDays.Where(kv => kv.Value > 0).ToList();
            float lineH = CelesFD_UIConfig.ScaledLineHeight(CelesFD_UIConfig.FontSub) + 2f;
            float contentH = (draftItems.Count + personnelItems.Count * 2) * lineH + 10f;
            Rect viewRect = new Rect(0f, 0f, scrollRect.width - 16f, contentH);
            Widgets.BeginScrollView(scrollRect, ref checkoutScrollPos, viewRect);

            // 三区样式（克隆实例——独立 alignment 不污染缓存）
            GUIStyle leftStyle = CelesFD_UIConfig.CloneScaledStyle(CelesFD_UIConfig.FontSub);
            leftStyle.alignment = TextAnchor.LowerLeft;
            GUIStyle centerStyle = new GUIStyle(leftStyle);
            centerStyle.alignment = TextAnchor.MiddleCenter;
            GUIStyle rightStyle = new GUIStyle(leftStyle);
            rightStyle.alignment = TextAnchor.LowerRight;

            float lineY = 0f;
            int totalCredit = 0, totalKey = 0;
            // ── 武备行（原逻辑 + applyDelay=0 → 即时审批）──
            foreach (var kv in draftItems)
            {
                CelesFD_SupportDef def = DefDatabase<CelesFD_SupportDef>.GetNamedSilentFail(kv.Key);
                if (def == null) continue;
                int credit = def.creditCost * kv.Value;
                int key = def.keyCost * kv.Value;
                totalCredit += credit;
                totalKey += key;
                string right = def.applyDelayHours > 0
                    ? "CelesFD_Keyed_ArmoryApplyTime".Translate(def.applyDelayHours)
                    : "CelesFD_Keyed_ArmoryApplyTimeNow".Translate();
                string price = "";
                if (key > 0) price = "CelesFD_Keyed_KeyAmount".Translate(key);
                if (credit > 0)
                {
                    string c = "CelesFD_Keyed_CreditAmount".Translate(credit);
                    price = price.Length > 0 ? price + " " + c : c;
                }
                right += " " + price;
                float w = viewRect.width;
                GUI.Label(new Rect(0f, lineY, w * 0.3f, lineH), def.LabelCap, leftStyle);   // 对齐人员侧 0.3/0.12/0.6（细节批）
                GUI.Label(new Rect(w * 0.3f, lineY, w * 0.12f, lineH), "··················", centerStyle);
                GUI.Label(new Rect(w * 0.6f, lineY, w * 0.4f, lineH), right.Trim(), rightStyle);
                lineY += lineH;
            }
            // ── 人员行（两行制：L1 = 品名···[抵达+燃油算式+雇佣算式 右]；L2 = [保险+额外+总价 右]）──
            foreach (var kv in personnelItems)
            {
                CelesFD_SupportDef def = DefDatabase<CelesFD_SupportDef>.GetNamedSilentFail(kv.Key);
                if (def == null) continue;
                int days = kv.Value;
                int head = def.personnel.Sum(e => e.amount);
                int fuel = def.fuelFeePerPawn * head;
                int wage = def.dailyWage * days;
                int credit = fuel + wage + def.insuranceCost + def.creditCost;
                int key = def.keyCost;
                totalCredit += credit;
                totalKey += key;
                // L1 右段：抵达时间 + 两算式（均带单位）
                string arrive = def.arrivalMode == CelesFD_PersonnelArrivalMode.Instant
                    ? "CelesFD_Keyed_PersonnelArriveNow".Translate()
                    : "CelesFD_Keyed_PersonnelArriveH".Translate(
                        def.arrivalMode == CelesFD_PersonnelArrivalMode.Expedited ? 1 : 24);
                string fuelCalc = "CelesFD_Keyed_PersonnelFuelCalc".Translate(
                    "CelesFD_Keyed_CreditAmount".Translate(def.fuelFeePerPawn), head.ToString(),
                    "CelesFD_Keyed_CreditAmount".Translate(fuel));
                string wageCalc = "CelesFD_Keyed_PersonnelWageCalc".Translate(
                    "CelesFD_Keyed_CreditAmount".Translate(def.dailyWage), days.ToString(),
                    "CelesFD_Keyed_CreditAmount".Translate(wage));
                float w = viewRect.width;
                GUI.Label(new Rect(0f, lineY, w * 0.3f, lineH), def.LabelCap, leftStyle);
                GUI.Label(new Rect(w * 0.3f, lineY, w * 0.12f, lineH), "··················", centerStyle);
                GUI.Label(new Rect(w * 0.4f, lineY, w * 0.6f, lineH),
                    (arrive + "  " + fuelCalc + "  " + wageCalc), rightStyle);
                lineY += lineH;
                // L2 右段：保险 + 额外（非零）+ 总价（非零币种段隐藏——段列表构建防残留空格）
                var segs = new List<string>();
                segs.Add("CelesFD_Keyed_PersonnelInsuranceCalc".Translate(
                    "CelesFD_Keyed_CreditAmount".Translate(def.insuranceCost)));
                string extra = FormatSupportPrice(def);
                if (extra.Length > 0) segs.Add("CelesFD_Keyed_PersonnelExtraCalc".Translate(extra));
                var totalSegs = new List<string>();
                if (key > 0) totalSegs.Add("CelesFD_Keyed_KeyAmount".Translate(key));
                if (credit > 0) totalSegs.Add("CelesFD_Keyed_CreditAmount".Translate(credit));
                segs.Add("CelesFD_Keyed_PersonnelLineTotal".Translate(string.Join(" ", totalSegs.ToArray())));
                GUI.Label(new Rect(w * 0.4f, lineY, w * 0.6f, lineH), string.Join("  ", segs.ToArray()), rightStyle);
                lineY += lineH;
            }
            Widgets.EndScrollView();
            curY += scrollRect.height + 4f;

            // 下半：总计 + 协议勾选（交易页同模式）+ 确认支付（灰显可点击 → 分支提示）
            bool hasContent = draftItems.Count > 0 || personnelItems.Count > 0;
            Rect totalRect = new Rect(rect.x, curY, rect.width * 0.6f, rect.height * 0.3f);
            Widgets.Label(totalRect, "CelesFD_Keyed_ArmoryTotal".Translate(totalKey, totalCredit));
            // 协议勾选：确认支付左侧（交易页 agreeToTerms 同模式——默认勾选）
            // 勾选（交易页同款 helper：24px 原版 checkbox + 26px 偏移 + WordWrap + 默认字体——细节批对齐）
            bool agree = agreeArmory;
            CelesFD_UIConfig.DrawCheckboxLabel(new Rect(rect.xMax - 140f - 20f - 210f, curY, 210f, 0f),
                "CelesFD_Keyed_AgreeArmory".Translate(), ref agree, 210f);
            agreeArmory = agree;
            Rect btnRect = new Rect(rect.xMax - 140f, curY, 140f, rect.height * 0.3f);
            // 交易页三行模式（前染 GUI.color → 按钮 → 恢复）——textColor 参数只染文字不染背景（根因修正）
            GUI.color = hasContent && agreeArmory ? Color.white : new Color(0.5f, 0.5f, 0.5f, 0.9f);
            if (Widgets.ButtonText(btnRect, "CelesFD_Keyed_ArmoryCheckout".Translate(), drawBackground: true))
            {
                if (!agreeArmory)
                {
                    Messages.Message("CelesFD_Keyed_AgreeArmoryFirst".Translate(), MessageTypeDefOf.RejectInput, false);
                    return;
                }
                if (!hasContent)
                {
                    Messages.Message("CelesFD_Keyed_ArmoryCheckoutEmpty".Translate(), MessageTypeDefOf.RejectInput, false);
                    return;
                }
                bool anySuccess = false;
                if (draftItems.Count > 0 && gc.TrySubmitSupportOrder(drafts))
                {
                    drafts.Clear();
                    anySuccess = true;
                }
                foreach (var kv in personnelItems.ToList())
                {
                    CelesFD_SupportDef def = DefDatabase<CelesFD_SupportDef>.GetNamedSilentFail(kv.Key);
                    if (def == null) continue;
                    if (gc.TryOrderPersonnel(def, kv.Value, Find.CurrentMap, out string failKey))
                    {
                        personnelDays.Remove(kv.Key);
                        anySuccess = true;
                    }
                    else
                        Messages.Message(failKey.Translate(), MessageTypeDefOf.RejectInput, false);
                }
                if (anySuccess)
                    Messages.Message("CelesFD_Keyed_ArmoryDraftCleared".Translate(), MessageTypeDefOf.PositiveEvent);
            }
            GUI.color = Color.white;   // 交易页三行模式第 ③ 行：恢复
        }

        // ═══ 正上子页：？帮助介绍（优先）> 选中支援详情（武备/人员两态）═══
        public void DrawSubPage(Rect rect)
        {
            // 背景绘制（Page_Trade 同款——缺失此调用则边框/底色不渲染）
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(10f);   // R-1 风格批：留间隔统一（原 4f 紧贴）

            // ？帮助：武备+人员完整介绍（wordWrap 长文）
            if (infoActive)
            {
                CelesFD_UIConfig.DrawInfoText(inner, "CelesFD_Keyed_InfoArmory".Translate());
                return;
            }

            if (selectedSupport == null)
            {
                Widgets.Label(inner, "CelesFD_Keyed_ArmoryNoSelection".Translate());
                return;
            }
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(selectedSupport.LabelCap);
            // 人员支援详情（五行价格 + 抵达模式 + 停留上限 + 构成）
            if (selectedSupport.supportType == CelesFD_SupportType.Personnel)
            {
                string fuel = "CelesFD_Keyed_CreditAmount".Translate(selectedSupport.fuelFeePerPawn);
                sb.AppendLine("CelesFD_Keyed_PersonnelFuelRow".Translate(fuel));
                string wage = "CelesFD_Keyed_CreditAmount".Translate(selectedSupport.dailyWage);
                sb.AppendLine("CelesFD_Keyed_PersonnelWageRow".Translate(wage));
                string insurance = "CelesFD_Keyed_CreditAmount".Translate(selectedSupport.insuranceCost);
                sb.AppendLine("CelesFD_Keyed_PersonnelInsuranceRow".Translate(insurance));
                string extra = FormatSupportPrice(selectedSupport);
                if (extra.Length > 0)
                    sb.AppendLine("CelesFD_Keyed_PersonnelExtraRow".Translate(extra));
                sb.AppendLine(selectedSupport.arrivalMode == CelesFD_PersonnelArrivalMode.Instant
                    ? "CelesFD_Keyed_PersonnelArriveNow".Translate()
                    : "CelesFD_Keyed_PersonnelArriveH".Translate(
                        selectedSupport.arrivalMode == CelesFD_PersonnelArrivalMode.Expedited ? 1 : 24));
                if (!selectedSupport.personnel.NullOrEmpty())
                {
                    var roster = new System.Text.StringBuilder();
                    foreach (CelesFD_PersonnelEntry e in selectedSupport.personnel)
                    {
                        if (roster.Length > 0) roster.Append("、");
                        roster.Append(e.pawnKind != null ? e.pawnKind.label : "?").Append("×").Append(e.amount);
                    }
                    sb.AppendLine("CelesFD_Keyed_PersonnelRoster".Translate(roster.ToString()));
                }
            }
            else
            {
                sb.AppendLine("CelesFD_Keyed_CreditAmount".Translate(selectedSupport.creditCost));
                if (selectedSupport.keyCost > 0) sb.AppendLine("CelesFD_Keyed_KeyAmount".Translate(selectedSupport.keyCost));
                // 审批时间（0 → 即时审批——与结算行一致）
                if (selectedSupport.applyDelayHours > 0)
                    sb.AppendLine("CelesFD_Keyed_ArmoryApplyTime".Translate(selectedSupport.applyDelayHours));
                else
                    sb.AppendLine("CelesFD_Keyed_ArmoryApplyTimeNow".Translate());
                if (selectedSupport.cooldownTicks > 0)
                    sb.AppendLine("CelesFD_Keyed_ArmoryCooldown".Translate(selectedSupport.cooldownTicks / 60));
                sb.AppendLine("CelesFD_Keyed_ArmoryOccupiesQuota".Translate(
                    selectedSupport.occupiesQuota
                        ? "CelesFD_Keyed_Yes".Translate()
                        : "CelesFD_Keyed_No".Translate()));
                if (gc != null)
                {
                    sb.AppendLine("CelesFD_Keyed_ArmoryAvailPending".Translate(
                        gc.AvailableCount(selectedSupport).ToString(), gc.PendingCount(selectedSupport).ToString()));
                }
            }
            if (!selectedSupport.description.NullOrEmpty()) sb.AppendLine(selectedSupport.description);
            Widgets.Label(inner, sb.ToString().TrimEnd('\n'));
        }

        // ═══ 价格格式化 helper（W-5 清理：3 处重复 → 1 处定义） ═══
        // "120信用额 1密钥"（为 0 不显示；全 0 返回空——Keyed 化复用 CreditAmount/KeyAmount）
        private static string FormatSupportPrice(CelesFD_SupportDef def)
        {
            string price = "";
            if (def.creditCost > 0) price = "CelesFD_Keyed_CreditAmount".Translate(def.creditCost);
            if (def.keyCost > 0)
            {
                string key = "CelesFD_Keyed_KeyAmount".Translate(def.keyCost);
                price = price.Length > 0 ? price + " " + key : key;
            }
            return price;
        }
    }
}
