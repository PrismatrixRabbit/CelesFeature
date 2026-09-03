 using System;
using Verse;

namespace CelesFeature
{
    // ═══ 资源类型（对齐 CelesFD_GameComponent 虚拟资源；FD=外勤部派系资源，预留原版 Thing 检测扩展空间） ═══
    public enum CelesFD_FDResourceType { Credit, Key, Fame, TradeVolume }

    // 动作内部操作（XML 用元素名 None/Add/Set 配置，值=数量）
    public enum CelesFD_ResourceOperation { None, Add, Set }

    // ═══ 动作配置（仿 CompProperties，挂在选项 comps 下） ═══
    public abstract class CelesFD_DialogueActionCompProperties
    {
        public Type compClass;

        public CelesFD_DialogueActionCompProperties() { }

        public CelesFD_DialogueActionCompProperties(Type compClass) { this.compClass = compClass; }

        public CelesFD_DialogueActionComp MakeComp()
        {
            var comp = (CelesFD_DialogueActionComp)Activator.CreateInstance(compClass);
            comp.props = this;
            return comp;
        }
    }

    // ═══ 动作基类：选项选择后执行；entryComps 覆写 OnNodeEntered（节点进入副作用） ═══
    public abstract class CelesFD_DialogueActionComp
    {
        public CelesFD_DialogueActionCompProperties props;

        // 执行动作；成功 true（跳 next/enterTree），失败 false（跳 failNode 或停留）
        public abstract bool TryExecute(CelesFD_DialogueEngine engine);

        // 节点进入时执行（节点 entryComps）：默认无操作；供刷新变量 / 注入动态选项覆写
        public virtual void OnNodeEntered(CelesFD_DialogueEngine engine) { }
    }

    // ═══ 通用资源 comp：检查 + 操作 合一，XML 参数化配置（元素名=操作，值=数量；None 隐含仅检查） ═══
    public class CelesFD_DialogueActionCompProperties_Resource : CelesFD_DialogueActionCompProperties
    {
        public CelesFD_FDResourceType resource = CelesFD_FDResourceType.Credit;
        public int checkNeed;                     // 前置检查：资源 >= checkNeed（0 跳过）
        public int? None;                         // 仅检查（可显式 <None>0</None>，或隐含：无 Add/Set 即仅检查）
        public int? Add;                          // 加（负值减）
        public int? Set;                          // 设为

        public CelesFD_DialogueActionCompProperties_Resource() { compClass = typeof(CelesFD_DialogueActionComp_Resource); }
    }

    public class CelesFD_DialogueActionComp_Resource : CelesFD_DialogueActionComp
    {
        public override bool TryExecute(CelesFD_DialogueEngine engine)
        {
            var p = (CelesFD_DialogueActionCompProperties_Resource)props;
            var gc = CelesFD_GameComponent.Instance;
            if (gc == null) return false;

            int current = GetResource(gc, p.resource);

            // 1. 前置检查：checkNeed > 0 时资源不足即失败
            if (p.checkNeed > 0 && current < p.checkNeed)
            {
                // 发布清理（2026-08-25）：Log.Message($"[CelesFD] ResourceCheck FAIL: {p.resource}={current} < need={p.checkNeed}");
                return false;
            }

            // 2. 操作：元素名=操作，值=数量；无 Add/Set 即 None（仅检查）
            if (p.Add.HasValue)
                SetResource(gc, p.resource, current + p.Add.Value);
            else if (p.Set.HasValue)
                SetResource(gc, p.resource, p.Set.Value);

            // 发布清理（2026-08-25）：高频日志——Log.Message($"[CelesFD] ResourceComp: {p.resource} add={p.Add} set={p.Set} → {GetResource(gc, p.resource)}");
            return true;
        }

        // 2026-08-25 开局任务链：补偿"进入 node 即发"——entryComps 挂 Resource comp 时节点进入即执行
        // （TryExecute 复用；checkNeed 失败返回 false 无副作用——节点进入不检查返回值）
        public override void OnNodeEntered(CelesFD_DialogueEngine engine)
        {
            TryExecute(engine);
        }

