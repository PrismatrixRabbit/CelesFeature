using UnityEngine;
using Verse;

namespace CelesFeature
{
    public interface CelesFD_ISubPage
    {
        void DrawSubPage(Rect rect);   // 在发信器窗口正上子页区域绘制（Dialog_Comms 按当前页分派）
    }
}
