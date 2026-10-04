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

        // ── 批 7 P2：设备连接状态行（族基一体——7 族设备共用；复用线程网络同款键）──
        // 形态：未连接 / 已连接: {目标} / 已连接: {N} 台（多目标 A 柜）；链接在但 !CanBeActive → 「（无效）」
        public override string CompInspectStringExtra()
        {
            int count = LinkedBuildings.Count;
            string status;
            if (count == 0)
            {
                status = "CelesIM_Keyed_StatusNotConnected".Translate();
            }
            else if (count == 1)
            {
                status = "CelesIM_Keyed_StatusConnected".Translate(LinkedBuildings[0].LabelCap);
            }
            else
            {
                status = "CelesIM_Keyed_DeviceLinkMulti".Translate(count);
            }
            if (count > 0 && !CanBeActive)
            {
                status += "CelesIM_Keyed_DeviceInactive".Translate();
            }
            return "CelesIM_Keyed_DeviceLinkStatus".Translate(status);
        }

        // ── 批 7 P3：占用×生效同门同步（60t 节流；无消费者直接返回）──
        // 消费者按线程网络自行接入、与设施链接互不知情 → 未连/失效仍占线程。本同步令
        // 「占用 ⟺ 生效路线在」：路线死→归还线程；路线活∧开关开→重接（TryConnect 自带手动开关守卫）。
        // 无循环依赖：路线活性读对端（中枢在线/协调器活性链），不读自身消费者。
        public override void CompTick()
        {
            base.CompTick();
            if (!parent.IsHashIntervalTick(60))
            {
                return;
            }
            CelesIM_CompThreadConsumer consumer = parent.GetComp<CelesIM_CompThreadConsumer>();
            if (consumer == null)
            {
                return;
            }
            bool before = CanBeActive;
            bool shouldConsume = RouteLiveAndLinked();
            if (shouldConsume && !consumer.IsConnected)
            {
                consumer.TryConnect();
            }
            else if (!shouldConsume && consumer.IsConnected)
            {
                consumer.Disconnect();
            }
            if (before != CanBeActive)
            {
                RefreshLinkedProducerPower();
            }
        }

        // 生效路线：有链接 ∧（中枢设备时）开关开 ∧ 任一路线活
        private bool RouteLiveAndLinked()
        {
            if (LinkedBuildings.Count == 0)
            {
                return false;
            }
            CelesIM_CompFacility_ProducerDevice device = this as CelesIM_CompFacility_ProducerDevice;
            if (device != null && !device.SwitchOn)
            {
                return false;
            }
            for (int i = 0; i < LinkedBuildings.Count; i++)
            {
                if (RouteLive(LinkedBuildings[i]))
                {
                    return true;
                }
            }
            return false;
        }

        // 路线活判据：中枢→在线 / 协调器→自身活性∧其直连中枢在线 / 终端侧目标→链接存在于即活
        private static bool RouteLive(Thing target)
        {
            CelesIM_CompThreadProducer producer = target.TryGetComp<CelesIM_CompThreadProducer>();
            if (producer != null)
            {
                return producer.IsOnline;
            }
            CompFacility route = target.TryGetComp<CompFacility>();
            if (route != null)
            {
                // 协调器（被视作中枢）：自身活性（开关/断电）∧ 其直连中枢在线
                if (!route.CanBeActive)
                {
                    return false;
                }
                for (int j = 0; j < route.LinkedBuildings.Count; j++)
                {
                    CelesIM_CompThreadProducer hub =
                        route.LinkedBuildings[j].TryGetComp<CelesIM_CompThreadProducer>();
                    if (hub != null && hub.IsOnline)
                    {
                        return true;
                    }
                }
                return false;
            }
            return true;   // 终端侧目标（A 柜/D 链终端）：无在线概念
        }

        // 关/开后即时刷新直连中枢耗电显示（挂协调器时经其上一层直达中枢；自 ProducerDevice 上移共用）
        public void RefreshLinkedProducerPower()
        {
            for (int i = 0; i < LinkedBuildings.Count; i++)
            {
                Thing linked = LinkedBuildings[i];
                CelesIM_CompProducerPower producerPower = linked.TryGetComp<CelesIM_CompProducerPower>();
                if (producerPower != null)
                {
                    producerPower.WritePower();
                    continue;
                }
                CompFacility up = linked.TryGetComp<CompFacility>();
                if (up == null)
                {
                    continue;
                }
                for (int j = 0; j < up.LinkedBuildings.Count; j++)
                {
                    up.LinkedBuildings[j].TryGetComp<CelesIM_CompProducerPower>()?.WritePower();
                }
            }
        }

        // ── 批 7 P6：选中协调器显示接纳范围（=中枢调整设备接纳半径，设备 def 单源静态缓存）──
        // 基类连线链保留（base）；仅协调器（producerPowerFactor > 1 标记）画环；默认环色（与放置环一致）
        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            CelesIM_AmplifierExt ext = parent.def.GetModExtension<CelesIM_AmplifierExt>();
            if (ext != null && ext.producerPowerFactor > 1f)
            {
                float radius = CelesIM_ProducerDeviceUtility.DeviceAcceptRadius;
                if (radius > 0f)
                {
                    GenDraw.DrawRadiusRing(parent.Position, radius);
                }
            }
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
        // 批 7：中枢电力负载（D46 先加后乘——电力链=StatPart_ProducerPowerOffset/Factor 消费）
        public float producerPowerOffset = 0f;    // W：中枢增幅器 +2000 / 平衡器 −1600 / 高级 +3600
        public float producerPowerFactor = 1f;    // 乘区基数：协调器 3；>1 即协调器标记（聚合遍历判据）
        public bool unlockOverclock = false;
        public List<string> unlockRecipeTags = null;
    }
}
