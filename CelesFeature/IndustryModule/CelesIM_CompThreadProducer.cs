using System.Collections.Generic;
using Verse;
using RimWorld;
using UnityEngine;

namespace CelesFeature
{
    public class CelesIM_CompProperties_ThreadProducer : CompProperties
    {
        public int baseThreadsProduce = 8;
        public float connectRadius = 24.9f;
        public int overloadCount = 7500;
        // ── E+F：自定义中枢故障（原版 CompBreakdownable 禁用——维修资源 XML 可调，Q-F1(a) 单种多数量）──
        public ThingDef faultRepairResource;   // 维修所需资源（null = 无配置，故障不可修——配置校验用）
        public int faultRepairCount = 1;       // 资源数量
        public int faultRepairWorkTicks = 1000;// 维修工作时长（原版 FixBrokenDownBuilding 同值默认）
        public string indicatorTexPath = "";
        public Vector2 indicatorDrawSize = new Vector2(1f, 1f);
        public float indicatorVerticalOffset = 0f;
        public CelesIM_CompProperties_ThreadProducer()
        {
            compClass = typeof(CelesIM_CompThreadProducer);
        }
    }

    // 中枢（E+F 批 2026-09-13：干扰存在数化[管理器重算] + 自定义中枢故障子系统 + 抑制算法）
    //   故障：唯一来源 = 超载倒计时归零；维修 = CelesIM_JobDriver_FixHubFault（资源/时长 XML 配置）
    //   抑制算法（完整版见批1执行要点）：
    //     置位唯一路径 = 维修完成且当时无电；消费唯一路径 = TryAutoWake（上电/干扰恢复两入口）；
    //     新超载周期防御性清位；拆除重建不跨实体；两字段随档
    public class CelesIM_CompThreadProducer : ThingComp, CelesIM_IThreadNode
    {
        public CelesIM_CompProperties_ThreadProducer Props => (CelesIM_CompProperties_ThreadProducer)props;
        [Unsaved] private CompPowerTrader powerComp;
        [Unsaved] private bool isShutdownByInterference;  // E：存在数派生（管理器重导出，不入档）
        private bool faultActive;                         // F：中枢故障态（存档）
        private bool suppressPending;                     // F：「当次跳过自动唤醒」未消费标记（存档）
        private int overloadTimer;
        private int capacityOffset;
        [Unsaved] private int devCapacityOffset;

        private CelesIM_ThreadNetworkManager Mgr =>
            parent == null || parent.Map == null ? null : CelesIM_ThreadNetworkManager.For(parent.Map);

        // ── Public API ──
        public bool IsShutdownByInterference => isShutdownByInterference;
        public bool FaultActive => faultActive;
        public int OverloadTimer => overloadTimer;
        Color CelesIM_IThreadNode.PipColor => ColorLibrary.Yellow;
        string CelesIM_IThreadNode.GizmoLabel => "CelesIM_Keyed_GizmoThreads".Translate();
        public int TotalCapacity =>
            (isShutdownByInterference || faultActive) ? 0 : Mathf.Max(0, Props.baseThreadsProduce + capacityOffset + devCapacityOffset);
        public int CurrentLoad => Mgr != null ? Mgr.LoadOf(parent) : 0;
        // 批 2：无信号段负载（Gizmo 嵌套格数据源——IThreadNode）
        public int NoSignalLoad
        {
            get
            {
                CelesIM_ThreadNetworkManager mgr = Mgr;
                if (mgr == null) return 0;
                mgr.LoadOfSplit(parent, out _, out int noSignal);
                return noSignal;
            }
        }
        public float LoadRate =>
            TotalCapacity > 0 ? (float)CurrentLoad / TotalCapacity : 0f;
        public bool IsOverloaded => CurrentLoad > TotalCapacity;
        private bool HasPower => powerComp?.PowerOn ?? true;
        public bool IsOnline =>
            HasPower && !isShutdownByInterference && !faultActive;
        public bool IsInRange(IntVec3 targetPos)
        {
            return parent.Position.InHorDistOf(targetPos, Props.connectRadius);
        }
        public bool CanAcceptConnection(int threadCost)
        {
            return IsOnline && CurrentLoad + threadCost <= TotalCapacity;
        }
        public void ModifyCapacityOffset(int delta)
        {
            capacityOffset += delta;
        }

