using System.Collections.Generic;
using Verse;

namespace CelesFeature
{
    public class CelesFD_TickerDef : Def
    {
        public List<string> welcomeTicker;   // 跑马灯文本（英文源，中文经 defInjected 索引路径覆盖，见 DefInjected/CelesFeature.CelesFD_TickerDef/）
    }
}