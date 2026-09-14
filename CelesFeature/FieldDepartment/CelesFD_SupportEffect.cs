using System.Collections.Generic;
using Verse;

namespace CelesFeature
{
    // W-2a：支援效果触发上下文（信标 → 效果的单次传递）
    // W-5 签名升级：单格 → 格集（landingRule 统一返回 List；Cell 保留为 Cells[0] 只读别名——
    //   全仓零写入方[实证]，单落点效果[TrackingBeam 等]零改动兼容）
    public class CelesFD_EffectContext
    {
        public CelesFD_SupportBeacon Beacon;
        public Map Map;
        public List<IntVec3> Cells;        // 落点集（landingRule 已解析；信标保证非空）
        public IntVec3 Cell => Cells[0];   // 兼容别名（首个落点）
        public CelesFD_SupportDef SupportDef;
        public IntVec3 OriginCell;     // 投掷者投出时刻位置（方向源——W-2b 连线基准）
        public Pawn Caster;            // 投掷者（敌对判定基准——W-2b；裁决 2026-09-03：与投掷者派系敌对）

        public CelesFD_EffectContext(CelesFD_SupportBeacon beacon, List<IntVec3> cells)
        {
            Beacon = beacon;
            Map = beacon.Map;
            Cells = cells;
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
    // W-5 效果收口：Drop（即刻投放）+ Barrage（排程投放）+ TrackingBeam（跟踪）三种
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
}
