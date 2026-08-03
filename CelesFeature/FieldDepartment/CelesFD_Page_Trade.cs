using UnityEngine;
using Verse;

namespace CelesFeature
{
    public class CelesFD_Page_Trade : CelesFD_IPage
    {
        public string Title => "CelesFD_Keyed_Tab_Trade".Translate();
        public void Draw(Rect inRect) => CelesFD_PageDrawer.DrawPlaceholder(inRect, Title);
        
        public void Notify_Deactivated() { }
    }
}