        // E：干扰旗标——管理器唯一写入方（RecalcInterference）
        internal void SetInterferenceShutdown(bool value)
        {
            isShutdownByInterference = value;
        }

        // ── F：抑制算法 ──
        // 一切自动唤醒的唯一出口：消费当次抑制（置 false 且跳过），否则唤醒范围内无绑定者
        public void TryAutoWake()
        {
            if (suppressPending)
            {
                suppressPending = false;
                return;
            }
            WakeNearbyDisconnectedConsumers();
        }

        // 事件 2：维修完成（JobDriver 终结成功分支 / DEV 按钮）
        public void Notify_FaultRepaired()
        {
            if (!faultActive) return;
            faultActive = false;
            if (!HasPower)
                suppressPending = true;   // 故障期断电：待下一自动唤醒点消费
            // 有电分支：修复即「当次」消费点——不唤醒、不置位
        }

        // ============================================================
        //  F-07: 超载倒计时 → 自定义中枢故障（事件 1）
        // ============================================================
        protected virtual void OnOverloadBreakdown()
        {
            faultActive = true;
            suppressPending = false;   // 新周期防御性清位（抑制不跨故障周期）
            Mgr?.SeverSubtree(parent); // 全子树解绑 + 预热清零，不走迁移重试路径
        }