        private static int GetResource(CelesFD_GameComponent gc, CelesFD_FDResourceType type) => type switch
        {
            CelesFD_FDResourceType.Credit => gc.Credit,
            CelesFD_FDResourceType.Key => gc.QuantumKey,
            CelesFD_FDResourceType.Fame => gc.Fame,
            CelesFD_FDResourceType.TradeVolume => gc.TradeVolume,
            _ => 0
        };

        // FD-G25（2026-09-02）：Set 语义经收敛 setter 以差值实现（原直写字段——D2 纪律违例第二处，与 G21 同批修）
        private static void SetResource(CelesFD_GameComponent gc, CelesFD_FDResourceType type, int value)
        {
            switch (type)
            {
                case CelesFD_FDResourceType.Credit: gc.ModifyCredit(value - gc.Credit); break;
                case CelesFD_FDResourceType.Key: gc.ModifyQuantumKey(value - gc.QuantumKey); break;
                case CelesFD_FDResourceType.Fame: gc.ModifyFame(value - gc.Fame); break;
                case CelesFD_FDResourceType.TradeVolume: gc.ModifyTradeVolume(value - gc.TradeVolume); break;
            }
        }
    }

    // ═══ 关闭发信器窗口 comp（2026-08-25）：挂断"结束"等效关闭页面（WindowStack 查找 CelesFD_Dialog_Comms → TryRemove） ═══
    public class CelesFD_DialogueActionCompProperties_CloseWindow : CelesFD_DialogueActionCompProperties
    {
        public CelesFD_DialogueActionCompProperties_CloseWindow() { compClass = typeof(CelesFD_DialogueActionComp_CloseWindow); }
    }

    public class CelesFD_DialogueActionComp_CloseWindow : CelesFD_DialogueActionComp
    {
        public override bool TryExecute(CelesFD_DialogueEngine engine)
        {
            foreach (Window w in Find.WindowStack.Windows)
                if (w is CelesFD_Dialog_Comms)
                {
                    Find.WindowStack.TryRemove(w);
                    // 发布清理（2026-08-25）：Log.Message("[CelesFD] Dialogue close-window action: comms window closed");
                    return true;
                }
            return true;   // 窗口不存在（防御）——不阻断选项流转
        }
    }

    // ═══ 对话变量置位 comp（2026-08-25 开局任务链）：entryComps/选项 comps 均可——
    //   进入节点（OnNodeEntered）或选择选项（TryExecute）时 Set 对话变量（如 ApologyViewed 防重复补偿）
    public class CelesFD_DialogueActionCompProperties_SetVariable : CelesFD_DialogueActionCompProperties
    {
        public string varName;
        public float Set;

        public CelesFD_DialogueActionCompProperties_SetVariable() { compClass = typeof(CelesFD_DialogueActionComp_SetVariable); }
    }

    public class CelesFD_DialogueActionComp_SetVariable : CelesFD_DialogueActionComp
    {
        public override bool TryExecute(CelesFD_DialogueEngine engine)
        {
            var p = (CelesFD_DialogueActionCompProperties_SetVariable)props;
            engine.SetVariable(p.varName, p.Set);
            return true;
        }

        public override void OnNodeEntered(CelesFD_DialogueEngine engine)
        {
            TryExecute(engine);
        }
    }

    // ═══ 测试：动态选项注入 + 插值验证（验证后可删；P1 由真实 Beacon_ListGenerator 替代） ═══
    public class CelesFD_DialogueActionCompProperties_TestList : CelesFD_DialogueActionCompProperties
    {
        public CelesFD_DialogueActionCompProperties_TestList() { compClass = typeof(CelesFD_DialogueActionComp_TestList); }
    }

    public class CelesFD_DialogueActionComp_TestList : CelesFD_DialogueActionComp
    {
        public override bool TryExecute(CelesFD_DialogueEngine engine) => true;   // entryComp 不用于选项动作

        public override void OnNodeEntered(CelesFD_DialogueEngine engine)
        {
            engine.SetVariable("beaconCount", 3f);
            engine.ClearDynamicOptions();
            for (int i = 0; i < 3; i++)
                engine.AddDynamicOption(new CelesFD_ResolvedOption
                {
                    Label = "测试信标站 " + (i + 1),
                    Next = "N_F2Test_Menu",
                    Sets = new System.Collections.Generic.List<CelesFD_VarOperationDef>
                    {
                        new CelesFD_VarOperationDef { varName = "beaconTarget", Set = i }
                    }
                });
        }
    }
}
