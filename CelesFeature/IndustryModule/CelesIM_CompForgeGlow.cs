using UnityEngine;
using Verse;

namespace CelesFeature
{
    // ════════════════════════════════════════════════════════════════
    //  锻造流光 comp（2026-10-16）：每建筑经 XML 指定自身 glow mote——
    //  解三炉共享单一硬编码 mote 之耦（Shaper 横扫窄带／Forger·Constructor 纵扫）
    //  · 数据 comp：无行为，AutoProducer 侧读取之（Props.glowMoteDef 空=无流光）
    //  · 生命周期/淡出语义不变（mote def 的 needsMaintenance 族配置）
    // ════════════════════════════════════════════════════════════════
    public class CelesIM_CompForgeGlow : ThingComp
    {
        public CelesIM_CompProperties_ForgeGlow Props => (CelesIM_CompProperties_ForgeGlow)props;
    }

    public class CelesIM_CompProperties_ForgeGlow : CompProperties
    {
        public ThingDef glowMoteDef;             // 该建筑流光 mote（空=无流光，安全默认）

        public Vector3 offset = Vector3.zero;    // 微调（默认零——位置由 mote 画布对齐决定）

        public float scale = 1f;                 // 缩放（默认 1）

        public CelesIM_CompProperties_ForgeGlow()
        {
            compClass = typeof(CelesIM_CompForgeGlow);
        }
    }
}
