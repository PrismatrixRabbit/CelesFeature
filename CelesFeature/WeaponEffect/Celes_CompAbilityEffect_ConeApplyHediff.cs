using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // ────────────────────────────────────────────────────────────────
    // 星铃 · 武器线批1：定向扇形致盲（T2 拓荒霰弹技能）——自写 ability comp
    // 效果侧参照（设计 §4.9.5 v0.9 参考栈，已实证）：
    //   CompAbilityEffect_FireSpew：AffectedCells 扇区判定（顶点=施法者 /
    //   GenRadial.NumCellsInRadius(range) / 夹角过滤 / CanUseCell 射界 / Bresenham 补格）
    //   ＋ DrawEffectPreview(DrawFieldEdges)
    // 表现侧（2026-10-10 三轮定稿 · 照 DMS 闪光装置的做法与数值；零 DMS/Fortified 依赖）：
    //   闪光 = 施法者位置的加色辉光 Fleck（与 DMS 同款口径：位置=施法者、scale=120、
    //   贴图=Core ShotFlash、海拔=MoteOverhead）；两处有意偏离：0.05s 升 + 0.05s 降
    //   （同速对称，总长 ~0.1s）、暖白染色 (1,0.97,0.9)——均落在自建 FleckDef
    //   （Celes_Fleck_Flash）内；不设 verbProperties.muzzleFlashScale（避免双闪）；
    //   离屏由相机自然裁剪（无需视野判定）。
    // 与设计的三处差异：施加对象改为 hediff+partTag 逐部位 / LOS 起算=施法者 / 不筛阵营。
    // v2 增量（2026-10-10 终裁，设计 §4.10.7）：目标侧硬直 = 原版 Stun 伤害路径——
    //   stunDamage（XML 主口径 = Stun 伤害量，0=关）→ 原版受击链 StunFor；
    //   时长 = 本值 × 30（StunDurationTicksPerDamage）；伤害语义（externalViolence）
    //   = 「无差别闪」设计意图的一部分；不掉血/不流血/不致死。
    // ────────────────────────────────────────────────────────────────
    public class Celes_CompProperties_AbilityConeApplyHediff : CompProperties_AbilityEffect
    {
        // —— 施加（XML 定义）——
        public HediffDef hediffDef;                  // 致盲 hediff；null = 跳过致盲（stunDamage>0 时即纯硬直配置）
        public BodyPartTagDef bodypartTagTarget;     // null = 全部未缺失部位
        public float severityFixed;                  // >0 覆写严重度；<=0 用 hediff def 初始值
        public float chance = 1f;                    // 每次施法掷一次（整扇区同命）

        // —— 几何（照 DMS 量级）——
        public float range = 7.9f;                   // 扇形半径（顶点 = 施法者；DMS 同值）
        public float coneSizeDegrees = 10f;          // 扇面**半角**（±，度；DMS 同值）

        // —— 闪光（照 DMS 做法；包络/颜色写在 FleckDef 内，不在此）——
        public FleckDef flashFleckDef;               // null → 运行时回退 FleckDefOf.ShotFlash（DMS 原味）
        public float flashScale = 120f;              // 辉光尺寸（照 DMS；<=0 不闪）

        // —— 目标侧硬直（v2 增量：原版 Stun 伤害路径，设计 §4.10.7 终裁）——
        public float stunDamage = 0f;                // 0 = 关；>0 = Stun 伤害量（XML 主口径，与 DMS 字面对应）
                                                     // 时长 = 本值 × 30（原版 StunDurationTicksPerDamage）；2.0 = 1.0s

        public Celes_CompProperties_AbilityConeApplyHediff()
        {
            compClass = typeof(Celes_CompAbilityEffect_ConeApplyHediff);
        }
    }

    public class Celes_CompAbilityEffect_ConeApplyHediff : CompAbilityEffect
    {
        private readonly List<IntVec3> tmpCells = new List<IntVec3>();
        private readonly HashSet<Pawn> tmpPawns = new HashSet<Pawn>();
        private readonly List<Pawn> tmpPawnList = new List<Pawn>();

        public new Celes_CompProperties_AbilityConeApplyHediff Props =>
            (Celes_CompProperties_AbilityConeApplyHediff)props;

        private Pawn Caster => parent.pawn;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            SpawnFlash();        // 表现先于判定：闪光是施法动作自身的光，不随 chance 掷骰结果变化
            ApplyCone(target);
            base.Apply(target, dest);
        }

        public override void DrawEffectPreview(LocalTargetInfo target)
        {
            GenDraw.DrawFieldEdges(AffectedCells(target));
        }

        // —— 闪光：施法者位置生成加色辉光 Fleck（照 DMS 做法——位置=施法者、scale=120）——
        private void SpawnFlash()
        {
            if (Props.flashScale <= 0f)
            {
                return;
            }
            Pawn caster = Caster;
            Map map = caster?.Map;
            if (map == null)
            {
                return;
            }
            // 运行时回退：不在 CompProperties 字段初始化期引用 DefOf（def 加载顺序不可靠）
            FleckDef fleckDef = Props.flashFleckDef ?? FleckDefOf.ShotFlash;
            FleckMaker.Static(caster.Position, map, fleckDef, Props.flashScale);
        }

        // —— 效果结算（照 FireSpew 结构；差异 = 逐部位 hediff + LOS 起算施法者 + 不筛阵营）——
        private void ApplyCone(LocalTargetInfo target)
        {
            Celes_CompProperties_AbilityConeApplyHediff p = Props;
            if (p.hediffDef == null && p.stunDamage <= 0f)
            {
                return;
            }
            if (!Rand.Chance(p.chance))
            {
                return;
            }
            Pawn caster = Caster;
            Map map = caster?.Map;
            if (map == null)
            {
                return;
            }
            List<IntVec3> cells = AffectedCells(target);
            if (cells.Count == 0)
            {
                return;
            }
            tmpPawns.Clear();
            tmpPawnList.Clear();
            for (int i = 0; i < cells.Count; i++)
            {
                List<Thing> thingList = cells[i].GetThingList(map);
                for (int j = 0; j < thingList.Count; j++)
                {
                    if (thingList[j] is Pawn pawn && !pawn.Dead && tmpPawns.Add(pawn))
                    {
                        tmpPawnList.Add(pawn);
                    }
                }
            }
            for (int k = 0; k < tmpPawnList.Count; k++)
            {
                Pawn pawn = tmpPawnList[k];
                if (!GenSight.LineOfSight(caster.Position, pawn.Position, map, skipFirstCell: true))
                {
                    continue;   // 墙挡（开着的门不挡）
                }
                // 硬直（先于致盲：观感"先闪停、再致盲"）——原版 Stun 伤害路径：
                //   伤害量 = stunDamage（XML 主口径）→ 时长 = 本值 × 30
                //   （换算先例 DamageWorker_Blunt.cs:73-74 → 受击链 StunFor）
                if (p.stunDamage > 0f)
                {
                    pawn.TakeDamage(new DamageInfo(DamageDefOf.Stun, p.stunDamage, instigator: caster));
                }
                if (p.hediffDef != null)
                {
                    foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts(
                        BodyPartHeight.Undefined, BodyPartDepth.Undefined, p.bodypartTagTarget))
                    {
                        Hediff hediff = HediffMaker.MakeHediff(p.hediffDef, pawn, part);
                        if (p.severityFixed > 0f)
                        {
                            hediff.Severity = p.severityFixed;
                        }
                        pawn.health.AddHediff(hediff, part);
                    }
                }
            }
        }

        // —— 扇区判定（照 FireSpew.AffectedCells；差异 = 半角直取 coneSizeDegrees）——
        private List<IntVec3> AffectedCells(LocalTargetInfo target)
        {
            Celes_CompProperties_AbilityConeApplyHediff p = Props;
            Pawn caster = Caster;
            tmpCells.Clear();
            Map map = caster?.Map;
            if (map == null)
            {
                return tmpCells;
            }
            Vector3 casterVec = caster.Position.ToVector3Shifted().Yto0();
            IntVec3 aim = target.Cell.ClampInsideMap(map);
            if (caster.Position == aim)
            {
                return tmpCells;
            }
            float distToAim = (aim - caster.Position).LengthHorizontal;
            if (distToAim <= 0.001f)
            {
                return tmpCells;
            }
            float dirX = (float)(aim.x - caster.Position.x) / distToAim;
            float dirZ = (float)(aim.z - caster.Position.z) / distToAim;
            IntVec3 endpoint = default(IntVec3);
            endpoint.x = Mathf.RoundToInt(caster.Position.x + dirX * p.range);
            endpoint.z = Mathf.RoundToInt(caster.Position.z + dirZ * p.range);
            float aimAngle = Vector3.SignedAngle(endpoint.ToVector3Shifted().Yto0() - casterVec, Vector3.right, Vector3.up);
            int numCells = GenRadial.NumCellsInRadius(p.range);
            for (int i = 0; i < numCells; i++)
            {
                IntVec3 c = caster.Position + GenRadial.RadialPattern[i];
                if (!CanUseCell(c))
                {
                    continue;
                }
                float cellAngle = Vector3.SignedAngle(c.ToVector3Shifted().Yto0() - casterVec, Vector3.right, Vector3.up);
                if (Mathf.Abs(Mathf.DeltaAngle(cellAngle, aimAngle)) > p.coneSizeDegrees)
                {
                    continue;
                }
                tmpCells.Add(c);
            }
            List<IntVec3> lineCells = GenSight.BresenhamCellsBetween(caster.Position, endpoint);
            for (int j = 0; j < lineCells.Count; j++)
            {
                if (!tmpCells.Contains(lineCells[j]) && CanUseCell(lineCells[j]))
                {
                    tmpCells.Add(lineCells[j]);
                }
            }
            return tmpCells;

            bool CanUseCell(IntVec3 c)
            {
                if (!c.InBounds(map))
                {
                    return false;
                }
                if (c == caster.Position)
                {
                    return false;
                }
                if (!c.InHorDistOf(caster.Position, p.range))
                {
                    return false;
                }
                ShootLine shootLine;
                return parent.verb.TryFindShootLineFromTo(caster.Position, c, out shootLine);
            }
        }
    }
}
