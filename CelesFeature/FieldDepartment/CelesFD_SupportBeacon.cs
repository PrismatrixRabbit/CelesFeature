using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // W-2a：落地支援信标——支援进行中枢（阶段机取代 W-1.5 固定 300 tick 自毁；FD-G19 完成信号）
    //   Waiting：arrivalDelayTicks 比较式倒计时（Alert"即将抵达：x秒"）→ 到期进 Active
    //   Active：landingRule 解析落点 → effect.Trigger(ctx)；效果持续实体注册进 controllers
    //           （Alert"正在进行：x秒"）；全部控制器终结 → Destroy（信标+光柱一体移除）
    // 存档：SupportDef/originCell/caster/startTick/phase/controllers 全 Scribe；读档光柱重启（无限时长）
    public class CelesFD_SupportBeacon : ThingWithComps   // ThingWithComps——GetComp<T>() 所需（Thing 无此方法）
    {
        public enum BeaconPhase { Waiting, Active }

        public CelesFD_SupportDef SupportDef;   // 光束颜色/延迟/效果来源（Projectile 生成时设置）
        public IntVec3 originCell;              // 投掷者投出时刻位置（方向源——W-2b 弹幕/烟幕连线基准）
        public Pawn caster;                     // 投掷者（敌对判定基准——W-2b；裁决：与投掷者派系敌对）
        public BeaconPhase phase = BeaconPhase.Waiting;
        public List<Thing> controllers = new List<Thing>();   // 效果持续实体（G19 终结判定；LookMode.Reference）

        private int startTick;
        public int activeStartTick;             // Active 起点秒表（正在进行 = 正计时——用户裁决 2026-09-03；Alert 数据源）
        private const int FadeOutTicks = 60;    // 光柱淡出段（无限时长下仅视觉参数）

        public int ArrivalDelayTicks
        {
            get { return SupportDef != null ? SupportDef.arrivalDelayTicks : 300; }
        }

        public int WaitingTicksLeft
        {
            get { return Mathf.Max(0, ArrivalDelayTicks - (Find.TickManager.TicksGame - startTick)); }
        }

        // "正在进行"经过时间（正计时：效果触发时刻起累计，持续至控制器终结/信标销毁）
        public int OngoingTicksElapsed
        {
            get { return Find.TickManager.TicksGame - activeStartTick; }
        }

        // "正在进行"剩余：控制器剩余最大值（Skyfaller → ticksToImpact 原生字段；
        // 自建控制器 → CelesFD_IControllerTicksRemaining；均无 → -1 = 不显示）
        public int OngoingTicksLeft()
        {
            int best = -1;
            controllers.RemoveAll(t => t == null || t.Destroyed);
            foreach (Thing t in controllers)
            {
                int r = -1;
                if (t is Skyfaller sf) r = sf.ticksToImpact;
                else if (t is CelesFD_IControllerTicksRemaining est) r = est.EstimatedTicksRemaining;
                if (r > best) best = r;
            }
            return best;
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                startTick = Find.TickManager.TicksGame;
            }
            // 光柱无限时长（duration<=0 = 不淡出不终止——信标销毁即移除；读档也重启，视觉从当前时刻恢复）
            GetComp<CelesFD_CompSupportBeam>()?.StartAnimation(0, FadeOutTicks);
            // 注册表（Alert 数据源；[Unsaved]——信标本体随地图存档，注册表运行期由 Spawn/Destroy 维护）
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc != null && !gc.SupportBeacons.Contains(this)) gc.SupportBeacons.Add(this);
            CelesFD_SupportAlertSlots.Assign(this);   // W-2b：Alert 槽位池绑定（>6 并发不显示）
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            base.Destroy(mode);
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc != null) gc.SupportBeacons.Remove(this);
            CelesFD_SupportAlertSlots.Release(this);   // W-2b：槽位释放
        }

        protected override void Tick()   // 基类 Thing.Tick 为 protected（Thing.cs:322）——访问级别须一致
        {
            base.Tick();
            if (SupportDef == null)
            {
                Destroy();
                return;
            }
            if (phase == BeaconPhase.Waiting)
            {
                // 比较式倒计时（dev 快进兼容——Bombardment 模式）
                if (Find.TickManager.TicksGame - startTick >= ArrivalDelayTicks)
                {
                    phase = BeaconPhase.Active;
                    TryTriggerEffect();
                    // W-4（2026-09-04 生命周期解耦）：无正在进行计时 → 触发即销毁
                    //（效果实体[TrackingBeam/Skyfaller/Controller]独立存活，不依赖信标监护）
                    if (!SupportDef.hasOngoingTimer)
                    {
                        Destroy();
                        return;
                    }
                }
            }
            else
            {
                controllers.RemoveAll(t => t == null || t.Destroyed);
                if (controllers.Count == 0)
                {
                    Destroy();
                    Log.Message("[CelesFD] Support beacon removed (support finished): " + SupportDef.defName);
                }
            }
        }

        // 到期触发：解析落点集 → 分发效果（效果内 RegisterController；无控制器 = 瞬发 → 下 tick 完成）
        // W-5：单格 → 格集（landingRule.TryResolveCells）；null 规则 = 默认就近 [Position]（旧语义）
        private void TryTriggerEffect()
        {
            activeStartTick = Find.TickManager.TicksGame;   // 正在进行的正计时起点（Alert）
            List<IntVec3> cells = null;
            if (SupportDef.landingRule != null &&
                !SupportDef.landingRule.TryResolveCells(Position, originCell, Map, out cells))
            {
                Log.Warning("[CelesFD] Landing rule failed to resolve cells (support " + SupportDef.defName + ") — finishing");
                return;
            }
            if (cells.NullOrEmpty()) cells = new List<IntVec3> { Position };
            if (SupportDef.effect != null)
                SupportDef.effect.Trigger(new CelesFD_EffectContext(this, cells));
            else
                Log.Warning("[CelesFD] Support def has no effect: " + SupportDef.defName);
        }

        public void RegisterController(Thing controller)
        {
            if (controller != null) controllers.Add(controller);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref SupportDef, "SupportDef");
            Scribe_Values.Look(ref originCell, "originCell");
            Scribe_References.Look(ref caster, "caster");
            Scribe_Values.Look(ref startTick, "startTick", 0);
            Scribe_Values.Look(ref activeStartTick, "activeStartTick", 0);
            int phaseInt = (int)phase;
            Scribe_Values.Look(ref phaseInt, "phase", 0);
            phase = (BeaconPhase)phaseInt;
            Scribe_Collections.Look(ref controllers, "controllers", LookMode.Reference);
        }
    }
}
