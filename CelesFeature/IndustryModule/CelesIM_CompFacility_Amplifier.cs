using System.Collections.Generic;
using RimWorld;
using Verse;

namespace CelesFeature
{
    // ════════════════════════════════════════════════════════════════
    // 批 6：增幅器 facility 族（2026-10-03 · 审计批准版）
    // 连接上限两轴（用户修正：BCAD 四类增幅器均需 XML 可配）：
    //   轴 1「最大连接数」（受影响侧：同一目标最多链 N 个本 def 设施）= 原版 maxSimultaneous
    //        直接复用（CompProperties_Facility.cs:13；受影响侧计数截断 :377-385，近者优先劣者顶替）
    //   轴 2「最大可连接对象」（设施侧：每台设施最多服务 N 个目标）= 原版无此字段
    //        （两侧 Props 字段面全量实证）→ 本族自建：maxLinkedTargets + CanLink 覆写
    //        （挂点=CompFacility.CanLink virtual :109，受影响侧两处过滤 :47/:373 均走它；
    //        链接由受影响侧发起、同 tick 顺序扫描非并发，建立时过滤即充分）
    // ════════════════════════════════════════════════════════════════
    public class CelesIM_CompProperties_AmplifierFacility : CompProperties_Facility
    {
        public int maxLinkedTargets = -1;   // 轴 2：每台设施最多服务目标数；-1=不限制（默认）

        public CelesIM_CompProperties_AmplifierFacility()
        {
            compClass = typeof(CelesIM_CompFacility_Amplifier);
        }
    }

    public class CelesIM_CompFacility_Amplifier : CompFacility
    {
        public CelesIM_CompProperties_AmplifierFacility AmpProps =>
            (CelesIM_CompProperties_AmplifierFacility)props;

        public override bool CanLink()
        {
            if (!base.CanLink())
            {
                return false;
            }
            return AmpProps.maxLinkedTargets < 0 || LinkedBuildings.Count < AmpProps.maxLinkedTargets;
        }
    }

    // ════════════════════════════════════════════════════════════════
    // 批 6 A：工具柜型增幅器（「需线程不耗电」· D37/D42）
    // 原版 CanBeActive 唯一失活条件=CompPowerTrader 断电（无电力 comp 恒 active）——
    // 本子类补线程条件：挂 ThreadConsumer 且依赖型未连接=失活（statOffsets 即不注入）。
    // 加成链/白红线/链接顶替/两轴上限全走原版框架+本族，零额外自建。
    // ════════════════════════════════════════════════════════════════
    public class CelesIM_CompFacility_Threaded : CelesIM_CompFacility_Amplifier
    {
        public override bool CanBeActive
        {
            get
            {
                if (!base.CanBeActive)
                {
                    return false;
                }
                CelesIM_CompThreadConsumer consumer = parent?.GetComp<CelesIM_CompThreadConsumer>();
                return consumer == null || !consumer.CannotWorkUnconnected;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    // 批 6：增幅器通用薄 ext（B/C/D 三类增幅器 def 携带——字段按需组合，零值=该轴不生效）
    //   B 中枢增幅器：boostPerUnit（每台增中枢线程产出——非 stat，TotalCapacity 现算直显）
    //   C 超频器：unlockOverclock（链接即解锁终端超频 Toggle）
    //   D 解锁增幅器：unlockRecipeTags（⊇ 配方 lockedTags 方可显示/挂单）
    // ════════════════════════════════════════════════════════════════
    public class CelesIM_AmplifierExt : DefModExtension
    {
        public int boostPerUnit = 0;
        public bool unlockOverclock = false;
        public List<string> unlockRecipeTags = null;
    }
}
