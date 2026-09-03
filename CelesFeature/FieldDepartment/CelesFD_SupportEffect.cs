using System.Collections.Generic;
using RimWorld;
using Verse;

namespace CelesFeature
{
    // W-2a：支援效果触发上下文（信标 → 效果的单次传递）
    public class CelesFD_EffectContext
    {
        public CelesFD_SupportBeacon Beacon;
        public Map Map;
        public IntVec3 Cell;           // 落点（landingRule 已解析）
        public CelesFD_SupportDef SupportDef;
        public IntVec3 OriginCell;     // 投掷者投出时刻位置（方向源——W-2b 渐进弹幕/一字烟幕连线基准）
        public Pawn Caster;            // 投掷者（敌对判定基准——W-2b；裁决 2026-09-03：与投掷者派系敌对）

        public CelesFD_EffectContext(CelesFD_SupportBeacon beacon, IntVec3 cell)
        {
            Beacon = beacon;
            Map = beacon.Map;
            Cell = cell;
            SupportDef = beacon.SupportDef;
            OriginCell = beacon.originCell;
            Caster = beacon.caster;
        }

        // 效果生成的持续实体注册到信标（FD-G19：全部终结 → 信标销毁）
        public void RegisterController(Thing controller)
        {
            if (Beacon != null) Beacon.RegisterController(controller);
        }
    }

    // W-2a：支援效果基类——一个效果一个子类，参数全在子类字段（XML 可调）
    // XML 载体：<effect Class="CelesFeature.XxxEffect">（单字段 Class 节点——A2-9，
    //   QuestScriptDef.root 同构先例；扩展 Mod 子类化 + 写 XML 即注入，零注册）
    public abstract class CelesFD_SupportEffect
    {
        public abstract void Trigger(CelesFD_EffectContext ctx);
    }

    // 控制器剩余时间估算（Alert"正在进行：x秒"数据源）——W-2b 自建控制器实现；
    // 原版 Skyfaller 不实现此接口，由信标特判 ticksToImpact
    public interface CelesFD_IControllerTicksRemaining
    {
        int EstimatedTicksRemaining { get; }
    }

    // a-1 早期武备空投：复用坠落系统建筑容器链（W-2a 基础代码层）
    // 坠落物 = crateDef.fallerDef（Decelerate 减速姿态/贴图由 fallerDef 配置——A2-10 裁决：速度绑定 fallerDef）
    public class CelesFD_Effect_CrateDrop : CelesFD_SupportEffect
    {
        public CelesFD_DropCrateDef crateDef;

        public override void Trigger(CelesFD_EffectContext ctx)
        {
            if (crateDef == null || crateDef.fallerDef == null)
            {
                Log.Warning("[CelesFD] Effect_CrateDrop missing crateDef/fallerDef (support " + (ctx.SupportDef != null ? ctx.SupportDef.defName : "null") + ")");
                return;
            }
            // 内胆必须经 ThingMaker 生成（IncidentWorker_OrbitDrop.SpawnBuildingDrop 同款——
            //   裸 new ActiveTransporter 缺 def/SpawnSetup，Skyfaller Tick 访问 inner 时 NRE）
            ActiveTransporter info = (ActiveTransporter)ThingMaker.MakeThing(ThingDefOf.ActiveDropPod);
            Skyfaller faller = SkyfallerMaker.SpawnSkyfaller(crateDef.fallerDef, info, ctx.Cell, ctx.Map);
            ctx.RegisterController(faller);
        }
    }
}
