using System.Collections.Generic;

namespace CelesFeature
{
    public class CelesFD_ResolvedOption
    {
        public string Label;
        public string RecordText;   // 空则用 Label
        public string Next;
        public string EnterTree;    // 非空时跨树进入（忽略 Next）
        public List<CelesFD_VarOperationDef> Sets;
        public bool IsReward;
    }
}