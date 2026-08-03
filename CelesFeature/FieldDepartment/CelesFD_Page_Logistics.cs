using UnityEngine;
using Verse;

namespace CelesFeature
{
    public class CelesFD_Page_Logistics : CelesFD_IPage
    {
        public string Title => "CelesFD_Keyed_Tab_Logistics".Translate();
        public void Draw(Rect inRect) => CelesFD_PageDrawer.DrawPlaceholder(inRect, Title);
        
        public void Notify_Deactivated() { }
    }
}
