using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    public class CelesFD_Page_Welcome : CelesFD_IPage
    {
        private const float StartupDuration = 9f;
        private const int ProgressBarCells = 18;
        private const float CharInterval = 0.01f;
        private const float ReplyCharInterval = 0.03f;   // 系统输出逐字间隔（启动日志保持 0.01）
        private const float TickerHeight = 40f;
        private const float DialogueFraction = 0.4f;
        private const float TickerGap = 3f;
        private const float PrepDuration = 1f;   // 预备段：_ 闪烁两次（0.5s × 2）
        
        private enum DialogueState { AwaitingChoice, ReplyTyping }
        private DialogueState dialogueState = DialogueState.AwaitingChoice;
        private string replyText;
        private int replyShownChars;
        private float replyCharAccum;
        private float lastReplyTick;
        private float replyStartTime;
        private static readonly Color RewardColor = new Color(1f, 0.84f, 0f, 1f);   // Reward 选项金色
        
        private static readonly Color StartupColor = new Color(0f, 0.667f, 0f, 1f); // #00AA00

        private static readonly string PortraitAscii = @"
  *#-.     .-**=-.              
  =@@%+: :*%@#:                 
   *@@@@##%%=                   
   .%@@@@@%#-                   
    :@@@@@@@@%+:                
     #@@@@@@@@@@#=.             
    *%%@@@@@@@@@@@%*-           
   :@@#@@@@@@@@@@@@@@%+:        
   *@@%#@@@@@@@@#%%%@@@@=       
  :%@@%#%@@@@@@@+**=:-==:       
  =@@%%%+@@@@@@#*=-:            
  *@@%%%.+@@@%*+*=              
  #@@%%#. %@@@**:=*.            
  #@@%%%. :@@@#*- =*.           
  %@@%%%-  +@@%==  =*:          
  #@@%%%+   #@@=:   =*.         
  #@@%%%#   .%@#     ++         
  +@@@%%%-   -@@:     *=        
  -@@@%%%#    +@+     .*:       
   %@@%%%%+    *%      -+       
   +@@@%%%%=    -       +:      
   .%@@%%%%%=           -+      
    -@@@%%%%%*.          +:     
     +@@@%%%%%%+:        =+     
      +@@@%%%%%%%#+====+##=     
       =@@@%%%%%%%%%%%%@*.=     
        :*@@@@%%%%%%%%%=  =.    
          :*#%@@@@@%*=-=:==     
             :-==-:.   .--.     
                                "; // 原文不变（Tiny 字号）
        private static readonly string BootLogPart1 = @"
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
";

        private static readonly string BootLogPart2 = @"
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
        private string bootLogFull;
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
            BuildBootLog();
        }

        private void BuildBootLog()
        {
            if (Find.CurrentMap == null)
            {
                bootLogFull = BootLogPart1 + "LOCAL TIME: --:--\nCOORDINATES: --\n" + BootLogPart2;
                return;
            }

            PlanetTile tile = Find.CurrentMap.Tile;
            Vector2 longLat = Find.WorldGrid.LongLatOf(tile);
            float lon = longLat.x;
            float lat = longLat.y;

            long absTick = Find.TickManager.TicksAbs;
            int hour = GenDate.HourOfDay(absTick, lon);
            int minute = (int)((GenDate.DayTick(absTick, lon) % 2500f) / 2500f * 60f);
            int day = GenDate.DayOfQuadrum(absTick, lon) + 1;   // DayOfQuadrum 返回 0-based，需 +1
            string quadrum = GenDate.Quadrum(absTick, lon).ToString().ToUpper();
            int year = GenDate.Year(absTick, lon);

            string timeLine = $"LOCAL TIME: {hour:00}:{minute:00}, {day:00} {quadrum}, {year}";
            string coordLine = $"COORDINATES: {Mathf.Abs(lat):0.#}°{(lat >= 0 ? "N" : "S")} {Mathf.Abs(lon):0.#}°{(lon >= 0 ? "E" : "W")}";

            bootLogFull = BootLogPart1 + timeLine + "\n" + coordLine + "\n" + BootLogPart2;
        }

        public void Notify_Deactivated()
        {
            if (!startupDone) startupInterrupted = true;
        }

        // 启动收尾（自然完成 / 切页打断两处触发共用此流程）：进入根树对话 + 启动跑马灯。
        // interrupted=true：被切页打断，不插入 Greeting 占位消息；
        // interrupted=false：自然完成，历史为空时插入启动第一条占位消息（CelesFD_Keyed_Greeting_Placeholder）。
        private void FinishStartup(bool interrupted)
        {
            startupDone = true;
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return;
            if (!interrupted && gc.DialogueHistory.Count == 0)
                gc.AddDialogue(false, "CelesFD_Keyed_Greeting_Placeholder".Translate());
            gc.DialogueEngine.StartRootTree();
            StartTypingNodeText(gc.DialogueEngine);
            StartTicker(Time.realtimeSinceStartup);
        }

        public void Draw(Rect inRect)
        {
            float now = Time.realtimeSinceStartup;
            if (!startupDone && startupInterrupted)
                FinishStartup(true);   // 切页打断启动：直接进入对话（不插入占位消息）

            Rect tickerBarRect = inRect.BottomPartPixels(TickerHeight);
            Rect restRect = inRect; restRect.yMax -= TickerHeight;
            float dialogueHeight = restRect.height * DialogueFraction;
            Rect dialogueRect = restRect.BottomPartPixels(dialogueHeight);
            Rect topRect = restRect; topRect.yMax -= dialogueHeight;
            Rect portraitRect = new Rect(topRect.x, topRect.y, topRect.height, topRect.height);   // 立绘正方形（宽=高）
            Rect textRect = topRect;
            textRect.xMin = portraitRect.xMax;   // 对话区从立绘右缘开始，增宽

            Widgets.DrawMenuSection(dialogueRect);

            if (!startupDone)
            {
                float elapsed = now - startupStartTime;
                DrawPortraitStartup(portraitRect, elapsed);
                DrawBootText(textRect, elapsed);
                if (elapsed >= StartupDuration)
                    FinishStartup(false);   // 自然完成：插入占位消息后进入对话
            }
            else
            {
                DrawPortrait(portraitRect);
                UpdateDialogue(now, inRect);
                DrawDialogue(textRect, now);
                DrawDialogueArea(dialogueRect, now);
            }

            DrawTickerBar(tickerBarRect, now);
        }
        
        private void UpdateDialogue(float now, Rect inRect)
        {
            if (dialogueState != DialogueState.ReplyTyping) return;

            float prepElapsed = now - replyStartTime;
            bool prepDone = prepElapsed >= PrepDuration;

            if (prepDone)
            {
                replyCharAccum += (now - lastReplyTick) / ReplyCharInterval;
                replyShownChars = Mathf.Min((int)replyCharAccum, replyText.Length);
            }
            lastReplyTick = now;   // 预备段与逐字段都更新，避免转段突跳

            if (Event.current.type == EventType.MouseDown && Mouse.IsOver(inRect))
            {
                if (!prepDone)
                {
                    replyStartTime = now - PrepDuration;   // 跳过预备 → 直接逐字
                    replyCharAccum = 0f;
                    lastReplyTick = now;
                }
                else
                {
                    int idx = replyText.IndexOf('\n', replyShownChars);
                    replyShownChars = (idx == -1) ? replyText.Length : idx + 1;
                    replyCharAccum = replyShownChars;   // 同步累加器，防下帧回退
                }
            }

            if (replyShownChars >= replyText.Length)
            {
                CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
                if (gc != null) gc.AddDialogue(false, replyText);
                replyText = null;
                dialogueState = DialogueState.AwaitingChoice;
            }
        }
        
        private void DrawDialogueArea(Rect rect, float now)
        {
            // 背景框由 Draw() 统一绘制（启动/完成阶段一致），此处不再重复画框
            if (dialogueState != DialogueState.AwaitingChoice) return;

            CelesFD_DialogueEngine engine = CelesFD_GameComponent.Instance?.DialogueEngine;
            if (engine?.CurrentNode == null) return;

            var opts = engine.GetVisibleOptions();
            float curY = rect.y + 10f;
            bool prevWrap = Text.WordWrap;
            Text.WordWrap = true;
            foreach (var o in opts)
            {
                float h = Text.CalcHeight(o.Label, rect.width - 20f);
                Rect optRect = new Rect(rect.x + 10f, curY, rect.width - 20f, h);
                Color baseC = o.IsReward ? RewardColor : Widgets.NormalOptionColor;
                Color c = Mouse.IsOver(optRect) ? Color.yellow : baseC;   // 悬停变黄
                GUI.color = c;
                GUI.Label(optRect, o.Label, Text.CurFontStyle);   // 多行渲染（换行）
                GUI.color = Color.white;
                if (Widgets.ButtonInvisible(optRect))
                {
                    OnOptionPicked(engine, o);
                    break;
                }
                curY += h + 4f;
            }
            Text.WordWrap = prevWrap;   // 恢复原值，避免帧末 WordWrap=false 报错

            if (engine.CurrentTree == null || !engine.CurrentTree.isRoot)   // 非 root 树才显示返回
            {
                Rect backRect = new Rect(rect.x + 10f, curY + 4f, rect.width - 20f, 30f);
                Color backC = Mouse.IsOver(backRect) ? Color.yellow : Widgets.NormalOptionColor;
                GUI.color = backC;
                GUI.Label(backRect, "CelesFD_Keyed_ReturnToRoot".Translate(), Text.CurFontStyle);
                GUI.color = Color.white;
                if (Widgets.ButtonInvisible(backRect)) OnReturnToRoot();
            }
        }

        private void OnOptionPicked(CelesFD_DialogueEngine engine, CelesFD_ResolvedOption o)
        {
            CelesFD_GameComponent.Instance.AddDialogue(true, engine.ResolveRecordText(o));
            engine.ApplyOption(o);
            Log.Message($"[CelesFD] Pick '{o.Label}' → next '{(o.EnterTree.NullOrEmpty() ? o.Next : o.EnterTree)}'");
            if (!o.EnterTree.NullOrEmpty())
                engine.EnterTree(o.EnterTree);   // 跨树进入：恢复进度或从 startNode 开始
            else
                engine.GotoNode(o.Next);
            StartTypingNodeText(engine);
        }

        private void OnReturnToRoot()
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return;
            CelesFD_DialogueEngine engine = gc.DialogueEngine;
            if (engine.CurrentTree != null && !engine.CurrentTree.isRoot && engine.CurrentNode != null)
                engine.SavedGameNode = engine.CurrentNode.defName;   // 保存 game 进度
            gc.AddDialogue(true, "CelesFD_Keyed_Return_Record".Translate());
            engine.StartRootTree();           // 进入 root 树（已 GotoNode startNode）
            StartTypingNodeText(engine);      // 逐字根树 startNode 文本
        }

        private void StartTypingNodeText(CelesFD_DialogueEngine engine)
        {
            replyText = engine.CurrentNode.nodeText;
            replyShownChars = 0;
            replyCharAccum = 0f;
            replyStartTime = Time.realtimeSinceStartup;
            lastReplyTick = Time.realtimeSinceStartup;
            dialogueState = DialogueState.ReplyTyping;
        }

        private void DrawPortraitStartup(Rect rect, float elapsed)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(10f);
            Rect asciiRect = inner;
            asciiRect.yMax -= 26f;
            GUI.color = StartupColor;
            GUI.Label(asciiRect, PortraitAscii, GetScaledDownStyle(0.5f)); //Ascii字号
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
            int charCount = Mathf.Min((int)(elapsed / CharInterval), bootLogFull.Length);
            string shown = bootLogFull.Substring(0, charCount);

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
        
        private void DrawDialogue(Rect rect, float now)
        {
            Widgets.DrawMenuSection(rect);
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return;

            Text.Font = GameFont.Small;

            string bottomLine = null;
            if (dialogueState == DialogueState.ReplyTyping && replyText != null)
            {
                if (now - replyStartTime < PrepDuration)
                {
                    bool cursor = ((int)(now * 2f)) % 2 == 0;
                    bottomLine = cursor ? "_" : " ";       // 预备段：裸 _ 闪烁
                }
                else
                {
                    bottomLine = replyText.Substring(0, replyShownChars);
                }
            }
            else if (dialogueState == DialogueState.AwaitingChoice)
            {
                bool cursor = ((int)(now * 2f)) % 2 == 0;
                bottomLine = "> " + (cursor ? "_" : " ");
            }

            Rect scrollRect = rect.ContractedBy(4f);
            float textWidth = scrollRect.width - 24f;

            float contentHeight = 0f;
            foreach (var e in gc.DialogueHistory)
                contentHeight += Text.CalcHeight(e.IsPlayer ? "> " + e.Text : e.Text, textWidth) + 2f;   // 与渲染间距一致
            float bottomHeight = bottomLine != null ? Text.CalcHeight(bottomLine, textWidth) : 0f;
            contentHeight += bottomHeight;
            float viewHeight = scrollRect.height;

            if (dialoguePinnedToBottom)
                dialogueScrollPos.y = Mathf.Max(0f, contentHeight - viewHeight);

            Widgets.BeginScrollView(scrollRect, ref dialogueScrollPos,
                new Rect(0f, 0f, scrollRect.width - 16f, contentHeight), showScrollbars: true);
            float curY = 0f;
            foreach (var e in gc.DialogueHistory)
            {
                string line = e.IsPlayer ? "> " + e.Text : e.Text;
                float h = Text.CalcHeight(line, textWidth);
                Widgets.Label(new Rect(4f, curY, textWidth, h), line);
                curY += h + 2f;
            }
            if (bottomLine != null)
            {
                Widgets.Label(new Rect(4f, curY, textWidth, bottomHeight), bottomLine);
                curY += bottomHeight;
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
            string text = tickerDef.welcomeTicker[idx];   // 文本经 defInjected 翻译（见 CelesFD_TickerDef.welcomeTicker）
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