        // ============================================================
        //  F-06: 中枢干扰（E 批：存在数口径——重算在管理器 RecalcInterference）
        // ============================================================
        private void WakeNearbyDisconnectedConsumers()
        {
            CelesIM_ThreadNetworkManager mgr = Mgr;
            if (mgr == null)
                return;
            // I 批：数据源 = 管理器注册表（替代全图扫描）——判定语义不变
            List<CelesIM_CompThreadConsumer> consumers = mgr.ConsumersForReading;
            for (int i = 0; i < consumers.Count; i++)
            {
                CelesIM_CompThreadConsumer consumer = consumers[i];
                if (consumer == null || consumer.parent == null || !IsInRange(consumer.parent.Position))
                    continue;
                if (!consumer.IsConnected)
                    consumer.TryConnect();
            }
            List<CelesIM_CompThreadRelay> relays = mgr.RelaysForReading;
            for (int i = 0; i < relays.Count; i++)
            {
                CelesIM_CompThreadRelay relay = relays[i];
                if (relay == null || relay.parent == null || !IsInRange(relay.parent.Position))
                    continue;
                if (!relay.IsConnectedToParent)
                    relay.TryConnectToParent();
            }
        }
        // ============================================================
        //  F-09: 选中连接线渲染（子级清单走管理器反向索引；线色按消费者派生活跃态）
        // ============================================================
        public override void PostDrawExtraSelectionOverlays()
        {
            List<Thing> children = Mgr?.GetDirectChildren(parent);
            if (children != null)
            {
                for (int i = 0; i < children.Count; i++)
                {
                    Thing thing = children[i];
                    if (thing == null)
                        continue;

                    CelesIM_CompThreadConsumer consumer = thing.TryGetComp<CelesIM_CompThreadConsumer>();
                    if (consumer != null)
                    {
                        if (consumer.IsActiveConnection)
                            GenDraw.DrawLineBetween(parent.TrueCenter(), thing.TrueCenter());
                        else
                            GenDraw.DrawLineBetween(parent.TrueCenter(), thing.TrueCenter(),
                                CompAffectedByFacilities.InactiveFacilityLineMat);
                        continue;
                    }

                    CelesIM_CompThreadRelay relay = thing.TryGetComp<CelesIM_CompThreadRelay>();
                    if (relay != null)
                    {
                        if (relay.IsStandby)
                            GenDraw.DrawLineBetween(parent.TrueCenter(), thing.TrueCenter(),
                                CompAffectedByFacilities.InactiveFacilityLineMat);
                        else
                            GenDraw.DrawLineBetween(parent.TrueCenter(), thing.TrueCenter());
                        relay.DrawSubtreeRangeRings();
                        relay.DrawSubtreeRecursive();
                    }
                }
            }
            GenDraw.DrawRadiusRing(parent.Position, Props.connectRadius);
        }
        // ============================================================
        //  F-04: 跨 Thing 信号（ECS 阶段1：统一转发管理器单入口，comp 不自持逻辑）
        // ============================================================
        public override void ReceiveCompSignal(string signal)
        {
            switch (signal)
            {
                case "PowerTurnedOn":
                    Mgr?.Notify_PowerChanged(parent, true);
                    break;
                case "PowerTurnedOff":
                    Mgr?.Notify_PowerChanged(parent, false);
                    break;
            }
        }
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            // 子级级联（解绑+迁移重试）与干扰重算由管理器 Deregister 处理
            CelesIM_ThreadNetworkManager mgr = map != null ? CelesIM_ThreadNetworkManager.For(map) : null;
            mgr?.Notify_HubDespawned(this);
            base.PostDeSpawn(map, mode);
        }
        // ============================================================
        //  F9-b: 方向性特效（E10：批 3 删除整段）
        // ============================================================
        [Unsaved] private Material indicatorMat;
        [Unsaved] private bool indicatorMatTried;
        [Unsaved] private Material overloadMat;
        [Unsaved] private bool overloadMatTried;
        public override void PostDraw()
        {
            // ── 过载/故障图标：有电 + 过载或故障 = 脉动 ──
            if (powerComp == null || powerComp.PowerOn)
            {
                if (IsOverloaded || faultActive)
                {
                    if (!overloadMatTried)
                    {
                        overloadMat = MaterialPool.MatFrom("Celes/UI/Icons/OverThreadConnect", ShaderDatabase.Transparent);
                        overloadMatTried = true;
                    }
                    if (overloadMat != null)
                    {
                        float alpha = Mathf.Lerp(0.35f, 0.95f, Mathf.PingPong(Time.realtimeSinceStartup * 1.0f, 1f));
                        overloadMat.SetColor("_Color", new Color(1f, 1f, 1f, alpha));
                        Vector3 overloadPos = parent.DrawPos;
                        overloadPos.y += 0.3f;
                        Graphics.DrawMesh(MeshPool.plane10,
                            Matrix4x4.TRS(overloadPos, Quaternion.identity, new Vector3(0.8f, 1f, 0.8f)),
                            overloadMat, 0);
                    }
                }
            }

            // ── 方向特效：仅在线时绘制（子级清单走管理器）──
            if (!IsOnline)
                return;
            if (Props.indicatorTexPath.NullOrEmpty())
                return;
            List<Thing> children = Mgr?.GetDirectChildren(parent);
            if (children == null || children.Count == 0)
                return;
            if (!indicatorMatTried)
            {
                indicatorMat = MaterialPool.MatFrom(Props.indicatorTexPath, ShaderDatabase.Transparent);
                indicatorMatTried = true;
            }
            if (indicatorMat == null)
                return;
            Vector3 prodCenter = parent.DrawPos;
            prodCenter.z += Props.indicatorVerticalOffset;
            for (int i = 0; i < children.Count; i++)
            {
                Thing thing = children[i];
                if (thing == null)
                    continue;
                if (thing.TryGetComp<CelesIM_CompThreadConsumer>() == null && thing.TryGetComp<CelesIM_CompThreadRelay>() == null)
                    continue;
                Vector3 consCenter = thing.DrawPos;
                Vector3 dir = consCenter - prodCenter;
                dir.y = 0f;
                float angle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                Vector3 scale = new Vector3(Props.indicatorDrawSize.x, 1f, Props.indicatorDrawSize.y);
                Matrix4x4 matrix = Matrix4x4.TRS(prodCenter,
                    Quaternion.AngleAxis(angle, Vector3.up), scale);
                Graphics.DrawMesh(MeshPool.plane10, matrix, indicatorMat, 0);
            }
        }
        // ============================================================
        //  Dev gizmo
        // ============================================================
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // 批 2：中枢面板 Order=-91 先于中继 -90（多选排序裁决 Q1(a)）
            yield return new CelesIM_Gizmo_ThreadBandwidth { node = this, Order = -91f };
            if (DebugSettings.godMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: 网络状态",
                    defaultDesc = "输出线程网络管理器状态到日志。",
                    action = delegate
                    {
                        CelesIM_ThreadNetworkManager mgr = Mgr;
                        if (mgr != null)
                            Log.Message(mgr.DebugString());
                        else
                            Log.Warning("[CelesIM] ThreadNetwork manager unavailable (no map)");
                    }
                };
                yield return new Command_Action
                {
                    defaultLabel = "DEV: 修复中枢故障",
                    defaultDesc = "立即修复中枢故障（等同维修 Job 成功，走抑制算法事件 2）。",
                    action = delegate { Notify_FaultRepaired(); }
                };
                yield return new Command_Action
                {
                    defaultLabel = "DEV: +4 线程上限",
                    action = delegate { devCapacityOffset += 4; }
                };
                yield return new Command_Action
                {
                    defaultLabel = "DEV: -4 线程上限",
                    action = delegate { devCapacityOffset -= 4; }
                };
                yield return new Command_Action
                {
                    defaultLabel = "DEV: 重置DEV线程调整",
                    action = delegate { devCapacityOffset = 0; }
                };
            }
        }
        // ── Lifecycle ──
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.GetComp<CompPowerTrader>();
            if (Mgr != null)
                Mgr.Notify_HubSpawned(this);   // 注册 + 干扰重算（新 hub 必为最大 thingIDNumber → 后来者停机）
            if (!respawningAfterLoad)
                overloadTimer = 0;
        }
        // ECS 阶段1：超载推进由管理器 MapComponentTick 统一调度（comp 零 tick 职责）
        // 注册表仅含 Spawned 实体——Spawned 守卫移除；干扰/故障冻结语义不变
        internal void TickOverload()
        {
            if (isShutdownByInterference)
                return;
            if (faultActive)
                return;
            if (IsOverloaded && IsOnline)
            {
                if (overloadTimer == 0)
                    overloadTimer = Props.overloadCount;
                else
                {
                    overloadTimer--;
                    if (overloadTimer <= 0)
                    {
                        overloadTimer = 0;
                        OnOverloadBreakdown();
                    }
                }
            }
            else if (!IsOverloaded)
            {
                overloadTimer = 0;
            }
            // IsOverloaded && !IsOnline: 冻结，不递减不复位
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref faultActive, "faultActive", false);
            Scribe_Values.Look(ref suppressPending, "suppressPending", false);
            Scribe_Values.Look(ref overloadTimer, "overloadTimer", 0);
            Scribe_Values.Look(ref capacityOffset, "capacityOffset", 0);
        }
        public override string CompInspectStringExtra()
        {
            if (isShutdownByInterference)
                return "CelesIM_Keyed_HubInspectInterference".Translate();
            if (faultActive)
                return "CelesIM_Keyed_HubInspectFault".Translate();
            if (IsOverloaded)
            {
                if (!IsOnline)
                    return "CelesIM_Keyed_HubInspectOverloadOffline".Translate(TotalCapacity, CurrentLoad, LoadRate.ToStringPercent());
                // 批 2 修正：计时走原版统一格式化器 GenDate.ToStringTicksToPeriod（:254——秒/小时/天自适应，官方 Period* 键族零硬编码）
                int timer = overloadTimer > 0 ? overloadTimer : Props.overloadCount;
                return "CelesIM_Keyed_HubInspectOverloadCountdown".Translate(TotalCapacity, CurrentLoad,
                    LoadRate.ToStringPercent(), timer.ToStringTicksToPeriod());
            }
            return "CelesIM_Keyed_HubInspectNormal".Translate(TotalCapacity, CurrentLoad, LoadRate.ToStringPercent());
        }
    }
}
