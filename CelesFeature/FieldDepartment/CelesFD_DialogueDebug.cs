using System.Collections.Generic;
using Verse;

namespace CelesFeature
{
    [StaticConstructorOnStartup]
    public static class CelesFD_DialogueDebug
    {
        static CelesFD_DialogueDebug()
        {
            LogDefs();          // D1：Def 加载验证
            TestVariableSystem();   // D3：变量系统单元测试
        }

        private static void LogDefs()
        {
            var trees = DefDatabase<CelesFD_DialogueTreeDef>.AllDefsListForReading;
            var nodes = DefDatabase<CelesFD_DialogueNodeDef>.AllDefsListForReading;

            Log.Message($"[CelesFD] Dialogue defs loaded: {trees.Count} trees, {nodes.Count} nodes");
            foreach (var t in trees)
                Log.Message($"[CelesFD]   Tree {t.defName}: start={t.startNode} isRoot={t.isRoot} conditions={(t.conditions != null ? t.conditions.Count : 0)}");

            foreach (var n in nodes)
                Log.Message($"[CelesFD]   Node {n.defName}: textLen={n.nodeText.Length} options={(n.options != null ? n.options.Count : 0)}");

            // 条件触发：数据缺失才 Log.Error（不刷屏）
            foreach (var t in trees)
                if (t.startNode.NullOrEmpty())
                    Log.Error($"[CelesFD] Tree {t.defName} has empty startNode");
            foreach (var n in nodes)
            {
                if (n.nodeText == null)
                    Log.Error($"[CelesFD] Node {n.defName} has null nodeText");
                if (n.options == null) continue;
                foreach (var o in n.options)
                {
                    if (o.isBranch)   // 分支容器：校验 branches，不校验 label/next
                    {
                        if (o.branches == null || o.branches.Count == 0)
                        {
                            Log.Error($"[CelesFD] Node {n.defName} isBranch option has no branches");
                            continue;
                        }
                        foreach (var b in o.branches)
                        {
                            if (b.label.NullOrEmpty())
                                Log.Error($"[CelesFD] Node {n.defName} branch missing label");
                            if (b.next.NullOrEmpty())
                                Log.Error($"[CelesFD] Node {n.defName} branch '{b.label}' missing next");
                            if (b.sets != null)
                                foreach (var s in b.sets)
                                    if (s.varName.NullOrEmpty())
                                        Log.Error($"[CelesFD] Node {n.defName} branch set missing varName");
                        }
                        continue;
                    }
                    // 普通 / Reward 选项（enterTree 选项无需 next）
                    if (o.label.NullOrEmpty())
                        Log.Error($"[CelesFD] Node {n.defName} option missing label");
                    if (o.enterTree.NullOrEmpty() && o.next.NullOrEmpty())
                        Log.Error($"[CelesFD] Node {n.defName} option '{o.label}' missing next");
                    if (o.comps != null && o.failNode.NullOrEmpty())
                        Log.Error($"[CelesFD] Node {n.defName} option '{o.label}' has comps but no failNode");
                    if (o.sets != null)
                        foreach (var s in o.sets)
                            if (s.varName.NullOrEmpty())
                                Log.Error($"[CelesFD] Node {n.defName} option set missing varName");
                }
            }
        }
        
        private static void TestVariableSystem()
        {
            var engine = new CelesFD_DialogueEngine();
            // Set weapon=1 → Equal 1 满足 / Gte 2 不满足
            engine.ApplyOperations(new List<CelesFD_VarOperationDef>
                { new CelesFD_VarOperationDef { varName = "weapon", Set = 1f } });
            bool eqTrue = engine.CheckConditions(new List<CelesFD_VarOperationDef>
             { new CelesFD_VarOperationDef { varName = "weapon", Equal = 1f } });
            bool gteFalse = engine.CheckConditions(new List<CelesFD_VarOperationDef>
                { new CelesFD_VarOperationDef { varName = "weapon", Gte = 2f } });
            // Add +1 → 2 → Gte 2 满足
            engine.ApplyOperations(new List<CelesFD_VarOperationDef>
                { new CelesFD_VarOperationDef { varName = "weapon", Add = 1f } });
            bool gteTrue = engine.CheckConditions(new List<CelesFD_VarOperationDef>
                { new CelesFD_VarOperationDef { varName = "weapon", Gte = 2f } });
            // Set 0 → 移除（HasVariable 应为 false，GetVariable 判 0）
            engine.ApplyOperations(new List<CelesFD_VarOperationDef>
                { new CelesFD_VarOperationDef { varName = "weapon", Set = 0f } });
            bool removed = !engine.HasVariable("weapon");
            float afterZero = engine.GetVariable("weapon");
            // AND 组合
            engine.SetVariable("fuel", 1f);
            bool andTrue = engine.CheckConditions(new List<CelesFD_VarOperationDef>
            {
                new CelesFD_VarOperationDef { varName = "weapon", Equal = 0f },   // weapon 已移除 → 判 0
                new CelesFD_VarOperationDef { varName = "fuel", Equal = 1f }
            });
            // Reset → 清空，GetVariable 判 0
            engine.ResetAllVariables();
            float afterReset = engine.GetVariable("fuel");
            Log.Message($"[CelesFD] VarSys test: eqTrue={eqTrue} gteFalse={gteFalse} gteTrue={gteTrue} " +
                $"zeroRemoved={removed} afterZero={afterZero} andTrue={andTrue} afterReset={afterReset}");
            if (!eqTrue || gteFalse || !gteTrue || !removed || afterZero != 0f || !andTrue || afterReset != 0f)
                Log.Error("[CelesFD] VarSys test FAILED");
        }
    }
}