using UnityEngine;
using Verse;

namespace CelesFeature
{
    [StaticConstructorOnStartup]
    public class Gizmo_ThreadBandwidth : Gizmo
    {
        public IThreadNode node;

        private static readonly Color PipEmptyColor = new Color(0.3f, 0.3f, 0.3f, 1f);
        private static readonly Color PipOverloadColor = ColorLibrary.Red;

        private const float LabelHeight = 20f;

        public Gizmo_ThreadBandwidth()
        {
            Order = -90f;  // 参照原版：排在所有 Gizmo 最前/最左
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
            int totalPips = Mathf.Max(node.TotalCapacity, node.CurrentLoad);
            int normalPips = node.TotalCapacity;
            int usedPips = Mathf.Min(node.CurrentLoad, normalPips);

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

            int cellPadding = 2;  // 参照原版 CellPadding，始终为 2

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

                    if (index < usedPips)
                        Widgets.DrawRectFast(cell, node.PipColor);
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