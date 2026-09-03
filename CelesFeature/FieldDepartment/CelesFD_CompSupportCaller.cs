using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // W-1：星铃支援呼叫器 comp（继承原版 CompApparelVerbOwner 复用 VerbTracker/ConstantCaster=Wearer/CanBeUsed）
    // 链路：gizmo 点击 → FloatMenu（可用/审批中支援列表，`武备xxx 1(2)次`）→ 选中 → BeginTargeting(verb)
    //   → 原版选点（范围环/LOS）→ 确认 → OrderForceTarget → Job（UseVerbOnThing）→ 前摇（Stance_Warmup + aimingLineMote）→ TryCastShot
    // 参照：原版许可菜单（Pawn_RoyaltyTracker.cs:1099-1111）+ gizmo 程序化 FloatMenu（Command_CallBossgroup.cs:100-106）
    public class CelesFD_CompSupportCaller : CompApparelVerbOwner
    {
        public CelesFD_SupportDef CurrentSupportDef;   // 本次施放选中的支援（FloatMenu 选中写入；TryCastShot 读取——同一次施放内无并发）

        // 已装备掉落（原版武器规则，Pawn_HealthTracker.cs:552-584：失去操作能力 → 主武器掉落）：
        // 呼叫器按同规则——穿戴者失去 Manipulation 能力时呼叫器掉落在地（不销毁，可被他人使用）
        // tickerType=Normal（ThingDef 已配）→ CompTick 每 tick 调用；250 tick 节流
        public override void CompTick()
        {
            base.CompTick();
            if (Find.TickManager.TicksGame % 250 != 0) return;
            Pawn wearer = Wearer;
            if (wearer == null || wearer.Dead) return;
            if (!wearer.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
                && wearer.SpawnedOrAnyParentSpawned && parent is Apparel ap && wearer.apparel != null)
            {
                wearer.apparel.TryDrop(ap, out _, wearer.PositionHeld, forbid: false);
                Log.Message("[CelesFD] Support caller dropped (wearer lost manipulation): " + wearer.LabelShort);
            }
        }

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            // 不调 base（禁用原版 Command_VerbOwner 单 verb gizmo）；自行出"呼叫支援"菜单 gizmo
            if (Wearer == null) yield break;
            RefreshVerbCaster();   // 关键修复：verb.caster 可能在穿戴前的 comps 惰性初始化中锁死为 null（Wear 内 CompBiocodable 检查触发），
                                   // 此处每次构建 gizmo 强制刷新为当前穿戴者（VerbTracker.cs:247 只设一次，SetupVerbs 仅 Initialize/PostLoadInit 重跑）
            bool drafted = Wearer.Drafted;
            if ((drafted && !Props.displayGizmoWhileDrafted) || (!drafted && !Props.displayGizmoWhileUndrafted))
                yield break;
            if (!Wearer.IsColonistPlayerControlled) yield break;

            Command_Action cmd = new Command_Action
            {
                defaultLabel = "CelesFD_Keyed_CallSupport".Translate(),
                defaultDesc = "CelesFD_Keyed_CallSupportDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("Things/Item/Equipment/WeaponRanged/EMPGrenades", false),   // 占位图标 = EMP 手榴弹装备形态贴图（用户定稿；W-3 换正式）
                action = OpenSupportMenu,
                hotKey = Props.hotKey,
            };
            // 禁用链（参考原版 CreateVerbTargetCommand 模式，VerbTracker.cs:105-135 / CompApparelVerbOwner.cs:113-125）：
            // 暴力禁用 / 无操作能力（1.6 无 Shooting 能力——原版"能暴力/能用工具"门槛 = Manipulation，PawnGenerator.cs:967 同款）→ gizmo 灰显
            if (Wearer.WorkTagIsDisabled(WorkTags.Violent))
            {
                cmd.Disable("IsIncapableOfViolenceLower".Translate(Wearer.LabelShort, Wearer).CapitalizeFirst() + ".");
                yield return cmd;
                yield break;
            }
            if (!Wearer.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
            {
                cmd.Disable("CelesFD_Keyed_SupportCallNoManipulation".Translate());
                yield return cmd;
                yield break;
            }
            string reason;
            if (!CanBeUsed(out reason)) cmd.Disable(reason);
            yield return cmd;
        }

        // gizmo 点击 → FloatMenu（§2.1：可用增援 floatmenu；显示 `武备xxx 1(2)次`；审批中 = 灰显不可选 + tooltip 提示审批中）
        private void OpenSupportMenu()
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            var options = new List<FloatMenuOption>();
            foreach (CelesFD_SupportDef def in DefDatabase<CelesFD_SupportDef>.AllDefsListForReading)
            {
                if (def.supportType != CelesFD_SupportType.CombatEquipment) continue;   // W-1 仅武备（人员随 R 系列）
                if (gc == null) continue;
                int avail = gc.AvailableCount(def);
                int pend = gc.PendingCount(def);
                if (avail <= 0 && pend <= 0) continue;   // 无可用也无审批中 → 不列出
                CelesFD_SupportDef localDef = def;
                FloatMenuOption option = new FloatMenuOption(
                    string.Format("{0} {1}({2})次", def.LabelCap, avail, pend),
                    () => StartTargeting(localDef));
                if (avail <= 0)
                {
                    option.Disabled = true;   // 仅审批中：显示最终数量但不可选（FloatMenuOption.cs:182-195）
                    option.tooltip = "CelesFD_Keyed_SupportPending".Translate();
                }
                options.Add(option);
            }
            if (options.Count == 0)
            {
                Messages.Message("CelesFD_Keyed_NoSupportAvailable".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            // W-1 先原版 FloatMenu（左上角锚定 + 分列/边界全自动）；左下角方案（FloatMenuGrid）批内最小验证后定（§5.1-1）
            Find.WindowStack.Add(new FloatMenu(options));
        }

        // 选中支援 → 落点选择（原版 Verb 即 ITargetingSource：DrawHighlight 范围环/LOS/确认链全自动，Verb.cs:11）
        private void StartTargeting(CelesFD_SupportDef def)
        {
            CurrentSupportDef = def;
            RefreshVerbCaster();   // 双保险：选点前再刷一次（防 gizmo 缓存/状态异常路径）
            Verb verb = VerbTracker.PrimaryVerb;
            if (verb == null)
            {
                Log.Error("[CelesFD] Support caller has no primary verb");
                return;
            }
            Find.Targeter.BeginTargeting(verb, null, false, null, null, true);
        }

        // 修复（2026-08-31）：verb.caster 刷新——Wear 内 CompBiocodable 检查（Pawn_ApparelTracker.cs:440）触发 comps 惰性初始化
        //   → comp.Initialize（ThingWithComps.cs:208）→ SetupVerbs → VerbTracker 创建（caster=Wearer=null，未穿戴）；
        //   InitVerb 仅设一次 caster（VerbTracker.cs:247），SetupVerbs 仅 Initialize/PostLoadInit 重跑 → 穿戴后 caster 锁死 null
        //   → Targeter.DrawHighlight（Verb.cs:678 caster.Position）NRE。此处每次 gizmo 构建/选点前强制刷新为当前穿戴者。
        private void RefreshVerbCaster()
        {
            Pawn wearer = Wearer;
            if (wearer == null) return;
            foreach (Verb v in VerbTracker.AllVerbs)
                v.caster = wearer;
        }
    }

    // 属性类：绑定 compClass + 复用原版 apparel 校验（thingClass 须 Apparel 子类，CompProperties_ApparelVerbOwner.cs:19-28）
    public class CelesFD_CompProperties_SupportCaller : CompProperties_ApparelVerbOwner
    {
        public CelesFD_CompProperties_SupportCaller()
        {
            compClass = typeof(CelesFD_CompSupportCaller);
        }
    }
}
