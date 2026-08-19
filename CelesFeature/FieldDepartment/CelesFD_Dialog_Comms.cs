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
        private Vector2 overviewScrollPos;   // ② 重构：左上概览区滚动（动态内容高度）

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
            if (pages[curTabIndex] is CelesFD_ISubPage sp)
                sp.DrawSubPage(topRect.ContractedBy(RegionSpacing));
            else
                DrawEmptySubPage(topRect.ContractedBy(RegionSpacing));   // 武备/任务页回退（零改动）
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

            // ② 重构：概览区滚动（内容坐标；距下一级行动态高度——超高触发滚动条防遮挡）
            float contentH = 5 * (OverviewLineHeight + 2f) + 48f + 8f;
            Widgets.BeginScrollView(rect.ContractedBy(4f), ref overviewScrollPos, new Rect(0f, 0f, rect.width - 24f, contentH));
            float curY = 8f;
            float textW = rect.width - 32f;
            Widgets.Label(new Rect(8f, curY, textW, OverviewLineHeight), "CelesFD_Keyed_Level".Translate(gc.GetEffectiveLevel()));   // 显示等级 = 有效等级（v4.7）
            curY += OverviewLineHeight + 2f;
            Widgets.Label(new Rect(8f, curY, textW, OverviewLineHeight), "CelesFD_Keyed_Fame".Translate(gc.Fame));
            curY += OverviewLineHeight + 2f;
            Widgets.Label(new Rect(8f, curY, textW, OverviewLineHeight), "CelesFD_Keyed_Credit".Translate(gc.Credit));
            curY += OverviewLineHeight + 2f;
            Widgets.Label(new Rect(8f, curY, textW, OverviewLineHeight), "CelesFD_Keyed_QuantumKey".Translate(gc.QuantumKey));
            curY += OverviewLineHeight + 2f;
            Widgets.Label(new Rect(8f, curY, textW, OverviewLineHeight), "CelesFD_Keyed_TradeVolume".Translate(gc.TradeVolume));
            curY += OverviewLineHeight + 2f;
            DrawNextLevelLine(curY, textW);
            Widgets.EndScrollView();
        }

        // M2（v4.7）：距下一级所需声望/交易额；欠款（Credit<0）替换"请还款"；最高级显示已达最高
        // ② 重构：内容坐标（概览滚动区内）；WordWrap + 动态高度换行适配
        private void DrawNextLevelLine(float curY, float width)
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return;
            string text;
            if (gc.Credit < 0)
            {
                text = "CelesFD_Keyed_NextLevelDebt".Translate();
            }
            else
            {
                CelesFD_UnlockLevelConfigDef config = CelesFD_DefOf.CelesFD_UnlockLevelConfigDefault;
                int nextIndex = gc.UnlockLevelValue + 1;
                if (config == null || config.unlockLevel == null || nextIndex >= config.unlockLevel.Count)
                {
                    text = "CelesFD_Keyed_NextLevelMax".Translate();
                }
                else
                {
                    CelesFD_UnlockLevelDef next = DefDatabase<CelesFD_UnlockLevelDef>.GetNamedSilentFail(config.unlockLevel[nextIndex]);
                    if (next == null)
                    {
                        text = "CelesFD_Keyed_NextLevelMax".Translate();
                    }
                    else
                    {
                        int fameNeed = Mathf.Max(0, next.fameRequire - gc.Fame);
                        int tradeNeed = Mathf.Max(0, next.tradeRequire - gc.TradeVolume);
                        text = "CelesFD_Keyed_NextLevel".Translate(fameNeed, tradeNeed);
                    }
                }
            }
            bool prevWrap = Text.WordWrap;
            Text.WordWrap = true;
            float h = Text.CalcHeight(text, width);
            Widgets.Label(new Rect(8f, curY, width, h), text);
            Text.WordWrap = prevWrap;
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