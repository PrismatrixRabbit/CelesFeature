using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // ════════════════════════════════════════════════════════════════
    //  批 7：中枢调整设备族基（D44 开关 + 单门 CanBeActive）
    //  · 开关字段 Scribe 存档；gizmo 暂用终端同款（键/图标占位——后续可配置化）
    //  · CanBeActive = 开关 ∧ base（断电）∧（挂消费者时）消费者可工作——
    //    所有下游（中枢读侧聚合/画线红白/:475 类活性门/乘区计数/嵌套路由）只看此单门
    //  · SetSwitch：关=设备整体惰性；若本机挂 ThreadConsumer（平衡器双身份）→
    //    同步 wantSwitchOn + Disconnect（归还线程）/TryConnect
    //  · 关后即时刷新直连中枢功率（WritePower 联动；托管链由 250t 节流兜底）
    // ════════════════════════════════════════════════════════════════
    public class CelesIM_CompFacility_ProducerDevice : CelesIM_CompFacility_Amplifier
    {
        private bool switchOn = true;

        public bool SwitchOn => switchOn;

        public override bool CanBeActive
        {
            get
            {
                if (!base.CanBeActive || !switchOn)
                {
                    return false;
                }
                CelesIM_CompThreadConsumer consumer = parent.GetComp<CelesIM_CompThreadConsumer>();
                return consumer == null || !consumer.CannotWorkUnconnected;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref switchOn, "switchOn", true);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }
            yield return new Command_Toggle
            {
                defaultLabel = "CelesIM_Keyed_ThreadSwitchLabel".Translate(),
                defaultDesc = "CelesIM_Keyed_ThreadSwitchDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("Celes/UI/Icons/ThreadConnect"),
                isActive = () => switchOn,
                toggleAction = delegate
                {
                    SetSwitch(!switchOn);
                }
            };
        }

        public void SetSwitch(bool on)
        {
            if (switchOn == on)
            {
                return;
            }
            switchOn = on;
            CelesIM_CompThreadConsumer consumer = parent.GetComp<CelesIM_CompThreadConsumer>();
            if (consumer != null)
            {
                consumer.SetSwitch(on);
            }
            RefreshLinkedProducerPower();   // 方法已上移族基（P3 共用）
        }
    }
}
