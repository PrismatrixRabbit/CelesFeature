using UnityEngine;
using Verse;

namespace CelesFeature
{
    public class CelesFD_Page_Quest : CelesFD_IPage
    {
        public string Title => "CelesFD_Keyed_Tab_Quest".Translate();
        public void Draw(Rect inRect) => CelesFD_PageDrawer.DrawPlaceholder(inRect, Title);
        
        public void Notify_Deactivated() { }
    }
}
