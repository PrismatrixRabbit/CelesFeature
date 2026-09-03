using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace CelesFeature
{
    // W-1：支援呼叫 JobDriver（仿 JobDriver_CastVerbOnceStatic——原地施放，不走位；呼叫支援语义）
    // 修复（2026-08-31）：
    //   ① Job 文案："攻击区域"（原版 UseVerbOnThing 的 UsingVerb 文案）→ 自定义 GetReport"正在呼叫支援：{支援名}。"
    //   ② 发呆无反馈：原版 CastVerb toil 忽略 TryStartCastOn 返回值（Toils_Combat.cs:89）——失败时 Job 秒结束无提示；
    //      自定义 CastSupportVerb toil 增加失败诊断日志（定位失败点）
    public class CelesFD_JobDriver_SupportCall : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        public override string GetReport()
        {
            Verb v = job.verbToUse;
            CelesFD_CompSupportCaller caller = (v?.caster as Pawn)?.apparel?.WornApparel
                .Select(a => a.TryGetComp<CelesFD_CompSupportCaller>()).FirstOrDefault(c => c != null);
            string name = caller?.CurrentSupportDef?.LabelCap ?? v?.ReportLabel ?? "?";
            return "CelesFD_Keyed_CallingSupport".Translate(name);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_General.StopDead();
            yield return CelesFD_Toils.CastSupportVerb(TargetIndex.A);
        }
    }

    // 施法 toil（带失败诊断）：TryStartCastOn 失败时输出失败点（caster 状态/LOS/state），替代原版静默失败
    public static class CelesFD_Toils
    {
        public static Toil CastSupportVerb(TargetIndex targetInd)
        {
            Toil toil = ToilMaker.MakeToil("CastSupportVerb");
            toil.initAction = delegate
            {
                LocalTargetInfo target = toil.actor.jobs.curJob.GetTarget(targetInd);
                Verb verb = toil.actor.jobs.curJob.verbToUse;
                bool ok = verb.TryStartCastOn(target, LocalTargetInfo.Invalid, false, true, false, false);
                if (!ok)
                {
                    Log.Message("[CelesFD] TryStartCastOn failed: verb=" + (verb != null ? verb.GetUniqueLoadID() : "NULL")
                        + " caster=" + (verb?.caster != null ? verb.caster.ToString() : "NULL")
                        + " spawned=" + (verb?.caster != null ? verb.caster.Spawned.ToString() : "-")
                        + " canHit=" + (verb != null ? verb.CanHitTarget(target).ToString() : "-")
                        + " state=" + (verb != null ? verb.state.ToString() : "-")
                        + " warmup=" + (verb != null ? verb.verbProps.warmupTime.ToString() : "-"));
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.FinishedBusy;
            // Job/toil 结束清理（Toil.Cleanup → finishActions，Toil.cs:62-72——toil 完成与 Job 中断（JobDriver.cs:289）均调用）：
            // 移除 CastSupportMap 条目（防泄漏——投掷物快照已独立携带，施放结束后 Map 无用途）
            toil.finishActions = new List<Action>
            {
                delegate
                {
                    (toil.actor.jobs.curJob.verbToUse as CelesFD_Verb_SupportCall)?.CastSupportMap.Remove(toil.actor.jobs.curJob.loadID);
                }
            };
            return toil;
        }
    }
}
