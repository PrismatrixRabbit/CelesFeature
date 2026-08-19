using System.Collections.Generic;
using System.Text;
using Verse;

namespace CelesFeature
{
    public class CelesFD_DialogueEngine
    {
        private readonly Dictionary<string, float> variables = new Dictionary<string, float>();
        private readonly Dictionary<string, string> stringVariables = new Dictionary<string, string>();   // string 变量（插值用，如 beaconTargetName，不持久化）
        private readonly List<CelesFD_ResolvedOption> dynamicOptions = new List<CelesFD_ResolvedOption>();   // 当前节点动态选项（entryComps 注入）

        public float GetVariable(string name)
        {
            if (name.NullOrEmpty()) return 0f;
            return variables.TryGetValue(name, out float v) ? v : 0f;   // 不存在判 0
        }

        public bool HasVariable(string name) => !name.NullOrEmpty() && variables.ContainsKey(name);

        public void SetVariable(string name, float value)
        {
            if (name.NullOrEmpty()) return;
            if (value == 0f)
                variables.Remove(name);      // 设 0 = 移除，避免堆积
            else
                variables[name] = value;
        }

        public string GetStringVariable(string name)
            => !name.NullOrEmpty() && stringVariables.TryGetValue(name, out var v) ? v : null;

        public void SetStringVariable(string name, string value)
        {
            if (name.NullOrEmpty()) return;
            if (value == null)
                stringVariables.Remove(name);
            else
                stringVariables[name] = value;
        }

        public void ApplyOperations(List<CelesFD_VarOperationDef> ops)   // 批量 Set/Add（新格式：元素名=操作）
        {
            if (ops == null) return;
            foreach (var op in ops)
            {
                if (op.varName.NullOrEmpty()) continue;
                if (op.Set.HasValue)
                    SetVariable(op.varName, op.Set.Value);
                else if (op.Add.HasValue)
                    SetVariable(op.varName, GetVariable(op.varName) + op.Add.Value);   // 加为 0 亦移除
            }
        }

        public bool CheckConditions(List<CelesFD_VarOperationDef> conds)   // AND 组合（新格式：Equal/Gte）
        {
            if (conds == null || conds.Count == 0) return true;
            foreach (var c in conds)
            {
                float val = GetVariable(c.varName);
                bool pass;
                if (c.Equal.HasValue) pass = val == c.Equal.Value;
                else if (c.Gte.HasValue) pass = val >= c.Gte.Value;
                else
                {
                    Log.Error($"[CelesFD] Condition on '{c.varName}' has no Equal/Gte");
                    pass = true;
                }
                if (!pass) return false;
            }
            return true;
        }

        public void ResetAllVariables() => variables.Clear();

        public void LogVariables()   // 供 N_DebugInspect 调用
        {
            if (variables.Count == 0) { Log.Message("[CelesFD] Variables: (none)"); return; }
            foreach (var kv in variables)
                Log.Message($"[CelesFD] Var {kv.Key} = {kv.Value}");
        }

        // ═══ D4：节点导航 ═══
        private CelesFD_DialogueTreeDef currentTree;
        private CelesFD_DialogueNodeDef currentNode;
        public CelesFD_DialogueTreeDef CurrentTree => currentTree;
        public CelesFD_DialogueNodeDef CurrentNode => currentNode;
        public string SavedGameNode;   // 上次退出 game 时的节点（D 返回保存 / 点 C 恢复）

        public void GotoNode(string nodeDefName)
        {
            if (nodeDefName.NullOrEmpty()) return;
            var node = DefDatabase<CelesFD_DialogueNodeDef>.GetNamedSilentFail(nodeDefName);
            if (node == null)
            {
                Log.Error($"[CelesFD] GotoNode: node '{nodeDefName}' not found");
                return;
            }
            currentNode = node;
            RefreshDynamicOptions();   // 节点进入：执行 entryComps（刷新变量 / 注入动态选项）
            if (currentTree != null && !currentTree.isRoot)
                SavedGameNode = currentNode.defName;   // game 树内节点变化自动保存进度
            Log.Message($"[CelesFD] GotoNode '{nodeDefName}'");
            if (nodeDefName == "N_DebugInspect") TriggerDebugInspect();
        }

        public List<CelesFD_ResolvedOption> GetVisibleOptions()
        {
            var result = new List<CelesFD_ResolvedOption>();
            if (currentNode?.options == null) return result;
            foreach (var o in currentNode.options)
            {
                if (o.isBranch)
                {
                    if (o.branches == null) continue;
                    foreach (var b in o.branches)
                        if (CheckConditions(b.conditions))
                        {
                            result.Add(new CelesFD_ResolvedOption
                            { Label = b.label, RecordText = b.recordText, Next = b.next, Sets = b.sets });
                            break;   // 首个满足分支
                        }
                }
                else if (o.branches != null && o.branches.Count > 0)
                {
                    Log.Error($"[CelesFD] Node {currentNode.defName} option has branches but isBranch=false; using first branch");
                    var b = o.branches[0];
                    result.Add(new CelesFD_ResolvedOption
                    { Label = b.label, RecordText = b.recordText, Next = b.next, Sets = b.sets, IsReward = o.isReward });
                }
                else
                {
                    if (!CheckConditions(o.conditions)) continue;
                    result.Add(new CelesFD_ResolvedOption
                    { Label = o.label, RecordText = o.recordText, Next = o.next, EnterTree = o.enterTree,
                        Sets = o.sets, IsReward = o.isReward, CompProps = o.comps, FailNode = o.failNode });
                }
            }
            result.AddRange(dynamicOptions);   // 节点 entryComps 注入的动态选项（如放弃清单）
            return result;
        }

