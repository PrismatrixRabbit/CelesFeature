using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // 订单卡片渲染（M5 ② 重构：严格解耦——布局/文本/交互渲染独立于 Page_Trade 列表逻辑）
    // 分区（用户定稿）：主卡片左中右三区 + 最下侧横置类别色条带
    //   左区：方向小字（上）/ icon（中）/ 品名（下，超宽往复滚动；右延伸至右区左缘——锁钮/按钮均在右区，中区底部空闲）
    //   中区：数量 / 报酬（或已接取进度）
    //   右区：锁定钮（上，可选）/ 大按钮（下）
    // 变体：市场卡片（接取两态/购买 + 锁钮）与已接取卡片（进度 + 无锁钮 + 放弃按钮）
    public static class CelesFD_OrderCard
    {
        // 字体分级与布局常量（集中 CelesFD_UIConfig——解耦 2026-08-14；此处保留别名便于调用处可读）
        public const float MainSize = CelesFD_UIConfig.FontMain;
        public const float SubSize = CelesFD_UIConfig.FontSub;
        public const float DirSize = CelesFD_UIConfig.FontDir;

        public delegate void CardAction(CelesFD_Order order);

        // ═══ 市场卡片：收购=接取两态；出售=购买 ═══
        // 修复（接取 bug 根因）：仅"确认？"态才写 acceptBtnRect（失焦检测目标）——否则每张卡片无条件覆盖
        // → 最后绘制的卡片胜出 → 失焦检测位置错位 → 确认态被误判清除（"确认？按下变回接取"循环）
        public static void Draw(Rect rect, CelesFD_Order order,
            bool acceptPending, ref Rect acceptBtnRect,
            CardAction onAccept, CardAction onBuy, CardAction onToggleLock)
        {
            CelesFD_MarketClassDef def = order.TemplateDef;
            if (def == null) return;
            if (def.isBuy)
            {
                if (acceptPending)
                    DrawInternal(rect, order, showLock: true, midText: null, "CelesFD_Keyed_ConfirmAccept",
                        onAccept, onToggleLock, ref acceptBtnRect);
                else
                {
                    Rect dummy = default;
                    DrawInternal(rect, order, showLock: true, midText: null, "CelesFD_Keyed_Accept",
                        onAccept, onToggleLock, ref dummy);
                }
            }
            else
            {
                Rect dummy = default;
                DrawInternal(rect, order, showLock: true, midText: null, "CelesFD_Keyed_Buy",
                    onBuy, onToggleLock, ref dummy);
            }
        }

        // ═══ 已接取卡片变体（用户规格：进度替代数量、无锁钮、品名右延伸滚动、放弃按钮） ═══
        // 同样：仅"确认放弃？"态写 abandonBtnRect（防污染）
        public static void DrawAccepted(Rect rect, CelesFD_Order order,
            bool abandonPending, ref Rect abandonBtnRect, CardAction onAbandon)
        {
            string progress = "CelesFD_Keyed_Progress".Translate(order.amount - order.remaining, order.amount);
            if (abandonPending)
                DrawInternal(rect, order, showLock: false, midText: progress, "CelesFD_Keyed_ConfirmAbandon",
                    onAbandon, null, ref abandonBtnRect);
            else
            {
                Rect dummy = default;
                DrawInternal(rect, order, showLock: false, midText: progress, "CelesFD_Keyed_Abandon",
                    onAbandon, null, ref dummy);
            }
        }

        // ═══ 购物车卡片变体（② 卡片化：无锁钮、数量+报价、移除按钮、品名右延伸滚动） ═══
        public static void DrawCart(Rect rect, CelesFD_Order order, CardAction onRemove)
        {
            Rect dummy = default;
            DrawInternal(rect, order, showLock: false, midText: null,
                "CelesFD_Keyed_RemoveFromCart", onRemove, null, ref dummy);
        }

        private static void DrawInternal(Rect rect, CelesFD_Order order, bool showLock, string midText,
            string actionKey, CardAction onAction, CardAction onToggleLock, ref Rect actionBtnRect)
        {
            CelesFD_MarketClassDef def = order.TemplateDef;
            if (def == null) return;

            Widgets.DrawBoxSolid(rect, new Color(0.13f, 0.13f, 0.13f, 0.6f));

            // 最下侧横置色条带（类别色——CelesFD_UIConfig 集中）
            float stripH = CelesFD_UIConfig.CardStripH;
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - stripH, rect.width, stripH), CelesFD_UIConfig.CategoryColor(def.category));

            // 主卡片三区（无锁钮时右区仅按钮，略窄）
            Rect main = new Rect(rect.x, rect.y, rect.width, rect.height - stripH);
            float leftW = CelesFD_UIConfig.CardLeftW;
            float rightW = showLock ? CelesFD_UIConfig.CardRightW : CelesFD_UIConfig.CardRightWNarrow;
            Rect left = new Rect(main.x, main.y, leftW, main.height);
            Rect mid = new Rect(main.x + leftW, main.y, main.width - leftW - rightW, main.height);
            Rect right = new Rect(main.xMax - rightW, main.y, rightW, main.height);

            // 左区：方向小字（上）/ icon（中）/ 品名（下；右延伸至右区左缘）
            // 垂直布局（实测行高驱动——修复 2026-08-15：行高 = ScaledLineHeight(size) 运行时实测，
            //   废除"字号×1.4"自拟假设；方向（顶，dirH）→ icon（方向行下沿）→ 品名（底部锚定 subH）不重叠）
            float dirH = CelesFD_UIConfig.ScaledLineHeight(DirSize);
            float subH = CelesFD_UIConfig.ScaledLineHeight(SubSize);
            float mainH = CelesFD_UIConfig.ScaledLineHeight(MainSize);
            DrawScaledLabel(new Rect(left.x + 2f, left.y + 2f, left.width - 4f, dirH),
                def.isBuy ? "CelesFD_Keyed_DirBuy".Translate() : "CelesFD_Keyed_DirSell".Translate(), DirSize);
            // v2 icon 规则（用户裁决）：类别模式 = 类别内 defName 字母序首个候选物；单物 = 该物品
            ThingDef td = order.FirstThingDef;
            if (td != null)
                Widgets.ThingIcon(new Rect(left.x + 3f, left.y + 2f + dirH + 3f, 36f, 36f), td);
            float nameW = left.width + mid.width - 6f;   // 品名右延伸至右区左缘（锁钮/按钮均在右区，中区底部空闲——市场/已接取/购物车三变体统一 2026-08-15）
            DrawScrollingText(new Rect(left.x + 2f, left.yMax - subH, nameW, subH), order.ResolveLabel(), SubSize);

            // 中区：line1（进度或数量）——无锁钮时扩展到卡片右缘（按钮上方区，超宽滚动）；报酬分行（保持 mid 宽）
            //   垂直布局（实测行高驱动）：line1（mainH）→ 报酬行 ×2（subH，至多两行）——不重叠
            float curY = mid.y + 4f;
            string line1 = midText ?? "CelesFD_Keyed_OrderQty".Translate(order.amount);
            float line1W = showLock ? mid.width : mid.width + right.width;   // 无锁钮扩展到右缘（按钮 y 42 起，line1 区域不重叠）
            DrawScrollingText(new Rect(mid.x, curY, line1W, mainH), line1, MainSize);
            curY += mainH + 2f;
            // 用户裁决（纠正）：收购="报酬：" / 贩售="报价："；仅第一行带前缀，密钥行裸显示（自适应）
            //   双币种 → "报酬/报价：X 信用额" + "X 密钥"；仅信用额 → 带前缀单行；仅密钥 → 带前缀单行（密钥为第一行时）
            float credit = order.CalcPriceCredit();
            float key = order.CalcPriceKey();
            string prefixKey = def.isBuy ? "CelesFD_Keyed_Reward" : "CelesFD_Keyed_Quote";
            bool firstLine = true;
            if (credit > 0f)
            {
                string creditText = "CelesFD_Keyed_CreditAmount".Translate(credit.ToString("0.##"));
                DrawScaledLabel(new Rect(mid.x, curY, mid.width, subH),
                    firstLine ? prefixKey.Translate(creditText) : creditText, SubSize);
                curY += subH + 2f;
                firstLine = false;
            }
            if (key > 0f)
            {
                string keyText = "CelesFD_Keyed_KeyAmount".Translate(key.ToString("0.##"));
                DrawScaledLabel(new Rect(mid.x, curY, mid.width, subH),
                    firstLine ? prefixKey.Translate(keyText) : keyText, SubSize);
            }

            // 右区：锁钮（可选；高亮 = 色块背景 + 文本 + 隐形按钮）+ 大按钮
            if (showLock && onToggleLock != null)
            {
                Rect lockRect = new Rect(right.xMax - 26f, right.y + 2f, 24f, 22f);
                bool locked = order.IsLocked;
                Widgets.DrawBoxSolid(lockRect, locked ? new Color(0.9f, 0.75f, 0.2f, 0.85f) : new Color(0.22f, 0.22f, 0.22f, 0.9f));
                GUI.color = locked ? Color.white : new Color(0.75f, 0.75f, 0.75f);
                Text.Font = GameFont.Small;
                Widgets.Label(lockRect, "CelesFD_Keyed_LockBtn".Translate());
                GUI.color = Color.white;
                if (Widgets.ButtonInvisible(lockRect)) onToggleLock(order);
            }
            // 履约倒计时（M6-1：仅已接取卡 state=Accepted；剩余不足一日红色警示）——右区按钮上方（无锁钮区）
            if (order.state == CelesFD_OrderState.Accepted && order.deadlineTick >= 0)
            {
                int remain = (int)(order.deadlineTick - Find.TickManager.TicksGame);
                bool urgent = remain < GenDate.TicksPerDay;
                GUI.color = urgent ? Color.red : new Color(0.85f, 0.85f, 0.85f);
                DrawScaledLabel(new Rect(right.x + 2f, right.yMax - 32f - subH - 4f, right.width - 6f, subH),
                    "CelesFD_Keyed_OrderDeadline".Translate(Mathf.Max(0, remain).ToStringTicksToPeriod()), SubSize);
                GUI.color = Color.white;
            }
            Rect btnRect = new Rect(right.x + 2f, right.yMax - 32f, right.width - 6f, 30f);
            actionBtnRect = btnRect;
            // 视觉区分（2026-08-15 用户裁决）：贩售市场卡=购买按钮绿色强调；已接取=放弃/购物车=移除（破坏性操作）红色警示；
            //   收购=接取保持默认——防文字相近误触
            if (!def.isBuy && showLock)
                GUI.color = new Color(0.35f, 0.8f, 0.5f);
            else if (!showLock)
                GUI.color = new Color(0.9f, 0.35f, 0.3f);
            if (Widgets.ButtonText(btnRect, actionKey.Translate()))
                onAction(order);
            GUI.color = Color.white;
        }

        // ═══ 历史归档卡片（M7-4 用户规格 2026-08-15）：静态展示——三区布局同交易页、无锁钮；
        // 按钮位置 = 状态文字（已完成绿/已失败红）；右上锁定位置 = 完成时间"年.象.日"（Keyed 格式 + 原版四象翻译） ═══
        public static void DrawArchive(Rect rect, CelesFD_OrderArchiveEntry e)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.13f, 0.13f, 0.13f, 0.6f));
            float stripH = CelesFD_UIConfig.CardStripH;
            // 底部条带：成功绿 / 失败红
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - stripH, rect.width, stripH),
                e.succeeded ? new Color(0.25f, 0.6f, 0.35f) : new Color(0.75f, 0.28f, 0.22f));
            Rect main = new Rect(rect.x, rect.y, rect.width, rect.height - stripH);
            float leftW = CelesFD_UIConfig.CardLeftW;
            float rightW = CelesFD_UIConfig.CardRightWNarrow;
            Rect left = new Rect(main.x, main.y, leftW, main.height);
            Rect mid = new Rect(main.x + leftW, main.y, main.width - leftW - rightW, main.height);
            Rect right = new Rect(main.xMax - rightW, main.y, rightW, main.height);
            float subH = CelesFD_UIConfig.ScaledLineHeight(SubSize);
            float mainH = CelesFD_UIConfig.ScaledLineHeight(MainSize);
            // 左区：icon（上）+ 品名（下，超宽 ... 截断不换行）
            ThingDef td = e.thingDefName.NullOrEmpty() ? null : DefDatabase<ThingDef>.GetNamedSilentFail(e.thingDefName);
            if (td != null)
                Widgets.ThingIcon(new Rect(left.x + 3f, left.y + 4f, 36f, 36f), td);
            DrawScaledLabel(new Rect(left.x + 2f, left.yMax - subH, left.width - 4f, subH),
                TruncateText(e.thingLabel ?? e.label, left.width - 4f, SubSize), SubSize);
            // 中区：数量 + 报酬（信用额/密钥）
            float curY = mid.y + 4f;
            DrawScaledLabel(new Rect(mid.x, curY, mid.width, mainH), "CelesFD_Keyed_OrderQty".Translate(e.amount), MainSize);
            curY += mainH + 2f;
            DrawScaledLabel(new Rect(mid.x, curY, mid.width, subH),
                "CelesFD_Keyed_CreditAmount".Translate(e.creditReward.ToString()), SubSize);
            curY += subH + 2f;
            if (e.keyReward > 0)
                DrawScaledLabel(new Rect(mid.x, curY, mid.width, subH),
                    "CelesFD_Keyed_KeyAmount".Translate(e.keyReward.ToString()), SubSize);
            // 右区：右上 = 完成时间（年.象.日）；右下 = 状态文字
            DrawScaledLabel(new Rect(right.x + 2f, right.y + 4f, right.width - 4f, subH), ArchiveDateText(e), SubSize);
            GUI.color = e.succeeded ? new Color(0.4f, 0.9f, 0.5f) : new Color(0.95f, 0.35f, 0.3f);
            DrawScaledLabel(new Rect(right.x + 2f, right.yMax - subH - 4f, right.width - 4f, subH),
                (e.succeeded ? "CelesFD_Keyed_ArchiveDoneLabel" : "CelesFD_Keyed_ArchiveFailedLabel").Translate(), SubSize);
            GUI.color = Color.white;
        }

        // 完成时间"年.象.日"（原版 QuadrumUtility.Label 四象翻译——中文"翠/赫/茶/素"，QuadrumUtility.cs:60-70 实证）
        private static string ArchiveDateText(CelesFD_OrderArchiveEntry e)
        {
            if (e.absTick <= 0) return string.Empty;   // 旧档无 absTick → 不显示
            float lon = Find.CurrentMap != null
                ? Find.WorldGrid.LongLatOf(Find.CurrentMap.Tile).x : 0f;
            return "CelesFD_Keyed_ArchiveDate".Translate(
                GenDate.Year(e.absTick, lon),
                GenDate.Quadrum(e.absTick, lon).Label(),
                GenDate.DayOfQuadrum(e.absTick, lon) + 1);
        }

        // 超宽 ... 截断（不换行——用户规格 2026-08-15）
        // G23 修复（归 W-3）：二分截断替代逐字符回退——原 O(n) 次 CalcSize/帧/卡（长品名数百字符），
        //   二分 O(log n) 次；结果语义不变（最长可放下的前缀 + "..."）
        private static string TruncateText(string text, float maxWidth, float size)
        {
            GUIStyle style = CelesFD_UIConfig.GetScaledStyle(size);
            if (text == null || style.CalcSize(new GUIContent(text)).x <= maxWidth) return text;
            int lo = 1, hi = text.Length;   // [lo, hi] = 可能的前缀长度域
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (style.CalcSize(new GUIContent(text.Substring(0, mid) + "...")).x <= maxWidth)
                    lo = mid;
                else
                    hi = mid - 1;
            }
            return text.Substring(0, lo) + "...";
        }

        // ═══ 通用往复滚动文本（品名/进度/长数值共用；超宽时 PingPong 左右滚动，未超宽静态显示） ═══
        private static void DrawScrollingText(Rect rect, string text, float size)
        {
            GUIStyle style = CelesFD_UIConfig.GetScaledStyle(size);
            float textW = style.CalcSize(new GUIContent(text)).x;
            if (textW <= rect.width)
            {
                DrawScaledLabel(rect, text, size);
                return;
            }
            float maxOffset = textW - rect.width;
            float offset = Mathf.PingPong(Time.realtimeSinceStartup * CelesFD_UIConfig.NameScrollSpeed, maxOffset);
            Widgets.BeginGroup(rect);
            GUI.Label(new Rect(-offset, 0f, textW, rect.height), text, style);
            Widgets.EndGroup();
        }

        // ═══ 工具 ═══
        private static void DrawScaledLabel(Rect rect, string text, float size)
        {
            Text.Font = GameFont.Small;
            GUIStyle style = CelesFD_UIConfig.GetScaledStyle(size);
            // G22 缓存 alignment 冻结——非默认 Text.Anchor 需克隆（同 Page_Trade 修复）
            if (Text.Anchor != TextAnchor.UpperLeft)
            {
                var clone = new GUIStyle(style);
                clone.alignment = Text.Anchor;
                GUI.Label(rect, text, clone);
            }
            else
            {
                GUI.Label(rect, text, style);
            }
        }
    }
}
