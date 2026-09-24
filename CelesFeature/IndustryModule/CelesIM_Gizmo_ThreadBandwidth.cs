using UnityEngine;
using Verse;

namespace CelesFeature
{
    // 带宽方格 Gizmo（批 2 四类格版 2026-09-23——Q1(a) 裁决）
    //   段序与画法逐行照抄原版 MechanitorBandwidthGizmo.cs:100-126：
    //   无信号段（嵌套缩小格：深灰底 + ContractedBy(rows<=2?4:2) 内芯节点色——「占位非活跃」语义，
    //   原版 gestation 段同构）→ 活跃段（节点色）→ 空容量（深灰）→ 超载（红追加末尾）
    //   数据源经 IThreadNode 窄接口（NoSignalLoad）——表现层零图知识
    [StaticConstructorOnStartup]
    public class CelesIM_Gizmo_ThreadBandwidth : Gizmo
    {
        public CelesIM_IThreadNode node;

        private static readonly Color PipEmptyColor = new Color(0.3f, 0.3f, 0.3f, 1f);
        private static readonly Color PipOverloadColor = ColorLibrary.Red;

        private const float LabelHeight = 20f;

        public CelesIM_Gizmo_ThreadBandwidth()
        {
            Order = -90f;  // 参照原版：中继基序（中枢实例覆写 -91 先行——多选排序裁决）
        }

        public override float GetWidth(float maxWidth)
        {
            return Mathf.Min(136f, maxWidth);
        }

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            Rect totalRect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), Height);
            Widgets.DrawWindowBackground(totalRect);

            Rect contentRect = totalRect.ContractedBy(6f);

            // ── 标题行 ──
            Rect labelRect = new Rect(contentRect.x, contentRect.y, contentRect.width / 2f, LabelHeight);
            Widgets.Label(labelRect, node.GizmoLabel);

            Rect countRect = new Rect(contentRect.x + contentRect.width / 2f, contentRect.y,
                contentRect.width / 2f, LabelHeight);
            string countText = $"{node.CurrentLoad} / {node.TotalCapacity}";
            if (node.IsOverloaded)
                GUI.color = Color.red;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(countRect, countText);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            // ── 方格区域 ──
            float gridTop = contentRect.y + LabelHeight + 6f;
            Rect gridRect = new Rect(contentRect.x, gridTop, contentRect.width,
                contentRect.height - LabelHeight - 6f);
            // 段序裁决（2026-09-23）：格不映射设备仅按数排布，恒为 黄（活跃）→ 嵌套缩小（无信号）
            //   → 灰（空容量）→ 红（超出 capacity 追加）
            int noSignalCount = node.NoSignalLoad;
            int totalLoad = node.CurrentLoad;
            int activeCount = totalLoad - noSignalCount;
            int totalPips = Mathf.Max(node.TotalCapacity, totalLoad);
            int normalPips = node.TotalCapacity;

            int rows = 2;
            int cellSize = Mathf.FloorToInt(gridRect.height / (float)rows);
            int cols = Mathf.FloorToInt(gridRect.width / (float)cellSize);
            int safety = 0;
            while (rows * cols < totalPips && safety < 1000)
            {
                rows++;
                cellSize = Mathf.FloorToInt(gridRect.height / (float)rows);
                cols = Mathf.FloorToInt(gridRect.width / (float)cellSize);
                safety++;
            }

            float offsetX = (gridRect.width - (float)(cols * cellSize)) / 2f;
            float offsetY = (gridRect.height - (float)(rows * cellSize)) / 2f;

            int cellPadding = 2;               // 原版 CellPadding
            int nestedShrink = rows <= 2 ? 4 : 2;   // 原版 :102——嵌套格内芯收缩量

            int index = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (index >= totalPips)
                        break;
                    Rect cell = new Rect(gridRect.x + c * cellSize + offsetX,
                        gridRect.y + r * cellSize + offsetY,
                        cellSize, cellSize).ContractedBy(cellPadding);

                    if (index < activeCount)
                        Widgets.DrawRectFast(cell, node.PipColor);
                    else if (index < totalLoad)
                    {
                        // 无信号段：深灰底 + 内芯节点色缩小（绑定保留·占位非活跃）
                        Widgets.DrawRectFast(cell, PipEmptyColor);
                        Widgets.DrawRectFast(cell.ContractedBy(nestedShrink), node.PipColor);
                    }
                    else if (index < normalPips)
                        Widgets.DrawRectFast(cell, PipEmptyColor);
                    else
                        Widgets.DrawRectFast(cell, PipOverloadColor);

                    index++;
                }
                if (index >= totalPips)
                    break;
            }

            return new GizmoResult(GizmoState.Clear);
        }
    }
}
