using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace CelesFeature
{
    // W-2b 终版（三轮需求合并）：跟踪光束——狙击瞄准线与跟随激光的单一载体
    //   跟随侧：初次锁定圈 initialLockRadius（信标为心，取最大 BodySize 敌对——caster 派系基准）→
    //     锁定后跟随出圈不限距；目标丢失 → 转火圈同值半径（光束当前格为心——用户确认）；
    //     连续移动（travelPos 插值，速度上限 followSpeed 格/s）；狙击跟丢 lostGiveUpTicks 放弃（弹窗）
    //   造伤侧（XML 驱动，可组合）：
    //     周期块（激光）：每 intervalTicks 于当前位置 r=damageRadius 全体 TakeDamage（无敌我识别；
    //       待机原地也造伤——用户裁决；移动路径天然覆盖）；攻击判定屋顶三态：无顶→造伤 /
    //       薄顶→本判仅拆当前格顶（下判起造伤）/ 厚顶→不拆不伤（末段渲染同步熄灭）
    //     末刻块（狙击）：duration 前一 tick 判定：无目标→放弃（弹窗）/ 目标厚顶→放弃（弹窗）/
    //       目标薄顶→仅拆目标格顶不造伤 / 无顶→弹丸自目标上空射至当前格
    //   渲染：两段自绘（主段 + 末段 endSegmentLength 仅可攻击时渲染——视觉表明可攻击性）；
    //     材质借用原版 OrbitalBeam（更细 width）；范围圈（待机锁定圈 / 激光伤害圈，可开关）
    public class CelesFD_TrackingBeamExtension : DefModExtension
    {
        public float initialLockRadius = 10f;     // 初次锁定圈 = 转火圈（用户裁决 r10）
        public float followSpeed = 30f;           // 跟随速度上限（格/秒；狙击 30 / 激光 4）
        public int lostGiveUpTicks = 120;         // 狙击跟丢时限（2s；0 = 不放弃——激光用 0）
        // 周期伤害块（激光）
        public int intervalTicks = 30;            // 0.5s（勘误修正：60 tick/s）
        public DamageDef damageDef;
        public int damageAmount = 23;
        public float armorPenetration = 2f;       // 穿甲（DamageInfo 直传——破甲问题根治）
        public float damageRadius = 1.4f;
        // 末刻射弹块（狙击）
        public ThingDef fireProjectileDef;
        public float fireOriginHeight = 40f;       // 弹丸起点 Z 轴偏移（+40 = 屏幕下方远距；40 格/speed 95 ≈ 25 tick 可见飞行）
        // 渲染
        public float beamWidth = 0.2f;            // 细束（原版 OrbitalBeam 材质，宽 0.2 对比原版 8）
        public Color beamColor = new Color(1f, 0.78f, 0.08f, 0.71f);   // 亮橙黄（激光默认；狙击 def 覆写红）
        public float endSegmentLength = 1.1f;     // 末段长（用户裁决 ~1 格；仅可攻击时渲染；BeamEndMat 端盖）
        public float glowScale = 0f;              // 光圈 Mote_PowerBeam 缩放（最终尺寸 = Scale × drawSize(1,1)——
                                                  //   Graphic_Mote.cs:30-32 实证；0=无光圈；激光 1.5）
        public SoundDef beamSound;                // 束音效（Sustainer 常驻——原版 OrbitalBeam 同款；自管；狙击不配）
    }

    [StaticConstructorOnStartup]
    public class CelesFD_Thing_TrackingBeam : OrbitalStrike
    {
        private static readonly Material BeamMat =
            MaterialPool.MatFrom("Other/OrbitalBeam", ShaderDatabase.MoteGlow, MapMaterialRenderQueues.OrbitalBeam);
        private static readonly Material BeamEndMat =
            MaterialPool.MatFrom("Other/OrbitalBeamEnd", ShaderDatabase.MoteGlow, MapMaterialRenderQueues.OrbitalBeam);
        private static readonly MaterialPropertyBlock MPB = new MaterialPropertyBlock();

        private const int FadeOutTicks = 60;

        public Pawn target;
        public Faction casterFaction;    // 敌对判定基准（与投掷者派系敌对，空兜底玩家）
        public IntVec3 beaconCell;       // 初次锁定圈心 / 弹丸起点（效果触发时写入）
        public Vector3 travelPos;        // 连续位置（速度上限驱动；Position = 其取整）
        private int nextDamageTick;
        private bool fired;
        private int lostTicks;           // 狙击跟丢累计
        private float beamAngle;         // 斜拉角（±12° 随机——原版轨道束同款视觉语言）
        private bool everLocked;         // 初次锁定（false=仍以信标为心搜索）
        private Sustainer beamSustainer; // 束音效（自管——原版 CompOrbitalBeam :109-118 同模式）
        private Mote glowMote;           // 光圈 Mote_PowerBeam（径向辉光 mote——原版 PowerBeam.cs:23 同款；
                                         //   屋顶三态门控：无顶生成/维持，有顶销毁；不存档[纯视觉，读档下 tick 重生]）

        private CelesFD_TrackingBeamExtension Ext
        {
            get { return def.GetModExtension<CelesFD_TrackingBeamExtension>(); }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                travelPos = DrawPos;
                beamAngle = Rand.Range(-12f, 12f);
            }
        }

        // 三轮修复（NRE 根治）：OrbitalStrike.StartStrike :55 对 CompOrbitalBeam 无空安全——
        //   def 已恢复该 comp（满足 base 调用），覆写中随即将其动画掐灭（1 tick 熄灭，视觉由两段自绘接管）；
        //   音效自管 Sustainer（comp 被掐灭后其 sound 不会常驻）
        public override void StartStrike()
        {
            base.StartStrike();   // 计时/AffectsSky/comp 动画（comp 存在——不再 NRE）
            CompOrbitalBeam comp = GetComp<CompOrbitalBeam>();
            if (comp != null) comp.StartAnimation(1, 1, beamAngle);   // 掐灭自绘
            CelesFD_TrackingBeamExtension ext = Ext;
            if (ext != null && ext.beamSound != null)
            {
                LongEventHandler.ExecuteWhenFinished(delegate
                {
                    beamSustainer = ext.beamSound.TrySpawnSustainer(SoundInfo.InMap(this, MaintenanceType.PerTick));
                });
            }
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (beamSustainer != null)
            {
                beamSustainer.End();
                beamSustainer = null;
            }
            // 光圈 mote：显式销毁（此前仅置 null = 泄漏——六轮修复）
            if (glowMote != null && !glowMote.Destroyed)
                glowMote.Destroy();
            glowMote = null;
            base.Destroy(mode);
        }

        protected override void Tick()
        {
            CelesFD_TrackingBeamExtension ext = Ext;
            if (ext != null)
            {
                int now = Find.TickManager.TicksGame;
                // ── 跟随侧 ──
                if (target == null || target.Dead || !target.Spawned)
                {
                    bool retarget = everLocked;
                    target = FindTarget(ext, retarget);   // 转火圈 = 当前格为心（用户确认）；初次 = 信标为心
                    if (target != null) everLocked = true;
                }
                if (target != null && target.Spawned)
                {
                    // 速度上限连续移动（格/tick = 格每秒 / 60）
                    Vector3 dest = target.DrawPos;
                    Vector3 delta = dest - travelPos;
                    delta.y = 0f;
                    float maxStep = ext.followSpeed / 60f;
                    if (delta.magnitude > maxStep) delta = delta.normalized * maxStep;
                    travelPos += delta;
                    travelPos.y = 0f;
                    IntVec3 cell = travelPos.ToIntVec3();
                    if (cell != Position && cell.InBounds(Map)) Position = cell;
                    // 狙击跟丢判定：距离超过 1 秒航程持续累计（时限到 → 放弃+弹窗）
                    if (ext.fireProjectileDef != null && ext.lostGiveUpTicks > 0)
                    {
                        float dist = (dest - travelPos).Yto0().magnitude;
                        if (dist > ext.followSpeed) lostTicks++;
                        else lostTicks = 0;
                        if (lostTicks >= ext.lostGiveUpTicks)
                        {
                            GiveUp("CelesFD_Keyed_SniperFailLost");
                            return;
                        }
                    }
                }
                // ── 激光周期块（待机原地也造伤；屋顶三态在判定内） ──
                if (ext.damageDef != null && ext.damageAmount > 0 && ext.damageRadius > 0f
                    && now >= nextDamageTick)
                {
                    nextDamageTick = now + ext.intervalTicks;
                    RoofDef roof = Position.GetRoof(Map);
                    if (roof == null)
                        ApplyPeriodicDamage(ext);
                    else if (!roof.isThickRoof)
                        Map.roofGrid.SetRoof(Position, null);   // 薄顶：本判仅拆顶（下判起造伤）
                    // 厚顶：不拆不伤（末段渲染同步熄灭——见 DrawAt）
                }
                // ── 狙击末刻块（duration 前一 tick） ──
                if (!fired && ext.fireProjectileDef != null && TicksPassed >= duration - 1)
                {
                    fired = true;
                    FireOrCancel(ext);
                }
                // ── 光圈 Mote_PowerBeam 管理（屋顶三态门控） ──
                // 原版 PowerBeam.cs:23 MoteMaker.MakePowerBeamMote 同款（Scale/rotationRate）
                UpdateGlowMote(ext);
            }
            base.Tick();   // OrbitalStrike：TicksPassed >= duration → Destroy
            if (beamSustainer != null)
            {
                beamSustainer.Maintain();   // 音效常驻（原版 comp :64-76 同模式）
                if (TicksLeft < FadeOutTicks) { beamSustainer.End(); beamSustainer = null; }
            }
        }

        // 最大体型敌对（caster 派系基准；初次=信标为心 r10，转火=当前格为心 r10）
        private Pawn FindTarget(CelesFD_TrackingBeamExtension ext, bool retarget)
        {
            Faction fac = casterFaction != null ? casterFaction : Faction.OfPlayer;
            IntVec3 center = retarget ? Position : beaconCell;
            Pawn best = null;
            foreach (Pawn p in Map.mapPawns.AllPawnsSpawned)
            {
                if (p.Dead || !p.Spawned) continue;
                if (!p.HostileTo(fac)) continue;
                if (!p.Position.InHorDistOf(center, ext.initialLockRadius)) continue;
                if (best == null || p.BodySize > best.BodySize) best = p;
            }
            return best;
        }

        private void ApplyPeriodicDamage(CelesFD_TrackingBeamExtension ext)
        {
            foreach (IntVec3 c in GenRadial.RadialCellsAround(Position, ext.damageRadius, useCenter: true))
            {
                foreach (Thing t in c.GetThingList(Map).ToList())
                {
                    if (t == null || t == this || t.Destroyed) continue;
                    t.TakeDamage(new DamageInfo(ext.damageDef, ext.damageAmount, ext.armorPenetration, -1f, this));
                }
            }
        }

        // 狙击末刻三态：无目标/厚顶→放弃（弹窗）；薄顶→仅拆目标格顶（不造伤不射弹）；无顶→自上空射弹
        private void FireOrCancel(CelesFD_TrackingBeamExtension ext)
        {
            if (target == null || target.Dead || !target.Spawned)
            {
                GiveUp("CelesFD_Keyed_SniperFailNoTarget");
                return;
            }
            RoofDef roof = target.Position.GetRoof(Map);
            if (roof != null && roof.isThickRoof)
            {
                GiveUp("CelesFD_Keyed_SniperFailRoof");
                return;
            }
            if (roof != null)
            {
                // 薄顶（用户裁决：同样渲染弹丸 + 抵达时拆顶+穿顶音效，不造伤）：
                //   弹丸目标 = 地格（非 pawn——Projectile.Impact(null) 仅视觉命中，无伤害）；
                //   音效 = roof.soundPunchThrough（空投舱穿顶同款——Skyfaller.cs:412-414 原版）
                Projectile roofProj = (Projectile)GenSpawn.Spawn(ext.fireProjectileDef, target.Position, Map);
                Vector3 roofOrigin = target.DrawPos + new Vector3(0f, 0f, ext.fireOriginHeight);
                roofProj.Launch(this, roofOrigin, new LocalTargetInfo(target.Position), new LocalTargetInfo(target.Position),
                    ProjectileHitFlags.IntendedTarget, preventFriendlyFire: false);
                Map.roofGrid.SetRoof(target.Position, null);
                if (!roof.soundPunchThrough.NullOrUndefined())
                    roof.soundPunchThrough.PlayOneShot(new TargetInfo(target.Position, Map));
                return;
            }
            Projectile proj = (Projectile)GenSpawn.Spawn(ext.fireProjectileDef, target.Position, Map);
            // 固定 Z 轴 +40 远起点（用户裁决：非随机方向——Z 高→Z 低 = 屏幕上自下而上的可见弹道；
            //   40 格 → speed 95 → ~25 tick 可见飞行——Projectile.cs:88 3D 距离实证）
            Vector3 origin = target.DrawPos + new Vector3(0f, 0f, ext.fireOriginHeight);
            proj.Launch(this, origin, new LocalTargetInfo(target), new LocalTargetInfo(target),
                ProjectileHitFlags.IntendedTarget, preventFriendlyFire: false);
        }

        private void GiveUp(string reasonKey)
        {
            Messages.Message("CelesFD_Keyed_SupportFailedSniper".Translate(reasonKey.Translate()),
                MessageTypeDefOf.NeutralEvent, historical: false);
            Destroy();
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)   // 基类 protected（OrbitalStrike.cs:40）——访问级别须一致
        {
            base.DrawAt(drawLoc, flip);
            CelesFD_TrackingBeamExtension ext = Ext;
            if (ext == null || Map == null) return;
            // 两段束（复刻 CompOrbitalBeam.cs:78-107 绘制语言——材质借用、更细、斜拉角）
            // 束长 = 图边公式同款（CompOrbitalBeam :84：(地图 z − 落点 z) × 1.414——三轮修复：原常量 15 过短）
            Vector3 pos = travelPos;
            Vector3 dir = Vector3Utility.FromAngleFlat(beamAngle - 90f);   // 束自天空方向（同原版 angle 语义）
            float beamLength = ((float)Map.Size.z - pos.z) * 1.4142135f;
            float fadeIn = Mathf.Min((float)TicksPassed / 10f, 1f);
            float alpha = 0.975f + Mathf.Sin((float)TicksPassed * 0.3f) * 0.025f;
            if (TicksLeft < FadeOutTicks) alpha *= (float)TicksLeft / FadeOutTicks;
            Color color = ext.beamColor;
            color.a *= alpha;
            MPB.SetColor(ShaderPropertyIDs.Color, color);
            float y = AltitudeLayer.MetaOverlays.AltitudeFor();
            // 可攻击性：末段渲染条件 = 攻击落点（狙击=目标格 / 激光=当前格）无顶
            bool endVisible = AttackTargetCell(ext).GetRoof(Map) == null;
            float mainLen = endVisible ? beamLength - ext.endSegmentLength : beamLength;
            // 束体：主段 BeamMat（束体材质）+ 末段 BeamEndMat（端盖径向辉光——原版 CompOrbitalBeam :100/:105 两种材质）
            DrawBeamSegment(pos + dir * ext.endSegmentLength, dir, mainLen, ext.beamWidth, y, fadeIn, main: true);
            if (endVisible)
                DrawBeamSegment(pos, dir, ext.endSegmentLength, ext.beamWidth, y, fadeIn, main: false);
            // 光圈 = Mote_PowerBeam（独立 mote 实体，非束体绘制件——见 UpdateGlowMote；原版 PowerBeam.cs:23）
        }

        // 光圈 Mote_PowerBeam 管理（每 tick 调用——屋顶三态门控 + 跟随移动 + Maintain 续命）
        // 原版：MoteMaker.MakePowerBeamMote :261-268（Scale + rotationRate + exactPosition + Spawn）
        private void UpdateGlowMote(CelesFD_TrackingBeamExtension ext)
        {
            if (ext.glowScale <= 0f) return;
            RoofDef roof = Position.GetRoof(Map);
            if (roof == null)
            {
                // 无顶：确保存在 → 跟随 → 续命
                if (glowMote == null || glowMote.Destroyed)
                {
                    glowMote = (Mote)ThingMaker.MakeThing(ThingDefOf.Mote_PowerBeam);
                    glowMote.exactPosition = travelPos;
                    glowMote.Scale = ext.glowScale;           // 最终尺寸 = Scale × drawSize(1,1)——Graphic_Mote.cs:30-32
                    glowMote.rotationRate = 1.2f;              // 原版 MoteMaker :266 同值
                    GenSpawn.Spawn(glowMote, Position, Map);
                }
                else
                {
                    glowMote.exactPosition = travelPos;        // 跟随光束移动
                }
                glowMote.Maintain();                           // 续命（Mote.cs:242——防止寿命到期消失）
            }
            else
            {
                // 有顶：销毁（下 tick 无顶时重生）
                if (glowMote != null && !glowMote.Destroyed)
                {
                    glowMote.Destroy();
                    glowMote = null;
                }
            }
        }

        private IntVec3 AttackTargetCell(CelesFD_TrackingBeamExtension ext)
        {
            if (ext.fireProjectileDef != null && target != null && target.Spawned) return target.Position;
            return Position;
        }

        private void DrawBeamSegment(Vector3 anchor, Vector3 dir, float length, float width, float y, float fadeIn, bool main)
        {
            Vector3 mid = anchor + dir * (length * 0.5f);
            mid.y = y;
            Vector3 offset = dir * ((1f - fadeIn) * length);   // 淡入：自天空方向"打来"
            Matrix4x4 matrix = default(Matrix4x4);
            matrix.SetTRS(mid + offset, Quaternion.Euler(0f, beamAngle, 0f), new Vector3(width, 1f, length));
            Graphics.DrawMesh(MeshPool.plane10, matrix, main ? BeamMat : BeamEndMat, 0, null, 0, MPB);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref target, "CFD_target");
            Scribe_References.Look(ref casterFaction, "CFD_casterFaction");
            Scribe_Values.Look(ref beaconCell, "CFD_beaconCell");
            Scribe_Values.Look(ref travelPos, "CFD_travelPos");
            Scribe_Values.Look(ref nextDamageTick, "CFD_nextDamageTick", 0);
            Scribe_Values.Look(ref fired, "CFD_fired", false);
            Scribe_Values.Look(ref lostTicks, "CFD_lostTicks", 0);
            Scribe_Values.Look(ref beamAngle, "CFD_beamAngle", 0f);
            Scribe_Values.Look(ref everLocked, "CFD_everLocked", false);
        }
    }

    // 狙击/激光共用效果：spawn beam → 写入派系/信标格/时长 → StartStrike → 注册信标
    public class CelesFD_Effect_TrackingBeam : CelesFD_SupportEffect
    {
        public ThingDef beamDef;
        public int durationTicks = 120;   // 狙击瞄准 2s；激光 1020（17s——勘误修正）

        public override void Trigger(CelesFD_EffectContext ctx)
        {
            if (beamDef == null || ctx.Map == null)
            {
                Log.Warning("[CelesFD] Effect_TrackingBeam missing beamDef (support " +
                    (ctx.SupportDef != null ? ctx.SupportDef.defName : "null") + ")");
                return;
            }
            CelesFD_Thing_TrackingBeam beam = (CelesFD_Thing_TrackingBeam)ThingMaker.MakeThing(beamDef);
            GenSpawn.Spawn(beam, ctx.Cell, ctx.Map);
            beam.casterFaction = ctx.Caster != null ? ctx.Caster.Faction : Faction.OfPlayer;
            beam.beaconCell = ctx.Cell;
            beam.duration = durationTicks;
            beam.StartStrike();   // 启动计时 + CompAffectsSky 光圈（激光 def 挂载时——原版 StartStrike :54 驱动）
            ctx.RegisterController(beam);
        }
    }
}
