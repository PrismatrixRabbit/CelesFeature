using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    public class CelesFD_Dialog_Comms : Window
    {
        private const float RegionSpacing = 6f;
        private const float OverviewLineHeight = 24f;
        private const float MainPageTopGap = 20f;
        private const float TabButtonHeight = 32f;
        private const float TabButtonGap = 4f;

        private readonly List<CelesFD_IPage> pages = new List<CelesFD_IPage>();
        private int curTabIndex;
        private Vector2 tabScrollPos;

        public override Vector2 InitialSize
        {
            get
            {
                float width = 1000f;
                float height = Mathf.Min(800f, UI.screenHeight - 50f);
                return new Vector2(width, height);
            }
        }

        public CelesFD_Dialog_Comms()
        {
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            closeOnCancel = true;
            closeOnClickedOutside = false;
            doCloseX = true;
            doWindowBackground = true;
            soundAppear = SoundDefOf.CommsWindow_Open;
            soundClose = SoundDefOf.CommsWindow_Close;
            soundAmbient = SoundDefOf.RadioComms_Ambience;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (pages.Count == 0) InitPages();

            float w = inRect.width, h = inRect.height;
            Rect overviewRect = new Rect(0f, 0f, w * 0.3f, h * 0.3f);       // ① 左上概况
            Rect topRect      = new Rect(w * 0.3f, 0f, w * 0.5f, h * 0.3f);  // ② 正上（空子页）
            Rect tabRect      = new Rect(w * 0.8f, 0f, w * 0.2f, h * 0.3f);  // ③ 右上切换钮（30%H）
            Rect mainRect     = new Rect(0f, h * 0.3f, w, h * 0.7f);         // ⑤ 主页面（100%W）

            DrawOverviewPanel(overviewRect.ContractedBy(RegionSpacing));
            DrawEmptySubPage(topRect.ContractedBy(RegionSpacing));
            DrawTabButtons(tabRect.ContractedBy(RegionSpacing));

            Rect mainContentRect = mainRect.ContractedBy(RegionSpacing);
            mainContentRect.yMin += MainPageTopGap;    // 顶部 20px 间距
            Widgets.DrawMenuSection(mainContentRect);  // 统一主页面框
            pages[curTabIndex].Draw(mainContentRect);  // 页面内容
        }

        private void InitPages()
        {
            pages.Add(new CelesFD_Page_Welcome());
            pages.Add(new CelesFD_Page_Trade());
            pages.Add(new CelesFD_Page_Armory());
            pages.Add(new CelesFD_Page_Quest());
            pages.Add(new CelesFD_Page_Logistics());
        }

        private void DrawOverviewPanel(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return;

            float curY = rect.y + 8f;
            Widgets.Label(new Rect(rect.x + 8f, curY, rect.width - 16f, OverviewLineHeight), "CelesFD_Keyed_Level".Translate(gc.UnlockLevelValue));
            curY += OverviewLineHeight + 2f;
            Widgets.Label(new Rect(rect.x + 8f, curY, rect.width - 16f, OverviewLineHeight), "CelesFD_Keyed_Fame".Translate(gc.Fame));
            curY += OverviewLineHeight + 2f;
            Widgets.Label(new Rect(rect.x + 8f, curY, rect.width - 16f, OverviewLineHeight), "CelesFD_Keyed_Credit".Translate(gc.Credit));
            curY += OverviewLineHeight + 2f;
            Widgets.Label(new Rect(rect.x + 8f, curY, rect.width - 16f, OverviewLineHeight), "CelesFD_Keyed_QuantumKey".Translate(gc.QuantumKey));
            curY += OverviewLineHeight + 2f;
            Widgets.Label(new Rect(rect.x + 8f, curY, rect.width - 16f, OverviewLineHeight), "CelesFD_Keyed_TradeVolume".Translate(gc.TradeVolume));
        }

        private void DrawEmptySubPage(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
        }

        private void DrawTabButtons(Rect rect)
        {
            Widgets.DrawMenuSection(rect);

            Rect contentRect = rect.ContractedBy(4f);
            float contentHeight = pages.Count * (TabButtonHeight + TabButtonGap);
            Widgets.BeginScrollView(contentRect, ref tabScrollPos, new Rect(0f, 0f, contentRect.width - 16f, contentHeight));
            float curY = 0f;
            Text.Font = GameFont.Small;
            for (int i = 0; i < pages.Count; i++)
            {
                // 按钮宽度 = 文本宽 + 80f padding，不超过内容区
                float btnWidth = Mathf.Min(Text.CalcSize(pages[i].Title).x + 130f, contentRect.width);
                // 水平居中（坐标是内容区相对值）
                Rect btnRect = new Rect((contentRect.width - btnWidth) / 2f, curY, btnWidth, TabButtonHeight);
                if (Widgets.ButtonText(btnRect, pages[i].Title))
                {
                    if (curTabIndex != i)
                    {
                        pages[curTabIndex].Notify_Deactivated();
                        curTabIndex = i;
                    }
                }
                curY += TabButtonHeight + TabButtonGap;
            }
            Widgets.EndScrollView();
        }
    }
}