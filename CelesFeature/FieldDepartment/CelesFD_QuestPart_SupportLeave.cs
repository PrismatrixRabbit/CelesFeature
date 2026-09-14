using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace CelesFeature
{
    // QuestNode 包装器（XML 注入用——创建并配置 QuestPart_SupportLeave）
    public class CelesFD_QuestNode_SupportLeave : QuestNode
    {
        public RimWorld.QuestGen.SlateRef<IEnumerable<Pawn>> pawns;
        public RimWorld.QuestGen.SlateRef<string> supportDefName;
        public RimWorld.QuestGen.SlateRef<bool> isEarlyRecall;

        protected override bool TestRunInt(Slate slate) => true;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            var part = new CelesFD_QuestPart_SupportLeave
            {
                inSignal = QuestGen.slate.Get<string>("inSignal"),   // Bug1 修复：从 slate 上下文获取（Delay 的 outSignal——原版 Leave 同模式 :32）
                pawns = pawns.GetValue(slate)?.ToList() ?? new List<Pawn>(),
                supportDefName = supportDefName.GetValue(slate),
                isEarlyRecall = isEarlyRecall.GetValue(slate)
            };
            QuestGen.quest.AddPart(part);
        }
    }

    // ═══ R-2：离场序列宿主（替换 QuestNode_Leave——P11 终版时序）═══
    // 触发：inSignal（Delay 到期出信号 / 提前召回注入信号）
    // 序列（原子操作——同一 tick 完成）：
    //   ① 归还先行：先解绑 overseer（防脱离控制消息）→ SetFaction(BeaconFaction)
    //   ② 逐 pawn G9 传送（成功→即时结算分子 / 失败→LordJob_ExitMapBest 边缘+登记 PendingSettlement）
    //   ③ 即时结算（SettlePersonnel——GameComponent）
    //   ④ quest.End(Success, sendLetter: false)——letter 由结算侧发
    //   ⑤ order.phase = Done
    // Cleanup：空实现（全权由序列接管——原 LeaveOnCleanup 已删除）
    public class CelesFD_QuestPart_SupportLeave : QuestPart
    {
        public string inSignal;
        public List<Pawn> pawns = new List<Pawn>();
        public string supportDefName;
        public bool isEarlyRecall;    // 提前召回标记（letter 附加退款行）
        public bool isWiped;          // 全灭标记（letter 开场句"已确认全灭"——方案 B）
        public int recallRefundCredit;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag != QuestGenUtility.HardcodedSignalWithQuestID(inSignal)) return;
            ExecuteLeaveSequence();
        }

        // 公共方法（提前召回直接调用——绕过信号路由，Bug1 附带修复）
        public void ExecuteLeaveSequence()
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return;
            Faction beacon = CelesFD_BeaconUtility.BeaconFaction;
            if (beacon == null) { Log.Error("[CelesFD] SupportLeave: beacon faction missing"); return; }

            CelesFD_PersonnelOrder order = gc.PersonnelOrders.FirstOrDefault(
                o => o.supportDefName == supportDefName && o.phase != CelesFD_PersonnelOrder.Phase.Done
                     && o.boundQuestId == quest.id);
            CelesFD_SupportDef def = order?.Def;
            if (order == null || def == null)
            {
                Log.Warning("[CelesFD] SupportLeave: order/def not found for " + supportDefName);
                quest.End(QuestEndOutcome.Fail, sendLetter: false, playSound: false);
                return;
            }

            var alive = pawns.Where(p => p != null && !p.Dead && !p.Destroyed).ToList();
            float teleportedWeight = 0f;
            var edgePawns = new List<Pawn>();
            var pawnWeights = new Dictionary<string, float>();

            // ① 归还先行：先解绑 overseer → SetFaction（全员——IsColonist 不可区分临时加入 vs 招募，Bug2 修复：删除守卫）
            foreach (Pawn p in alive)
            {
                if (p.RaceProps.IsMechanoid)
                    p.GetOverseer()?.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, p);
                if (p.Faction != beacon) p.SetFaction(beacon);
            }

            // ② 逐 pawn 传送 / 边缘 / 被动追踪（三层覆盖——!Spawned 修复）
            foreach (Pawn p in alive)
            {
                float w = order.pawnWeights[Mathf.Min(pawns.IndexOf(p), order.pawnWeights.Count - 1)];
                pawnWeights[p.thingIDNumber.ToString()] = w;
                if (p.Spawned && CelesFD_PawnTeleport.TryTeleportOut(p))
                {
                    teleportedWeight += w;
                }
                else if (p.Spawned)
                {
                    // Spawned 但传送失败 → Lord 走边缘（quest 版参数——A2-4）
                    LordMaker.MakeNewLord(beacon,
                        new LordJob_ExitMapBest(LocomotionUrgency.Walk, canDig: true, canDefendSelf: true),
                        p.Map, new[] { p });
                    edgePawns.Add(p);
                }
                else
                {
                    // !Spawned（被背着/容器内）→ 不创建 Lord、不传送——加入 PendingSettlement 被动追踪
                    // GameComponent 250 tick 轮询发现恢复 Spawned 后再接走/走边缘
                    edgePawns.Add(p);
                }
            }

            // ③ 即时结算（含 letter 发送）——修5：调用 Settlement 静态类（非 gc 实例方法）
            CelesFD_Settlement.SettlePersonnel(gc, def, teleportedWeight, edgePawns, pawnWeights,
                order.completenessBaseline, isEarlyRecall, recallRefundCredit, isWiped);

            // ④ quest.End
            quest.End(QuestEndOutcome.Success, sendLetter: false, playSound: false);

            // ⑤ order Done
            order.phase = CelesFD_PersonnelOrder.Phase.Done;
            Log.Message("[CelesFD] SupportLeave complete: " + def.defName
                + " teleported=" + (alive.Count - edgePawns.Count) + " edge=" + edgePawns.Count);
        }

        public override void Cleanup()
        {
            // 空实现——序列已全权处理离场；LeaveOnCleanup 节点已从 XML 删除
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal", null);
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Reference);
            Scribe_Values.Look(ref supportDefName, "supportDefName", null);
            Scribe_Values.Look(ref isEarlyRecall, "isEarlyRecall", false);
            Scribe_Values.Look(ref isWiped, "isWiped", false);
            Scribe_Values.Look(ref recallRefundCredit, "recallRefundCredit", 0);
        }
    }
}
