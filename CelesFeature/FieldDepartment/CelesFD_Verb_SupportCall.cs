using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace CelesFeature
{
    // W-1.5：支援呼叫信标施放 verb（继承 Verb_LaunchProjectile——投掷式，替代 W-1 的"施放即触发"）
    // 链路：选点确认 → Job（CelesFD_SupportCall）→ TryStartCastOn → Stance_Warmup 前摇 → WarmupComplete → TryCastShot
    //   → 发射投掷物（误差 = forcedMissRadius × 技能因子 × 距离系数）→ 投掷物落地 → Projectile_SupportCaller 检测+生成信标+触发
    // 技能因子（用户定稿 2026-08-31）：幂函数拟合——accuracy 0.5 → 4-5 格 / 1.0 → 1-1.5 格 / 2.0+ → <0.5 格
    // 实现模式（低侵入）：覆写 TryCastShot 精简版（纯子类零侵入；GetForcedMissTarget/GetForceMissFactorFor 均非 virtual 不可子类覆写）
    public class CelesFD_Verb_SupportCall : Verb_LaunchProjectile
    {
        // 覆写原版 Job 下发（Verb.cs:543-567）：改用自定义 JobDef（原地施放 + 正确文案 + 失败诊断，见 CelesFD_JobDriver_SupportCall）
        // 本次施放的支援绑定（Job 创建时刻写入——键 = job.loadID（原版 Job 唯一标识，JobDriver.cs:387 同模式）——
        // 每施放独立，防双投串号：verb 单值/JobDriver 启动读均会被排队中的后续选择覆盖，按 Job 键隔离则无竞态）
        // 边界：Job 入队未启动即取消（pawn 死亡/离场）→ Cleanup 未调 → 条目残留（异常路径低频、条目极小，可接受）
        // 读档中断施放 → Map 运行期不存档 → 空 → TryCastShot 兜底 → 投掷物快照 null → 落地安全失败（不扣错）
        public readonly Dictionary<int, CelesFD_SupportDef> CastSupportMap = new Dictionary<int, CelesFD_SupportDef>();

        public override void OrderForceTarget(LocalTargetInfo target)
        {
            if (verbProps.IsMeleeAttack)
            {
                base.OrderForceTarget(target);
                return;
            }
            Job job = JobMaker.MakeJob(CelesFD_DefOf.CelesFD_SupportCall, target);
            job.verbToUse = this;
            job.endIfCantShootInMelee = true;
            CastSupportMap[job.loadID] = GetCallerComp()?.CurrentSupportDef;   // Job 创建时刻绑定（选择确认时刻——后续选择不影响）
            CasterPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        // 选点期范围预览（用户裁决：点 gizmo→floatmenu 选中带范围支援→Targeter 选落点时鼠标跟随画圈）
        // Verb.DrawHighlight 虚方法（:676）——Targeter 每帧调用（:288 委托）
        public override void DrawHighlight(LocalTargetInfo target)
        {
            base.DrawHighlight(target);
            CelesFD_SupportDef def = GetCallerComp()?.CurrentSupportDef;
            if (def != null && def.previewRadius > 0f && target.IsValid && Find.CurrentMap != null)
                GenDraw.DrawRadiusRing(target.Cell, def.previewRadius);
        }

        // 投掷（复制原版 forcedMiss 分支 Verb_LaunchProjectile.cs:121-149 + 技能因子注入；省略 canGoWild/掩体分支——行为等价）
        // 数量检测/扣除移至投掷物落地（Projectile_SupportCaller.Impact——生效前扣除语义，用户裁决）
        protected override bool TryCastShot()
        {
            if (currentTarget.HasThing && currentTarget.Thing.Map != caster.Map)
                return false;
            ThingDef projectile = Projectile;
            if (projectile == null)
                return false;
            // FD-G18 修复（2026-09-02，原版 Verb_LaunchProjectile.cs:81-86 实证）：射击线失败仅在
            // verbProps.stopBurstWithoutLos 时中止（与原版完全同款）；本项目 XML 未设该标志——失败仍投掷
            //（投掷物飞行中被拦截走"生效前不扣"语义——Projectile_SupportCaller 不生成信标即不触发）
            if (!TryFindShootLineFromTo(caster.Position, currentTarget, out ShootLine resultingLine)
                && verbProps.stopBurstWithoutLos)
                return false;

            Vector3 drawPos = caster.DrawPos;
            Projectile projectile2 = (Projectile)GenSpawn.Spawn(projectile, resultingLine.Source, caster.Map);
            // 投掷时刻快照（防双投串号——2026-08-31：CurrentSupportDef 为 comp 当前选中，飞行期间被后续投掷覆盖则落地错配；
            //   每个投掷物绑定投掷时刻的支援——双投各自独立检测+扣除）
            if (projectile2 is CelesFD_Projectile_SupportCaller supportProjectile)
            {
                // 投掷物快照 = CastSupportMap 按 Job loadID 绑定（Job 创建时刻写入——选择确认时刻——前摇/排队中再选其他不影响）
                CelesFD_SupportDef castDef = null;
                if ((caster as Pawn)?.jobs?.curJob is Job curJob && curJob.verbToUse == this)
                    CastSupportMap.TryGetValue(curJob.loadID, out castDef);
                supportProjectile.SupportDefSnapshot = castDef ?? GetCallerComp()?.CurrentSupportDef;   // 兜底（异常路径安全失败）
                supportProjectile.OriginCellSnapshot = caster.Position;   // 投掷时刻位置快照（W-2a 方向源——与支援快照同纪律）
            }

            // forcedMiss 计算：基础误差 × 技能因子 × 距离系数（原版 VerbUtility.CalculateAdjustedForcedMiss）
            // 不用 verbProps.ForcedMissRadius（XML 字段）：原版 ConfigErrors 要求 forcedMiss 仅爆炸投射物可用
            // （VerbProperties.cs:706-709 CausesExplosion=thingClass 须 Projectile_Explosive——我们的投掷物非爆炸类），
            // 误差完全自定义：基础 1.9（原版 grenade 基线）[占位，未来 SupportDef XML 化]
            const float SupportMissRadiusBase = 1.9f;
            float num = SupportMissRadiusBase;
            if (caster is Pawn pawn)
            {
                num *= SkillFactor(pawn);   // ★ 技能因子（唯一技能影响点——原版投掷物全流程与技能零相关，用户需求注入）
            }
            float num2 = VerbUtility.CalculateAdjustedForcedMiss(num, currentTarget.Cell - caster.Position);
            if (num2 > 0.5f)
            {
                IntVec3 forcedMissTarget = GetForcedMissTarget(num2);
                if (forcedMissTarget != currentTarget.Cell)
                {
                    ProjectileHitFlags projectileHitFlags = ProjectileHitFlags.NonTargetWorld;
                    if (Rand.Chance(0.5f))
                        projectileHitFlags = ProjectileHitFlags.All;
                    if (!canHitNonTargetPawnsNow)
                        projectileHitFlags &= ~ProjectileHitFlags.NonTargetPawns;
                    projectile2.Launch(caster, drawPos, forcedMissTarget, currentTarget,
                        projectileHitFlags, preventFriendlyFire, EquipmentSource);
                    return true;
                }
            }
            // 直击（目标为地格——原版 :184-197 简化）
            ProjectileHitFlags flags = ProjectileHitFlags.IntendedTarget | ProjectileHitFlags.NonTargetWorld;
            projectile2.Launch(caster, drawPos, resultingLine.Dest, currentTarget,
                flags, preventFriendlyFire, EquipmentSource);
            return true;
        }

        // 技能因子（幂函数拟合，用户目标点）：accuracy = 最终射击精度 stat（技能 + Sight×12 + Manipulation×8 + 特质/健康 + 曲线）
        //   f = clamp(1.4 / max(acc, 0.1)^1.7, 0.35, 5.0)：acc 0.5 → 4.55（4-5 格目标）✓ / 1.0 → 1.40（1-1.5）✓ / 2.0 → 0.43（<0.5）✓
        private static float SkillFactor(Pawn pawn)
        {
            if (pawn == null) return 1f;
            float accuracy = pawn.GetStatValue(StatDefOf.ShootingAccuracyPawn);
            return Mathf.Clamp(1.4f / Mathf.Pow(Mathf.Max(accuracy, 0.1f), 1.7f), 0.35f, 5.0f);
        }

        // 找穿戴者的呼叫信标 comp（apparel 非 EquipmentSource 链，遍历 WornApparel——投掷时刻一次性，成本可忽略）
        private CelesFD_CompSupportCaller GetCallerComp()
        {
            Pawn p = caster as Pawn;
            if (p?.apparel == null) return null;
            foreach (Apparel a in p.apparel.WornApparel)
            {
                CelesFD_CompSupportCaller c = a.TryGetComp<CelesFD_CompSupportCaller>();
                if (c != null) return c;
            }
            return null;
        }
    }
}
