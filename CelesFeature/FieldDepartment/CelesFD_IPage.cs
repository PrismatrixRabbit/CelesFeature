using UnityEngine;
using Verse;

namespace CelesFeature
{
    public interface CelesFD_IPage
    {
        string Title { get; }
        void Draw(Rect inRect);
        void Notify_Deactivated();
    }

    public static class CelesFD_PageDrawer
    {
        public static void DrawPlaceholder(Rect inRect, string pageName)
        {
            float curY = inRect.y + 20f;
            for (int i = 1; i <= 5; i++)
            {
                Widgets.Label(new Rect(inRect.x + 20f, curY, inRect.width - 40f, 30f), i + ". " + pageName);
                curY += 30f;
            }
        }
    }
}