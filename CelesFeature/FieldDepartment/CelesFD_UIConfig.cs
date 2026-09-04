using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // UI 布局常量与网格算法集中（交易区解耦 2026-08-14；数值集中可调，暂不 XML 化——用户裁决硬编码项除外）
    public static class CelesFD_UIConfig
    {
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
    }
}
