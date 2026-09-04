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
            _ = CircleTex;   // 主线程预初始化圆纹理（防 UI 首次绘制时才创建）
        }
        private Vector2 inTransitScrollPos;
        private Vector2 historyScrollPos;

        // 节点"圈"纹理（UI 层无现成圆点——自生成白色圆，静态缓存）
        private static Texture2D circleTex;
        private static Texture2D CircleTex
        {
            get
            {
                if (circleTex == null)
                {
                    const int size = 16;
                    circleTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                    float r = size / 2f - 0.5f;
                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size; x++)
                        {
                            float dx = x - r, dy = y - r;
                            circleTex.SetPixel(x, y, dx * dx + dy * dy <= r * r ? Color.white : Color.clear);
                        }
                    circleTex.Apply();
                }
                return circleTex;
            }
        }

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

        // 左：发送中物流卡片
        private void DrawInTransit(Rect rect, CelesFD_GameComponent gc)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), "CelesFD_Keyed_LogisticsInTransit".Translate());
            Rect listRect = new Rect(rect.x, rect.y + 26f, rect.width, rect.yMax - (rect.y + 26f));
            const float cardH = 82f;   // 交易页 CardMinHeight 同高（用户裁决）
            const float cardGap = 4f;
            float contentH = Mathf.Max(listRect.height, gc.InTransitList.Count * (cardH + cardGap));
            Widgets.BeginScrollView(listRect, ref inTransitScrollPos, new Rect(0f, 0f, listRect.width - 16f, contentH));
            for (int i = 0; i < gc.InTransitList.Count; i++)
            {
                Rect card = new Rect(0f, i * (cardH + cardGap), listRect.width - 16f, cardH);
                DrawLogisticsCard(card, gc.InTransitList[i]);
            }
            Widgets.EndScrollView();
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
            Rect lower = new Rect(rect.x, rect.y + 66f, rect.width, rect.yMax - 66f);
            DrawNodeProgress(lower, lo, now);
        }

        // 节点文段（用户规格：右区第一行——当前阶段；与 DrawNodeProgress 同"节点数-1 段等分"：
        //   标准 5 段×4.8h / 加急 3 段×20 分钟；t ≥ 总时长 = 正在运输中）
        private static string StageText(CelesFD_LogisticsOrder lo, long now)
        {
            float t = now - lo.startTick;
            float total = lo.expedited ? 2500f : 60000f;
            if (t >= total) return "CelesFD_Keyed_StageInTransit".Translate();
            int segCount = lo.expedited ? 3 : 5;
            float seg = total / segCount;
            int idx = Mathf.Clamp((int)(t / seg), 0, segCount - 1);
            if (lo.expedited)
            {
                switch (idx)
                {
                    case 0: return "CelesFD_Keyed_StageSkipPaperwork".Translate();
                    case 1: return "CelesFD_Keyed_StageTossCargo".Translate();
                    default: return "CelesFD_Keyed_StageBypassLaunch".Translate();
                }
            }
            switch (idx)
            {
                case 0: return "CelesFD_Keyed_StageConfirming".Translate();
                case 1: return "CelesFD_Keyed_StageWarehouse".Translate();
                case 2: return "CelesFD_Keyed_StageFueling".Translate();
                case 3: return "CelesFD_Keyed_StageLoading".Translate();
                default: return "CelesFD_Keyed_StageLaunchQueue".Translate();
            }
        }

        // 节点进度条：○——○——○ 圈+线；时间等分（标准 5 段×4.8h / 加急 3 段×20 分钟——节点数-1 段）；抵达节点亮起
        private void DrawNodeProgress(Rect rect, CelesFD_LogisticsOrder lo, long now)
        {
            int nodeCount = lo.expedited ? 4 : 6;
            float totalTicks = lo.expedited ? 2500f : 60000f;
            float t = now - lo.startTick;
            float margin = 10f;
            float lineY = rect.y + rect.height / 2f;
            float x0 = rect.x + margin;
            float x1 = rect.xMax - margin;
            float nodeGap = (x1 - x0) / (nodeCount - 1);
            float nodeSize = 8f;
            // 背景线
            Widgets.DrawLineHorizontal(x0, lineY, x1 - x0, new Color(0.35f, 0.35f, 0.35f));
            // 已抵达段：亮线（节点均分 → 每段时长 = totalTicks / (nodeCount - 1)）
            float segTicks = totalTicks / (nodeCount - 1);
            int litSegs = Mathf.Clamp((int)(t / segTicks), 0, nodeCount - 1);
            if (litSegs > 0)
                Widgets.DrawLineHorizontal(x0, lineY, litSegs * nodeGap, new Color(0.45f, 0.75f, 0.95f));
            // 节点圈（抵达亮起）
            for (int i = 0; i < nodeCount; i++)
            {
                float nodeX = x0 + i * nodeGap;
                bool lit = t >= i * segTicks - 1f;   // -1 容差
                GUI.color = lit ? new Color(0.45f, 0.75f, 0.95f) : new Color(0.4f, 0.4f, 0.4f);
                GUI.DrawTexture(new Rect(nodeX - nodeSize / 2f, lineY - nodeSize / 2f, nodeSize, nodeSize), CircleTex);
                GUI.color = Color.white;
            }
        }

        // 右：已完成历史（双列卡片网格——同交易页 CalcCardGrid；CelesFD_OrderCard.DrawArchive 静态卡，用户裁决 2026-08-15）
        private void DrawHistory(Rect rect, CelesFD_GameComponent gc)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), "CelesFD_Keyed_LogisticsHistory".Translate());
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

        public void Notify_Deactivated() { }
    }
}
