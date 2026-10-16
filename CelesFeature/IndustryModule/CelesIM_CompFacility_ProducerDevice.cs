using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

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

        // 手动切换的宿主偏好（0=自动；宿主 thingIDNumber，随存档——2026-10-17）
        private int preferredHostId;

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
            Scribe_Values.Look(ref preferredHostId, "preferredHostId", 0);
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
            // ── 重置线程连接（2026-10-17）：手动在多个宿主（中枢/协调器）间切换 ──
            // 复用中继同款键与图标；仅单宿主设备（maxLinkedTargets==1）显示；≤1 候选时静默无变化
            if (AmpProps.maxLinkedTargets == 1)
            {
                yield return new Command_Action
                {
                    defaultLabel = "CelesIM_Keyed_ResetConnection".Translate(),
                    defaultDesc = "CelesIM_Keyed_ResetConnectionDescRelay".Translate(),
                    icon = ContentFinder<Texture2D>.Get("Celes/UI/Icons/TryThreadConnect"),
                    action = delegate { SwitchHost(); }
                };
            }
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

        // ════════════════════════════════════════════════════════════
        //  重置线程连接 / 宿主切换（2026-10-17·用户裁决）
        //  · 候选=可链宿主（Props.linkableBuildings 反扫生成；affected.CanLinkTo 过滤
        //    ——与原版 RelinkAll 同款）→ 距离升序 → thingID 升序（稳定环序）
        //  · 拆建全走原版 public 成对 API：
        //    拆 my.Notify_LinkRemoved(host) ＋ host.affected.Notify_LinkRemoved(parent)
        //    建 my.Notify_NewLink(host)    ＋ host.affected.Notify_NewLink(parent)
        //  · 60t 校正：偏好已生效→跳过；未链且仍可连→回链；失效→清 0 回退自动
        //    （防原版自动重链覆盖手动选择）
        // ════════════════════════════════════════════════════════════
        public void SwitchHost()
        {
            if (parent == null || parent.Map == null)
            {
                return;
            }
            List<Thing> candidates = ValidHosts();
            if (candidates.Count <= 1)
            {
                return;   // 无其他可选宿主——静默（自显示）
            }
            Thing current = LinkedBuildings.Count > 0 ? LinkedBuildings[0] : null;
            int idx = current != null ? candidates.IndexOf(current) : -1;
            Thing next = candidates[(idx + 1) % candidates.Count];
            if (next == current)
            {
                return;
            }
            preferredHostId = next.thingIDNumber;
            UnlinkHost(current);
            LinkHost(next);
            RefreshLinkedProducerPower();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            for (int i = 0; i < 3; i++)
            {
                FleckMaker.ThrowMetaPuff(next.DrawPos, parent.Map);
            }
        }

        private List<Thing> ValidHosts()
        {
            List<Thing> result = new List<Thing>();
            List<ThingDef> defs = Props.linkableBuildings;
            if (defs == null)
            {
                return result;
            }
            for (int i = 0; i < defs.Count; i++)
            {
                if (defs[i] == null)
                {
                    continue;
                }
                foreach (Thing thing in parent.Map.listerThings.ThingsOfDef(defs[i]))
                {
                    CompAffectedByFacilities affected = thing.TryGetComp<CompAffectedByFacilities>();
                    if (affected != null && affected.CanLinkTo(parent))
                    {
                        result.Add(thing);
                    }
                }
            }
            result.Sort(delegate (Thing a, Thing b)
            {
                float da = (a.Position - parent.Position).LengthHorizontalSquared;
                float db = (b.Position - parent.Position).LengthHorizontalSquared;
                if (da != db)
                {
                    return da.CompareTo(db);
                }
                return a.thingIDNumber.CompareTo(b.thingIDNumber);
            });
            return result;
        }

        private void UnlinkHost(Thing host)
        {
            if (host == null)
            {
                return;
            }
            Notify_LinkRemoved(host);
            host.TryGetComp<CompAffectedByFacilities>()?.Notify_LinkRemoved(parent);
        }

        private void LinkHost(Thing host)
        {
            Notify_NewLink(host);
            host.TryGetComp<CompAffectedByFacilities>()?.Notify_NewLink(parent);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (preferredHostId == 0 || !parent.IsHashIntervalTick(60))
            {
                return;
            }
            for (int i = 0; i < LinkedBuildings.Count; i++)
            {
                if (LinkedBuildings[i] != null && LinkedBuildings[i].thingIDNumber == preferredHostId)
                {
                    return;   // 偏好已生效
                }
            }
            List<Thing> candidates = ValidHosts();
            Thing preferred = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].thingIDNumber == preferredHostId)
                {
                    preferred = candidates[i];
                    break;
                }
            }
            if (preferred == null)
            {
                preferredHostId = 0;   // 偏好失效（被拆/越界/失活）→ 回退自动
                return;
            }
            List<Thing> toUnlink = new List<Thing>(LinkedBuildings);
            for (int i = 0; i < toUnlink.Count; i++)
            {
                UnlinkHost(toUnlink[i]);
            }
            LinkHost(preferred);
            RefreshLinkedProducerPower();
        }
    }
}
