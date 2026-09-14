using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // UI 布局常量与网格算法集中（交易区解耦 2026-08-14；数值集中可调，暂不 XML 化——用户裁决硬编码项除外）
    [Verse.StaticConstructorOnStartup]   // Texture2D 静态字段须主线程加载（错误3修复——CircleTex 预初始化）
    public static class CelesFD_UIConfig
    {
        static CelesFD_UIConfig() { _ = CircleTex; }   // 主线程预初始化圆纹理

        // 卡片布局
        public const float CardAspect = 3f;          // 高宽 1:3
        public const float CardMinHeight = 82f;      // 高度保底（内容 ~78px，防窄窗文本遮挡）
        public const float CardGap = 4f;
        public const float CardLeftW = 60f;          // 左区宽（品名区 ≈ 4.4 字符——修复 4 汉字触发滚动晃动）
        public const float CardRightW = 76f;         // 右区宽（有锁钮）
        public const float CardRightWNarrow = 70f;   // 右区宽（无锁钮）
        public const float CardStripH = 8f;          // 底部色条带高
        public const float NameScrollSpeed = 30f;    // 品名往复滚动速度（px/s）

        // 字体分级（欢迎页方案：13 = Small 基准；次级 11；方向 10）
        public const float FontMain = 13f;
        public const float FontSub = 11f;
        public const float FontDir = 10f;

        // 双列网格（订单区/已接取区/购物车区三处统一——消除重复算法）
        public static void CalcCardGrid(Rect listRect, int count,
            out float cardW, out float cardH, out float contentH)
        {
            cardW = (listRect.width - CardGap) / 2f;
            cardH = Mathf.Max(cardW / CardAspect, CardMinHeight);
            contentH = Mathf.Ceil(count / 2f) * (cardH + CardGap);
        }

        // 欢迎页方案（Page_Welcome.cs:514-519）：GUIStyle 复制 + 显式 fontSize
        // 注意：GUIStyle.lineHeight 为只读（Unity 由字体按 fontSize 自动计算——设置 fontSize 后自动更新）
        // 下半部遮挡的保证 = 文本 rect 高度 ≥ 实测 lineHeight（修复 2026-08-15：废除"字号×1.4"自拟假设——
        //   原版行高 = 运行时实测缓存（Text.cs:194-200 CalcHeight("W",999f)）；中文 fallback 字体的实际行高
        //   不可由固定比例预测，布局一律以 ScaledLineHeight() 实测值驱动）
        // G22 修复（归 W-3）：按 size 缓存 GUIStyle——原每次调用 new GUIStyle（卡片密集页每帧数十至上百次分配 GC 压力）
        // 缓存模式同 ScaledLineHeight（:50-61 同文件先例）；GUIStyle 为引用类型且仅读使用，缓存安全
        private static readonly Dictionary<int, GUIStyle> scaledStyles = new Dictionary<int, GUIStyle>();

        public static GUIStyle GetScaledStyle(float size)
        {
            int key = Mathf.FloorToInt(size);
            GUIStyle style;
            if (!scaledStyles.TryGetValue(key, out style))
            {
                style = new GUIStyle(Text.CurFontStyle);
                style.fontSize = Mathf.Max(4, key);
                style.wordWrap = false;
                scaledStyles[key] = style;
            }
            return style;
        }

        // 缩放字体实测行高（照抄原版 Text.cs:194-200 同款做法：CalcHeight("W", 999f) 单行实测 + 缓存）
        // 所有缩放文本 rect 高度必须用此值（或 ≥ 此值），不得硬编码——Widgets.Label/GUI.Label 均无裁剪，
        // rect 高度不足时文本底部溢出被后续 UI 覆盖（"下半部被遮盖"根因）
        private static readonly Dictionary<int, float> scaledLineHeights = new Dictionary<int, float>();

        public static float ScaledLineHeight(float size)
        {
            int key = Mathf.FloorToInt(size);
            if (!scaledLineHeights.TryGetValue(key, out float h))
            {
                // GUIStyle.CalcHeight 形参为 GUIContent（原版 Text.cs:209-212 用 tmpTextGUIContent 复用实例，此处单次测量直接 new）
                h = GetScaledStyle(size).CalcHeight(new GUIContent("W"), 999f);
                scaledLineHeights[key] = h;
            }
            return h;
        }

        // 类别色条带（开灰/内蓝/珍金/彩粉/突红）
        public static Color CategoryColor(CelesFD_MarketCategory cat)
        {
            switch (cat)
            {
                case CelesFD_MarketCategory.Open: return new Color(0.6f, 0.6f, 0.6f);       // 灰
                case CelesFD_MarketCategory.Internal: return new Color(0.3f, 0.5f, 0.9f);   // 蓝
                case CelesFD_MarketCategory.Precious: return new Color(0.9f, 0.75f, 0.2f);  // 金
                case CelesFD_MarketCategory.EasterEgg: return new Color(0.9f, 0.4f, 0.8f);  // 粉
                case CelesFD_MarketCategory.Urgent: return new Color(0.85f, 0.2f, 0.2f);    // 红
                default: return Color.gray;
            }
        }

        // ═══ R-1 UI 改造共用 helper（2026-09-06 审查提取——三页复用，耦合度低：绘制骨架，数据区调用方自绘）═══

        // 等级头（武备/人员两栏共用）：文字高度以 ScaledLineHeight 实测驱动（修"等级：x"下部截断——
        //   旧实现用 cardH×0.25 派生高，人员卡 64px 派生 16px < 行高必截断；两侧统一改此实现）
        // 坐标空间：滚动内容坐标（x=0 起）。返回占用高度。
        public static float DrawLevelHeader(float y, float width, int level, int currentLevel)
        {
            var style = GetScaledStyle(FontSub);
            float h = ScaledLineHeight(FontSub) + 2f;
            Color prev = GUI.color;
            GUI.color = level <= currentLevel ? Color.white : Color.gray;
            GUI.Label(new Rect(0f, y, width, h), "CelesFD_Keyed_Level".Translate(level), style);
            GUI.color = prev;
            Widgets.DrawLineHorizontal(0f, y + h + 1f, width);
            return h + 5f + CardGap;
        }

        // 卡片骨架（武备/人员卡共用）：底色 + icon 左上（0.7h，缺省安全）+ 品名底部整宽（LowerLeft）
        //   + 整卡点击。bodyRect = icon 右侧数据区（品名上方）。grayIcon = 等级锁定灰显。
        public static bool DrawCardBackdrop(Rect rect, string iconPath, string label, bool grayIcon, out Rect bodyRect, bool checkClick = true)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.13f, 0.13f, 0.13f, 0.6f));
            float iconSize = rect.height * 0.7f;
            if (!iconPath.NullOrEmpty())
            {
                Texture2D tex = ContentFinder<Texture2D>.Get(iconPath, false);
                if (tex != null)
                {
                    GUI.color = grayIcon ? new Color(0.4f, 0.4f, 0.4f, 1f) : Color.white;
                    GUI.DrawTexture(new Rect(rect.x + 4f, rect.y + 2f, iconSize, iconSize), tex, ScaleMode.ScaleToFit);
                    GUI.color = Color.white;
                }
            }
            float nameH = ScaledLineHeight(FontSub);
            GUIStyle nameStyle = new GUIStyle(GetScaledStyle(FontSub));
            nameStyle.alignment = TextAnchor.LowerLeft;
            GUI.color = grayIcon ? Color.gray : Color.white;
            GUI.Label(new Rect(rect.x + 4f, rect.yMax - nameH - 2f, rect.width - 8f, nameH), label, nameStyle);
            GUI.color = Color.white;
            float bodyX = rect.x + iconSize + 10f;
            bodyRect = new Rect(bodyX, rect.y + 2f, rect.xMax - bodyX - 4f, rect.height - nameH - 6f);
            return checkClick && Widgets.ButtonInvisible(rect, false);   // checkClick=false：调用方自行在控件之后注册整卡点击（IMGUI 先绘先消费——人员卡 ± 失效根因）
        }

        // ？帮助按钮（武备/交易/物流三页共用）：正方形（宽=高=height），字面"？"不翻译
        public static bool DrawHelpButton(float x, float y, float height)
        {
            return Widgets.ButtonText(new Rect(x, y, height, height), "?");
        }

        // GUIStyle 克隆 helper（审查抽取 #1——8 处三行模式收敛；克隆纪律不变：每次独立实例不污染缓存）
        public static GUIStyle CloneScaledStyle(float size)
        {
            return new GUIStyle(GetScaledStyle(size));
        }

        // 帮助长文统一绘制（抽取 #3 + 风格统一 1b：三页同字号同留间隔——CurFontStyle 基准对齐其他 description）
        public static void DrawInfoText(Rect rect, string text)
        {
            GUIStyle style = new GUIStyle(Text.CurFontStyle);
            style.wordWrap = true;
            GUI.Label(rect, text, style);
        }

        // 锁定卡"封锁线"（2026-09-06 细节批）：底侧半透黑条带（全卡宽、恰容文本）+ 居中略大灰字（FontSub）
        // 锁定卡"封锁线"（细节批终版）：条带=背景层（先绘半透黑），文本后绘浮于其上；
        //   垂直居中横贯卡片；字号 16.5（FontSub×1.5 两侧对齐）；条带加高（行高+10）
        public static void DrawLockBand(Rect cardRect)
        {
            float fontSize = 16.5f;
            GUIStyle style = new GUIStyle(GetScaledStyle(fontSize));
            style.alignment = TextAnchor.MiddleCenter;
            float bandH = ScaledLineHeight(fontSize) + 10f;
            Rect band = new Rect(cardRect.x, cardRect.center.y - bandH / 2f, cardRect.width, bandH);
            Widgets.DrawBoxSolid(band, new Color(0f, 0f, 0f, 0.55f));
            Color prev = GUI.color;
            GUI.color = new Color(0.75f, 0.75f, 0.75f, 0.95f);
            GUI.Label(band, "CelesFD_Keyed_ArmoryLevelLocked".Translate(), style);
            GUI.color = prev;
        }

        // 勾选+标签（交易页 DrawCheckboxLabel 提取共用——细节批：两侧对齐统一；
        //   24px 原版 checkbox + 26px 右偏移 + WordWrap 动态行高 + 默认字体——非缩放样式）
        public static float DrawCheckboxLabel(Rect rect, string label, ref bool value, float width)
        {
            bool result = value;
            Widgets.Checkbox(new Vector2(rect.x, rect.y), ref result);   // 原版签名 :1191（Vector2, ref bool, size=24f）
            value = result;
            float h = CalcLabelHeight(label, width - 26f);
            Widgets.Label(new Rect(rect.x + 26f, rect.y, width - 26f, h), label);
            return Mathf.Max(22f, h);
        }

        public static float CalcLabelHeight(string text, float width)
        {
            bool prevWrap = Text.WordWrap;
            Text.WordWrap = true;
            float h = Text.CalcHeight(text, width);
            Text.WordWrap = prevWrap;
            return h;
        }

        // 节点"圈"纹理（从 Page_Logistics 迁移——C 提取共用；物品/人员两族进度条共用）
        private static Texture2D _circleTex;
        public static Texture2D CircleTex
        {
            get
            {
                if (_circleTex == null)
                {
                    const int size = 16;
                    _circleTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                    float r = size / 2f - 0.5f;
                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size; x++)
                        {
                            float dx = x - r, dy = y - r;
                            _circleTex.SetPixel(x, y, dx * dx + dy * dy <= r * r ? Color.white : Color.clear);
                        }
                    _circleTex.Apply();
                }
                return _circleTex;
            }
        }

        // 节点进度线（C 提取——物品/人员两族共用唯一实现：灰底线 + 蓝进度线 + 等分圆节点亮暗）
        // 参数：lower = 下区 Rect；startTick = 运输起点；totalTicks = 总时长；nodeCount = 节点数（等分 nodeCount-1 段）
        public static void DrawNodeProgressLine(Rect lower, long startTick, long totalTicks, int nodeCount)
        {
            long now = Verse.Find.TickManager.TicksGame;
            float margin = 10f;
            float lineY = lower.y + lower.height / 2f;
            float x0 = lower.x + margin;
            float x1 = lower.xMax - margin;
            float nodeGap = (x1 - x0) / (nodeCount - 1);
            float nodeSize = 8f;
            Widgets.DrawLineHorizontal(x0, lineY, x1 - x0, new Color(0.35f, 0.35f, 0.35f));
            float segTicks = (float)totalTicks / (nodeCount - 1);
            int litSegs = UnityEngine.Mathf.Clamp((int)((now - startTick) / segTicks), 0, nodeCount - 1);
            if (litSegs > 0)
                Widgets.DrawLineHorizontal(x0, lineY, litSegs * nodeGap, new Color(0.45f, 0.75f, 0.95f));
            for (int n = 0; n < nodeCount; n++)
            {
                float nodeX = x0 + n * nodeGap;
                bool lit = (now - startTick) >= n * segTicks - 1f;
                GUI.color = lit ? new Color(0.45f, 0.75f, 0.95f) : new Color(0.4f, 0.4f, 0.4f);
                GUI.DrawTexture(new UnityEngine.Rect(nodeX - nodeSize / 2f, lineY - nodeSize / 2f, nodeSize, nodeSize), CircleTex);
                GUI.color = Color.white;
            }
        }
    }
}