        // 节点进入：清空并重新生成动态选项（entryComps 的 OnNodeEntered）
        private void RefreshDynamicOptions()
        {
            dynamicOptions.Clear();
            if (currentNode?.entryComps != null)
                foreach (var cp in currentNode.entryComps)
                    if (cp != null && cp.compClass != null)
                        cp.MakeComp().OnNodeEntered(this);
        }

        public void ClearDynamicOptions() => dynamicOptions.Clear();
        public void AddDynamicOption(CelesFD_ResolvedOption o) => dynamicOptions.Add(o);

        // 节点文本 {varName} 插值：优先 string 变量，否则 float 变量取整显示
        public string ResolveNodeText(string text)
        {
            if (text.NullOrEmpty()) return text;
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '{')
                {
                    int close = text.IndexOf('}', i + 1);
                    if (close > i)
                    {
                        string varName = text.Substring(i + 1, close - i - 1);
                        string sv = GetStringVariable(varName);
                        sb.Append(sv ?? GetVariable(varName).ToString("0"));
                        i = close;
                        continue;
                    }
                }
                sb.Append(text[i]);
            }
            return sb.ToString();
        }

        public string ResolveRecordText(CelesFD_ResolvedOption r)
            => r.RecordText.NullOrEmpty() ? r.Label : r.RecordText;

        // 动作先行 → 成功执行 sets；动作失败返回 false（不执行 sets、不跳转）
        public bool ApplyOption(CelesFD_ResolvedOption r)
        {
            if (r.CompProps != null)
                foreach (var ap in r.CompProps)
                {
                    if (ap == null || ap.compClass == null) continue;
                    if (!ap.MakeComp().TryExecute(this))
                        return false;
                }
            ApplyOperations(r.Sets);
            foreach (var s in r.Sets ?? new List<CelesFD_VarOperationDef>())
                Log.Message($"[CelesFD] VarOp {s.varName} {(s.Set.HasValue ? "Set=" + s.Set : s.Add.HasValue ? "Add=" + s.Add : "?")} → now {GetVariable(s.varName)}");
            return true;
        }

        private void TriggerDebugInspect()
        {
            Log.Message("[CelesFD] === Debug Inspect ===");
            LogVariables();
            Log.Message($"[CelesFD] CurrentTree={currentTree?.defName} CurrentNode={currentNode?.defName}");
        }

        // ═══ D6：持久化接口 ═══
        public Dictionary<string, float> ExportVariables() => new Dictionary<string, float>(variables);

        public void ImportVariables(Dictionary<string, float> imported)
        {
            variables.Clear();
            foreach (var kv in imported) SetVariable(kv.Key, kv.Value);   // 0 值经 SetVariable 自动移除
        }

        public string CurrentTreeName => currentTree?.defName;
        public string CurrentNodeName => currentNode?.defName;

        public void RestoreState(string treeName, string nodeName)
        {
            if (!treeName.NullOrEmpty())
            {
                var t = DefDatabase<CelesFD_DialogueTreeDef>.GetNamedSilentFail(treeName);
                if (t != null) currentTree = t;
            }
            if (!nodeName.NullOrEmpty())
            {
                var n = DefDatabase<CelesFD_DialogueNodeDef>.GetNamedSilentFail(nodeName);
                if (n != null) currentNode = n;
            }
        }

        // ═══ D7：根树选择 ═══
        public void StartRootTree()
        {
            foreach (var tree in DefDatabase<CelesFD_DialogueTreeDef>.AllDefsListForReading)
                if (tree.isRoot && CheckConditions(tree.conditions))
                {
                    currentTree = tree;
                    GotoNode(tree.startNode);
                    return;
                }
            Log.Warning("[CelesFD] No root tree available");
        }

        // ═══ 跨树跳转（D7 扩展）：进入指定树，恢复进度或从 startNode 开始 ═══
        public void EnterTree(string treeDefName)
        {
            if (treeDefName.NullOrEmpty())
            {
                Log.Error("[CelesFD] EnterTree: empty tree name");
                return;
            }
            bool wasInRootTree = currentTree != null && currentTree.isRoot;
            var tree = DefDatabase<CelesFD_DialogueTreeDef>.GetNamedSilentFail(treeDefName);
            if (tree == null)
            {
                Log.Error($"[CelesFD] EnterTree: tree '{treeDefName}' not found");
                return;
            }
            if (tree.startNode.NullOrEmpty())
            {
                Log.Error($"[CelesFD] Tree '{treeDefName}' has no startNode");
                return;
            }
            currentTree = tree;
            // 从根树进入且已有 game 存档 → 恢复；否则从 startNode 重新开始
            if (wasInRootTree && !SavedGameNode.NullOrEmpty())
            {
                var saved = DefDatabase<CelesFD_DialogueNodeDef>.GetNamedSilentFail(SavedGameNode);
                if (saved != null)
                {
                    GotoNode(saved.defName);
                    return;
                }
                Log.Warning($"[CelesFD] Saved node '{SavedGameNode}' not found; starting from startNode");
            }
            SavedGameNode = null;
            GotoNode(tree.startNode);
        }
    }
}