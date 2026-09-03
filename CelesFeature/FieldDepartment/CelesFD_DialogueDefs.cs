using System.Collections.Generic;
using Verse;

namespace CelesFeature
{
    // 变量操作/条件：元素名=操作，值=数量（Equal/Gte 用于 conditions；Set/Add 用于 sets；Add 负值=减）
    // ═══ RPN 操作符（2026-08-25 用户方案）：conditions 列表 = 逆波兰表达式——
    //   原子（Equal/Gte）压栈；Not 弹 1 压 1（取反）；And/Or 弹 2 压 1；栈余值隐式 AND（旧 XML 纯原子列表兼容）
    //   XML 写法：<Not>true</Not> / <And>true</And> / <Or>true</Or>（显式值——空元素 DirectXmlToObject 报错，None 教训）
    public class CelesFD_VarOperationDef
    {
        public string varName;
        public float? Equal;   // 条件：var == value
        public float? Gte;     // 条件：var >= value
        public float? Set;     // 操作：var = value
        public float? Add;     // 操作：var += value（Add 负值=减）
        public bool? Not;
        public bool? And;
        public bool? Or;
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
        public string failNode;                 // 必填（带 comps 时）：动作失败跳转节点
        public List<CelesFD_DialogueActionCompProperties> comps;   // 动作副作用（任一失败即失败，跳 failNode）
        public List<CelesFD_VarOperationDef> sets;
    }

    public class CelesFD_DialogueNodeDef : Def
    {
        public string nodeText;                 // 节点文本（支持 {varName} 插值；defInjected 翻译）
        public List<CelesFD_DialogueActionCompProperties> entryComps;   // 进入节点时执行（刷新变量 / 注入动态选项）
        public List<CelesFD_DialogueOptionDef> options;
    }

    public class CelesFD_DialogueTreeDef : Def
    {
        public string startNode;                // 起始节点 DefName
        public List<CelesFD_VarOperationDef> conditions;   // 根树候选条件（AND）
        public bool isRoot;                     // 参与根树候选
    }
}