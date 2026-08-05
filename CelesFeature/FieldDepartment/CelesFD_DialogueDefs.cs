using System.Collections.Generic;
using Verse;

namespace CelesFeature
{
    public enum CelesFD_VarOp { Set, Add }
    public enum CelesFD_VarCompare { Equal, GreaterOrEqual }

    public class CelesFD_VarOperationDef
    {
        public string varName;
        public float value;
        public CelesFD_VarOp op;                // Set / Add（Add 负值=减）
        public CelesFD_VarCompare comparison;   // 条件专用：Equal / GreaterOrEqual
    }

    public class CelesFD_DialogueBranchDef
    {
        public List<CelesFD_VarOperationDef> conditions;   // AND 组合
        public string label;
        public string recordText;
        public string next;
        public List<CelesFD_VarOperationDef> sets;
    }

    public class CelesFD_DialogueOptionDef
    {
        public bool isBranch;                   // 分支容器：内含 branches，运行时取首个满足分支
        public bool isReward;                   // 奖励第四选项（蓝色）
        public List<CelesFD_DialogueBranchDef> branches;   // isBranch=true 时用
        public List<CelesFD_VarOperationDef> conditions;   // 普通/Reward 显示条件（AND）
        public string label;                    // 普通/Reward 用
        public string recordText;
        public string next;
        public string enterTree;                // 跨树进入：非空时忽略 next，进树后恢复进度或从 startNode 开始
        public List<CelesFD_VarOperationDef> sets;
    }

    public class CelesFD_DialogueNodeDef : Def
    {
        public string nodeText;                 // 节点文本（defInjected 翻译）
        public List<CelesFD_DialogueOptionDef> options;
    }

    public class CelesFD_DialogueTreeDef : Def
    {
        public string startNode;                // 起始节点 DefName
        public List<CelesFD_VarOperationDef> conditions;   // 根树候选条件（AND）
        public bool isRoot;                     // 参与根树候选
    }
}