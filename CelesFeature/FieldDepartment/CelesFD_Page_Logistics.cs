using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // 物流页（M7-4 用户规格 2026-08-15）：
    //   左 50%：发送中物流卡片——高度 82f（交易页 CardMinHeight 同高）、宽度 = 本区宽；
    //     上区：前 5 物品 icon 横排（第 6 位"…"）+ label（多物品","间隔、不换行、"..."截断）+ 右状态/剩余时间
    //     下区：节点进度条（左右各 10px、节点均分、时间等分：标准 6 节点×4h / 加急 4 节点×15 分钟；圈=节点抵达亮起、线=进度）
    //   右 50%：已完成历史卡片（CelesFD_OrderCard.DrawArchive 静态卡——类同交易页，右上完成时间"年.象.日"）
    //   两区均内置滚动条；文本一律 ScaledLineHeight 实测行高（规避交易页文字问题）
    [StaticConstructorOnStartup]   // 静态字段 circleTex（Texture2D 自生成）——启动主线程预初始化（原版警告修复，同 CelesFD_CompTransporter 先例）
    public class CelesFD_Page_Logistics : CelesFD_IPage, CelesFD_ISubPage
    {
        static CelesFD_Page_Logistics()
        {
            _ = CelesFD_UIConfig.CircleTex;   // 主线程预初始化（C 提取：CircleTex 已迁 UIConfig）
        }
        private Vector2 inTransitScrollPos;
        private Vector2 historyScrollPos;
        private bool infoActive;   // ？帮助（R-1 UI 改造：子页显示物流页机制介绍）


        public string Title => "CelesFD_Keyed_Tab_Logistics".Translate();

        public void Draw(Rect inRect)
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return;
            Rect left = new Rect(inRect.x, inRect.y, inRect.width * 0.5f, inRect.height);
            Rect right = new Rect(inRect.x + inRect.width * 0.5f, inRect.y, inRect.width * 0.5f, inRect.height);
            Widgets.DrawMenuSection(left);
            Widgets.DrawMenuSection(right);
            DrawInTransit(left.ContractedBy(4f), gc);
            DrawHistory(right.ContractedBy(4f), gc);
        }

        // 左：发送中物流卡片（R-1：物品单 + 人员物流单混排——人员 Transit 段入列，Instant 模式无物流段）
        private void DrawInTransit(Rect rect, CelesFD_GameComponent gc)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), "CelesFD_Keyed_LogisticsInTransit".Translate());
            Rect listRect = new Rect(rect.x, rect.y + 26f, rect.width, rect.yMax - (rect.y + 26f));
            const float cardH = 82f;   // 交易页 CardMinHeight 同高（用户裁决）
            const float cardGap = 4f;
            var personnelTransit = new List<CelesFD_PersonnelOrder>();
            foreach (CelesFD_PersonnelOrder po in gc.PersonnelOrders)
                if (po.phase == CelesFD_PersonnelOrder.Phase.Transit && po.Def != null
                    && po.Def.arrivalMode != CelesFD_PersonnelArrivalMode.Instant)
                    personnelTransit.Add(po);
            int totalCards = gc.InTransitList.Count + personnelTransit.Count;
            float contentH = Mathf.Max(listRect.height, totalCards * (cardH + cardGap));
            Widgets.BeginScrollView(listRect, ref inTransitScrollPos, new Rect(0f, 0f, listRect.width - 16f, contentH));
            for (int i = 0; i < gc.InTransitList.Count; i++)
            {
                Rect card = new Rect(0f, i * (cardH + cardGap), listRect.width - 16f, cardH);
                DrawLogisticsCard(card, gc.InTransitList[i]);
            }
            for (int i = 0; i < personnelTransit.Count; i++)
            {
                Rect card = new Rect(0f, (gc.InTransitList.Count + i) * (cardH + cardGap), listRect.width - 16f, cardH);
                DrawPersonnelTransitCard(card, personnelTransit[i]);
            }
            Widgets.EndScrollView();
        }

        // R-1：人员物流卡片（上区 = icon + 名 + 右三行[段文案（共享基类）/类型/剩余]；下区简单进度条）
        private void DrawPersonnelTransitCard(Rect rect, CelesFD_PersonnelOrder po)
        {
            long now = Find.TickManager.TicksGame;
            CelesFD_SupportDef def = po.Def;
            Widgets.DrawBoxSolid(rect, new Color(0.13f, 0.13f, 0.13f, 0.6f));
            float subH = CelesFD_UIConfig.ScaledLineHeight(CelesFD_UIConfig.FontSub);
            Rect upper = new Rect(rect.x, rect.y, rect.width, 62f);
            float iconSize = 40f;
            if (!def.uiIcon.NullOrEmpty())
            {
                Texture2D icon = ContentFinder<Texture2D>.Get(def.uiIcon, false);
                if (icon != null) GUI.DrawTexture(new Rect(upper.x + 4f, upper.y + 4f, iconSize, iconSize), icon, ScaleMode.ScaleToFit);
            }
            DrawScaledLabel(new Rect(upper.x + 4f, upper.y + 46f, upper.width - 130f, subH), def.LabelCap, CelesFD_UIConfig.FontSub);
            float rightW = 110f;
            var provider = CelesFD_TransitStageProvider.For(def.arrivalMode);
            DrawScaledLabel(new Rect(upper.xMax - rightW, upper.y + 4f, rightW - 4f, subH),
                provider.CurrentStageKey(po.startTick, now).Translate(), CelesFD_UIConfig.FontSub);
            DrawScaledLabel(new Rect(upper.xMax - rightW, upper.y + 8f + subH, rightW - 4f, subH),
                (def.arrivalMode == CelesFD_PersonnelArrivalMode.Expedited
                    ? "CelesFD_Keyed_ExpediteTag" : "CelesFD_Keyed_StandardTag").Translate(), CelesFD_UIConfig.FontSub);
            DrawScaledLabel(new Rect(upper.xMax - rightW, upper.y + 12f + subH * 2f, rightW - 4f, subH),
                "CelesFD_Keyed_LogisticsRemain".Translate(
                    Mathf.Max(0, (int)(po.transitEndTick - now)).ToStringTicksToPeriod()), CelesFD_UIConfig.FontSub);
            Rect lower = new Rect(rect.x, rect.y + 66f, rect.width, rect.height - 66f);
            // C 提取：共用 helper（物品/人员两族唯一实现）
            int nodeCount = def.arrivalMode == CelesFD_PersonnelArrivalMode.Expedited ? 4 : 6;
            CelesFD_UIConfig.DrawNodeProgressLine(lower, po.startTick, provider.TransitTicks, nodeCount);
        }

        // 物流卡片（用户规格：上区 icon 前 5 + label 截断 + 右状态/剩余；下区节点进度条等分）
        private void DrawLogisticsCard(Rect rect, CelesFD_LogisticsOrder lo)
        {
            long now = Find.TickManager.TicksGame;
            Widgets.DrawBoxSolid(rect, new Color(0.13f, 0.13f, 0.13f, 0.6f));
            float subH = CelesFD_UIConfig.ScaledLineHeight(CelesFD_UIConfig.FontSub);
            // 上区（约 62f）
            Rect upper = new Rect(rect.x, rect.y, rect.width, 62f);
            // 左：前 5 物品 icon 横排 + 第 6 位省略号
            float iconSize = 40f;
            float curX = upper.x + 4f;
            int shown = 0;
            foreach (CelesFD_LogisticsItem item in lo.items)
            {
                if (shown >= 5) break;
                ThingDef td = item.ThingDef;
                if (td != null) Widgets.ThingIcon(new Rect(curX, upper.y + 4f, iconSize, iconSize), td);
                curX += iconSize + 4f;
                shown++;
            }
            if (lo.items.Count > 5)
                DrawScaledLabel(new Rect(curX, upper.y + 4f, 20f, iconSize), "…", CelesFD_UIConfig.FontSub);
            // label：多物品 "," 间隔、不换行、"..." 截断（宽度扣除右区三行区）
            float rightW = 110f;   // 右区三行（节点文段/类型/剩余时间）
            string label = string.Join(",", lo.items.Select(it => it.ThingDef?.label ?? it.thingDefName));
            DrawScaledLabel(new Rect(upper.x + 4f, upper.y + 46f, upper.width - rightW - 12f, subH),
                TruncateText(label, upper.width - rightW - 12f), CelesFD_UIConfig.FontSub);
            // 右：三行——①节点文段（当前阶段）②类型（标准/加急）③剩余时间（用户规格 2026-08-15）
            float ry = upper.y + 4f;
            DrawScaledLabel(new Rect(upper.xMax - rightW, ry, rightW - 4f, subH), StageText(lo, now), CelesFD_UIConfig.FontSub);
            ry += subH + 2f;
            DrawScaledLabel(new Rect(upper.xMax - rightW, ry, rightW - 4f, subH),
                (lo.expedited ? "CelesFD_Keyed_ExpediteTag" : "CelesFD_Keyed_StandardTag").Translate(), CelesFD_UIConfig.FontSub);
            ry += subH + 2f;
            int remain = (int)(lo.arrivalTick - now);
            DrawScaledLabel(new Rect(upper.xMax - rightW, ry, rightW - 4f, subH),
                "CelesFD_Keyed_LogisticsRemain".Translate(Mathf.Max(0, remain).ToStringTicksToPeriod()), CelesFD_UIConfig.FontSub);
            // 下区：节点进度条（左右 10px、节点均分、时间等分——用户裁决）
            Rect lower = new Rect(rect.x, rect.y + 66f, rect.width, rect.height - 66f);
            DrawNodeProgress(lower, lo, now);
        }

        // 节点文段（用户规格：右区第一行——当前阶段）：审查 R2 修复——迁移共享基类（推进逻辑唯一实现，
        //   段键沿用物品侧既有 Keyed，行为不变）；t ≥ 总时长 = 正在运输中（物品侧终态文案，基类外保留）
        private static string StageText(CelesFD_LogisticsOrder lo, long now)
        {
            var provider = lo.expedited
                ? (CelesFD_TransitStageProvider)new CelesFD_TransitStage_ItemExpedited()
                : new CelesFD_TransitStage_ItemStandard();
            if (now - lo.startTick >= provider.TransitTicks) return "CelesFD_Keyed_StageInTransit".Translate();
            return provider.CurrentStageKey(lo.startTick, now).Translate();
        }

        // 节点进度条（C 提取：改调 UIConfig 共用 helper——DrawNodeProgressLine 唯一实现）
        private void DrawNodeProgress(Rect rect, CelesFD_LogisticsOrder lo, long now)
        {
            int nodeCount = lo.expedited ? 4 : 6;
            long totalTicks = lo.expedited ? 2500L : 60000L;
            CelesFD_UIConfig.DrawNodeProgressLine(rect, lo.startTick, totalTicks, nodeCount);
        }

        // 右：已完成历史（双列卡片网格——同交易页 CalcCardGrid；CelesFD_OrderCard.DrawArchive 静态卡，用户裁决 2026-08-15）
        private void DrawHistory(Rect rect, CelesFD_GameComponent gc)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width - 30f, 24f), "CelesFD_Keyed_LogisticsHistory".Translate());
            if (CelesFD_UIConfig.DrawHelpButton(rect.xMax - 24f, rect.y, 24f)) infoActive = !infoActive;
            Rect listRect = new Rect(rect.x, rect.y + 26f, rect.width, rect.yMax - (rect.y + 26f));
            CelesFD_UIConfig.CalcCardGrid(listRect, gc.OrderArchive.Count, out float cardW, out float cardH, out float totalH);
            float contentH = Mathf.Max(listRect.height, totalH);
            Widgets.BeginScrollView(listRect, ref historyScrollPos, new Rect(0f, 0f, listRect.width - 16f, contentH));
            for (int i = 0; i < gc.OrderArchive.Count; i++)
            {
                int col = i % 2;
                int row = i / 2;
                Rect card = new Rect(col * (cardW + CelesFD_UIConfig.CardGap), row * (cardH + CelesFD_UIConfig.CardGap), cardW, cardH);
                CelesFD_OrderCard.DrawArchive(card, gc.OrderArchive[i]);
            }
            Widgets.EndScrollView();
        }

        // 缩放文本（物流页副本——项目模式：各 UI 文件自带；行高实测规避文字问题）
        private static void DrawScaledLabel(Rect rect, string text, float size)
        {
            Text.Font = GameFont.Small;
            GUI.Label(rect, text, CelesFD_UIConfig.GetScaledStyle(size));
        }

        // 超宽 "..." 截断（不换行——用户规格）
        // G23 修复（归 W-3）：二分截断——同 OrderCard.TruncateText 改造
        private static string TruncateText(string text, float maxWidth)
        {
            GUIStyle style = CelesFD_UIConfig.GetScaledStyle(CelesFD_UIConfig.FontSub);
            if (text == null || style.CalcSize(new GUIContent(text)).x <= maxWidth) return text;
            int lo = 1, hi = text.Length;
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

        // 正上子页：物流系统状态（数据源 CelesFD_BeaconUtility.GetStations）
        public void DrawSubPage(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(10f);
            // ？帮助（R-1 UI 改造）优先：物流页机制介绍（替代常态站点状态）
            if (infoActive)
            {
                CelesFD_UIConfig.DrawInfoText(inner, "CelesFD_Keyed_InfoLogistics".Translate());
                return;
            }
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 24f), "CelesFD_Keyed_LogisticsStatus".Translate());

            List<Settlement> stations = CelesFD_BeaconUtility.GetStations();   // 站数极少，每帧查询可接受；量大时实现期缓存
            string body;
            if (stations.Count == 0)
            {
                body = "CelesFD_Keyed_LogisticsNoStation".Translate();
            }
            else
            {
                Settlement s = stations[0];
                string status = "CelesFD_Keyed_LogisticsStatusGood".Translate();
                string template = CelesFD_DefOf.CelesFD_SubPageLogistics?.logisticsTemplate;
                if (template.NullOrEmpty())
                {
                    body = "CelesFD_Keyed_LogisticsDefault".Translate(s.Label, CelesFD_BeaconUtility.FormatStationDescription(s), status);
                }
                else
                {
                    // M0 占位符替换（\n 转真实换行）；M5d 统一换 ResolveNodeText 插值引擎
                    body = template.Replace("\\n", "\n")
                                   .Replace("{station}", s.Label)
                                   .Replace("{position}", CelesFD_BeaconUtility.FormatStationDescription(s))
                                   .Replace("{status}", status);
                }
            }

            Rect textRect = new Rect(inner.x, inner.y + 26f, inner.width, inner.height - 26f);
            bool prevWrap = Text.WordWrap;
            Text.WordWrap = true;
            GUI.Label(textRect, body, Text.CurFontStyle);
            Text.WordWrap = prevWrap;
        }

        public void Notify_Deactivated() { infoActive = false; }   // R-1 风格批：切页重置 ？态
    }
}
