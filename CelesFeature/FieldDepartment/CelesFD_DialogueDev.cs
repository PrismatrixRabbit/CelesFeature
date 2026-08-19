using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace CelesFeature
{
    public static class CelesFD_DialogueDev
    {
        private static CelesFD_DialogueEngine Engine => CelesFD_GameComponent.Instance?.DialogueEngine;

        [DebugAction("CelesFD", "Set dialogue variable")]
        private static void DevSetVariable()
        {
            if (Engine == null) return;
            var names = CollectVariableNames();
            if (names.Count == 0)
            {
                Log.Message("[CelesFD] No dialogue variables found in defs");
                return;
            }
            var options = new List<DebugMenuOption>();
            foreach (var name in names)
            {
                options.Add(new DebugMenuOption(name + " = 0", DebugMenuOptionMode.Action,
                    delegate { Engine.SetVariable(name, 0f); Log.Message($"[CelesFD] Dev set {name}=0"); }));
                options.Add(new DebugMenuOption(name + " = 1", DebugMenuOptionMode.Action,
                    delegate { Engine.SetVariable(name, 1f); Log.Message($"[CelesFD] Dev set {name}=1"); }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        // 从全部树的 conditions 与全部节点的 options/branches 中动态收集变量名
        private static List<string> CollectVariableNames()
        {
            var names = new HashSet<string>();
            foreach (var tree in DefDatabase<CelesFD_DialogueTreeDef>.AllDefsListForReading)
                CollectOps(names, tree.conditions);
            foreach (var node in DefDatabase<CelesFD_DialogueNodeDef>.AllDefsListForReading)
            {
                if (node.options == null) continue;
                foreach (var o in node.options)
                {
                    CollectOps(names, o.conditions);
                    CollectOps(names, o.sets);
                    if (o.branches == null) continue;
                    foreach (var b in o.branches)
                    {
                        CollectOps(names, b.conditions);
                        CollectOps(names, b.sets);
                    }
                }
            }
            var list = new List<string>(names);
            list.Sort();
            return list;
        }

        private static void CollectOps(HashSet<string> names, List<CelesFD_VarOperationDef> ops)
        {
            if (ops == null) return;
            foreach (var op in ops)
                if (!op.varName.NullOrEmpty())
                    names.Add(op.varName);
        }

        [DebugAction("CelesFD", "Jump to dialogue node")]
        private static void DevJumpToNode()
        {
            if (Engine == null) return;
            var options = new List<DebugMenuOption>();
            foreach (var n in DefDatabase<CelesFD_DialogueNodeDef>.AllDefsListForReading)
            {
                var name = n.defName;
                options.Add(new DebugMenuOption(name, DebugMenuOptionMode.Action,
                    delegate { Engine.GotoNode(name); }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        [DebugAction("CelesFD", "Log visible options")]
        private static void DevLogVisibleOptions()
        {
            if (Engine?.CurrentNode == null) { Log.Message("[CelesFD] No current node"); return; }
            Log.Message($"[CelesFD] Visible options at {Engine.CurrentNode.defName}:");
            foreach (var o in Engine.GetVisibleOptions())
            {
                string kind = o.IsReward ? "REWARD" : "normal";
                string target = o.EnterTree.NullOrEmpty() ? o.Next : "TREE:" + o.EnterTree;
                Log.Message($"[CelesFD]   [{kind}] '{o.Label}' → {target}");
            }
        }

        // 选点器验证：近/远点输出距离分布（正态验证），随机输出成功率
        [DebugAction("CelesFD", "Test tile selector")]
        private static void DevTestTileSelector()
        {
            Faction beacon = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => f.def == CelesFD_DefOf.Celes_BeaconFaction);
            Log.Message($"[CelesFD] TileSelector beacon: {(beacon != null ? beacon.Name : "NULL")}   worldFactions={Find.World.info.factions?.Count ?? -1}");
            var near = new List<int>();
            var far = new List<int>();
            int rndOk = 0;
            for (int i = 0; i < 20; i++)
            {
                if (CelesFD_TileSelector.TryFindNearTile(out var nt)
                    && TileFinder.TryFindRandomPlayerTile(out var root0, allowCaravans: false, validator: null, canBeSpace: false))
                    near.Add(Find.WorldGrid.TraversalDistanceBetween(root0, nt));
                if (CelesFD_TileSelector.TryFindFarTile(out var ft)
                    && TileFinder.TryFindRandomPlayerTile(out var root1, allowCaravans: false, validator: null, canBeSpace: false))
                    far.Add(Find.WorldGrid.TraversalDistanceBetween(root1, ft));
                if (beacon != null && CelesFD_TileSelector.TryFindRandomTile(out var rt, beacon))
                    rndOk++;
            }
            Log.Message($"[CelesFD] TileSelector Near dists(20): {string.Join(",", near)}");
            Log.Message($"[CelesFD] TileSelector Far  dists(20): {string.Join(",", far)}");
            Log.Message($"[CelesFD] TileSelector Random success: {rndOk}/20");
        }
    }
}
