using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    public class CelesFD_Page_Welcome : CelesFD_IPage
    {
        private const float StartupDuration = 9f;
        private const int ProgressBarCells = 18;
        private const float CharInterval = 0.01f;
        private const float TickerHeight = 40f;
        private const float DialogueFraction = 0.4f;
        private const float TickerGap = 3f;
        private static readonly Color StartupColor = new Color(0f, 0.667f, 0f, 1f); // #00AA00

        private static readonly string PortraitAscii = @"
  .*%#+-:.  :-+**+-:..                  
    =@@@@@%%%%%+:                       
     .#@@@@@@@@@@%#+=:.                 
    :#@%%@@@@@@@@@@@@@@%%#+=:.          
   :%@@@%#@@@@@@@@@@#*##***#**=         
  .%@@@%%%:+@@@@@%#*++-.                
  =@%@%%%%. :#@@@%+*-.-=-.              
  =@%@%%%%+   -#@@%--   -=+:            
  .@@@@%%%%=    =%@%.     :=+:          
   =@@@@%%%%*.    =%#       :+=         
    -%@@@%%%%%*:    .         -+        
     .+%@@@%%%%%#*=:.          =*:      
       .=#%@@@@%%%%%###*****##*===      
          .:=*#%%%%%%%%%%##**=..==      
                ..::::..      .:.       "; // 原文不变（Tiny 字号）
        private static readonly string BootLog = @"
MAGPIE_OS Rev 1.3 BOOT SEQUENCE ... OK
CPU @ 4MHZ
TESTING 0x0000 - 0x3FFF ... OK
CMOS ... 99%
RTC CPU TIME SYNC ... OK
IRQ0 - IRQ8 ... OK

-RUN COMM_CORE.MOS ... OK
-RUN NETSTACK.MOS ... OK
-RUN FREQ_HOPPER.MOS ... OK

ATTEMPTING LINK TO ""AURORA"" ... FAIL
ATTEMPTING LINK TO CELESTIA SATELLITE PROTOCOL ... FAIL
CONNECTING TO LOCAL NETWORK PROTOCOL ... OK
WARNING: CHANNEL UNSTABLE, SIGNAL STRENGTH WEAK
RELAY: UNAUTHORIZED NODE
SYSTEM NOT ACTIVATED

NETWORK TIME SYNC ... OK
LOCAL TIME: 13:22, 04 JUGUSTO, 5502

LOCAL KEY VERIFICATION
-PINGING LOCAL NETWORK CREDENTIALS...
CLEARANCE LEVEL: GUEST

LOADING COMMUNICATION PROTOCOLS:
-LOAD PACKET_HANDLER.BIN... OK
-LOAD ENCRYPT_BOOTES.DRV... KEY EXPIRED
-WARN: USING FALLBACK XOR CIPHER. SECURITY LOW.

BANDWIDTH BUDGET: 300 BYTES (LIMITED BY RELAY CACHE)
CHANNEL OPEN. READY FOR UPLINK.";        // 原文不变

        private class TickerEntry
        {
            public string Text;
            public float Width;
            public float EnterTime;
        }

        private bool startupDone;
        private bool startupInterrupted;
        private float startupStartTime;
        private Texture2D portraitTex;
        private Vector2 dialogueScrollPos;
        private Vector2 bootTextScrollPos;
        private bool bootTextPinnedToBottom = true;
        private bool dialoguePinnedToBottom = true;

        private CelesFD_TickerDef tickerDef;
        private readonly List<TickerEntry> tickerEntries = new List<TickerEntry>();
        private int lastTicker1 = -1;
        private int lastTicker2 = -1;
        private float nextAppendTime;

        public string Title => "CelesFD_Keyed_Tab_Welcome".Translate();

        public CelesFD_Page_Welcome()
        {
            startupStartTime = Time.realtimeSinceStartup;
        }

        public void Notify_Deactivated()
        {
            if (!startupDone) startupInterrupted = true;
        }

        public void Draw(Rect inRect)
        {
            float now = Time.realtimeSinceStartup;
            if (!startupDone && startupInterrupted)
            {
                startupDone = true;
                StartTicker(now);
            }

            Rect tickerBarRect = inRect.BottomPartPixels(TickerHeight);
            Rect restRect = inRect; restRect.yMax -= TickerHeight;
            float dialogueHeight = restRect.height * DialogueFraction;
            Rect dialogueRect = restRect.BottomPartPixels(dialogueHeight);
            Rect topRect = restRect; topRect.yMax -= dialogueHeight;
            Rect portraitRect = topRect.LeftPart(0.4f);
            Rect textRect = topRect.RightPart(0.6f);

            Widgets.DrawMenuSection(dialogueRect);

            if (!startupDone)
            {
                float elapsed = now - startupStartTime;
                DrawPortraitStartup(portraitRect, elapsed);
                DrawBootText(textRect, elapsed);
                if (elapsed >= StartupDuration)
                {
                    startupDone = true;
                    CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
                    if (gc != null && gc.DialogueHistory.Count == 0)
                        gc.AddDialogue("CelesFD_Keyed_Greeting_Placeholder".Translate());
                    StartTicker(now);
                }
            }
            else
            {
                DrawPortrait(portraitRect);
                DrawDialogue(textRect);
            }

            DrawTickerBar(tickerBarRect, now);
        }

        private void DrawPortraitStartup(Rect rect, float elapsed)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(10f);
            Rect asciiRect = inner;
            asciiRect.yMax -= 26f;
            GUI.color = StartupColor;
            GUI.Label(asciiRect, PortraitAscii, GetScaledDownStyle(0.8f)); //Ascii字号
            int filled = Mathf.Clamp(Mathf.CeilToInt(elapsed / StartupDuration * ProgressBarCells), 0, ProgressBarCells);
            string bar = "";
            for (int i = 0; i < ProgressBarCells; i++)
                bar += (i < filled) ? "█" : "░";
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inner.x, inner.yMax - 20f, inner.width, 20f), bar);
            GUI.color = Color.white;
        }

        private void DrawBootText(Rect rect, float elapsed)
        {
            Widgets.DrawMenuSection(rect);
            int charCount = Mathf.Min((int)(elapsed / CharInterval), BootLog.Length);
            string shown = BootLog.Substring(0, charCount);

            Rect scrollRect = rect.ContractedBy(10f);
            GUIStyle style = GetScaledDownStyle(0.9f); //启动日志字号
            float contentHeight = style.CalcHeight(new GUIContent(shown), scrollRect.width - 24f);

            if (bootTextPinnedToBottom)   // 贴底时自动滚到底
                bootTextScrollPos.y = Mathf.Max(0f, contentHeight - scrollRect.height);

            Widgets.BeginScrollView(scrollRect, ref bootTextScrollPos,
                new Rect(0f, 0f, scrollRect.width - 16f, contentHeight), showScrollbars: true);
            GUI.color = StartupColor;
            GUI.Label(new Rect(0f, 0f, scrollRect.width - 24f, contentHeight), shown, style);
            Widgets.EndScrollView();
            GUI.color = Color.white;

            // 基于滚动位置判断（Unity 滚轮已在 BeginScrollView 内更新 scrollPos）
            if (bootTextScrollPos.y < contentHeight - scrollRect.height - 2f)
                bootTextPinnedToBottom = false;
            else
                bootTextPinnedToBottom = true;
        }

        private void DrawPortrait(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            if (portraitTex == null)
                portraitTex = ContentFinder<Texture2D>.Get("Celes/UI/CelesFD_UI/CelesFD_portrait0");
            Rect inner = rect.ContractedBy(10f);
            float size = Mathf.Min(inner.width, inner.height);
            Rect drawRect = new Rect(inner.x, inner.y, size, size);   // 靠左显示
            Widgets.DrawTextureFitted(drawRect, portraitTex, 1f);
        }

        private void DrawDialogue(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return;

            Text.Font = GameFont.Small;
            float lineHeight = Text.LineHeight + 2f;
            float contentHeight = gc.DialogueHistory.Count * lineHeight;
            Rect scrollRect = rect.ContractedBy(4f);
            float viewHeight = scrollRect.height;

            if (dialoguePinnedToBottom)
                dialogueScrollPos.y = Mathf.Max(0f, contentHeight - viewHeight);

            Widgets.BeginScrollView(scrollRect, ref dialogueScrollPos,
                new Rect(0f, 0f, scrollRect.width - 16f, contentHeight), showScrollbars: true);
            float curY = 0f;
            for (int i = 0; i < gc.DialogueHistory.Count; i++)
            {
                Widgets.Label(new Rect(4f, curY, scrollRect.width - 24f, lineHeight), "> " + gc.DialogueHistory[i]);
                curY += lineHeight;
            }
            Widgets.EndScrollView();

            if (dialogueScrollPos.y < contentHeight - viewHeight - 2f)
                dialoguePinnedToBottom = false;
            else
                dialoguePinnedToBottom = true;
        }

        private void DrawTickerBar(Rect rect, float now)
        {
            Widgets.DrawMenuSection(rect);
            if (tickerDef == null) return;
            UpdateTickerFlow(rect, now);
        }

        private void UpdateTickerFlow(Rect rect, float now)
        {
            float speed = rect.width / 10f;

            if (tickerEntries.Count == 0 || now >= nextAppendTime)
                AppendTicker(rect, now, speed);

            Widgets.BeginGroup(rect);
            Text.Font = GameFont.Small;
            foreach (TickerEntry e in tickerEntries)
            {
                float d = speed * (now - e.EnterTime);
                float leftEdge = rect.width - d;
                float rightEdge = leftEdge + e.Width;
                if (rightEdge < 0f || leftEdge > rect.width) continue;
                Widgets.Label(new Rect(leftEdge, 2f, e.Width, rect.height - 4f), e.Text);
            }
            Widgets.EndGroup();

            tickerEntries.RemoveAll(e => speed * (now - e.EnterTime) > rect.width + e.Width);
        }

        private void AppendTicker(Rect rect, float now, float speed)
        {
            int idx = PickRandomTicker();
            string text = tickerDef.welcomeTicker[idx].Translate();
            Text.Font = GameFont.Small;
            float width = Text.CalcSize(text).x;
            tickerEntries.Add(new TickerEntry { Text = text, Width = width, EnterTime = now });
            nextAppendTime = now + width / speed + TickerGap;
            lastTicker2 = lastTicker1;
            lastTicker1 = idx;
        }

        private void StartTicker(float now)
        {
            tickerDef = DefDatabase<CelesFD_TickerDef>.GetNamed("CelesFD_TickerDefault");
            tickerEntries.Clear();
            lastTicker1 = -1;
            lastTicker2 = -1;
            nextAppendTime = now;
        }

        private int PickRandomTicker()
        {
            int count = tickerDef.welcomeTicker.Count;
            if (count <= 0) return 0;
            if (count <= 2) return Rand.RangeInclusive(0, count - 1);
            int idx;
            do { idx = Rand.RangeInclusive(0, count - 1); }
            while (idx == lastTicker1 || idx == lastTicker2);
            return idx;
        }

        private static GUIStyle GetScaledDownStyle(float scale)
        {
            GUIStyle style = new GUIStyle(Text.CurFontStyle);
            style.fontSize = Mathf.Max(4, Mathf.FloorToInt(13f * scale));   // 13 = Small 基准字号（CurFontStyle.fontSize 为 0 不可用）
            return style;
        }
    }
}