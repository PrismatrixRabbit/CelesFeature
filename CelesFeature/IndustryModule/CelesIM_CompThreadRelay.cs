using System.Collections.Generic;
using Verse;
using Verse.Sound;
using RimWorld;
using UnityEngine;

namespace CelesFeature
{
    public class CelesIM_CompProperties_ThreadRelay : CompProperties
    {
        public int connectionLimit = 3;
        public float connectRadius = 7.9f;
        public bool defaultAccessible = true;

        // indicator 六参数：批 3 视觉批随方向指示器一并删除（E10）
        public string indicatorTexPath = "";
        public Vector2 indicatorDrawSize = new Vector2(1f, 1f);
        public float indicatorVerticalOffset = 0f;

        public string childIndicatorTexPath = "";
        public Vector2 childIndicatorDrawSize = new Vector2(0.6f, 0.6f);
        public float childIndicatorVerticalOffset = 0f;

        public CelesIM_CompProperties_ThreadRelay()
        {
            compClass = typeof(CelesIM_CompThreadRelay);
        }
    }

    // 中继（2026-09-13 C'+D'：绑定图迁管理器 + 断电语义终版）
    //   状态机：活跃 / 无信号（上行绑定保留·恢复即回）/ 未连接（无上行绑定）；正交：可接入/仅转发
    //   断电不解除上行绑定、不踢子级——子树显示随派生态转「无信号」
    public class CelesIM_CompThreadRelay : ThingComp, CelesIM_IThreadNode
    {
        public CelesIM_CompProperties_ThreadRelay Props => (CelesIM_CompProperties_ThreadRelay)props;

        // ── 本机持久状态（上行绑定经管理器）──
        private bool accessibleMode = true;

        // ── 运行期缓存 ──
        [Unsaved] private CompPowerTrader powerComp;
        [Unsaved] private int devConnectionOffset;
        [Unsaved] private Material indicatorMat;
        [Unsaved] private bool indicatorMatTried;
        [Unsaved] private Material childIndicatorMat;
        [Unsaved] private bool childIndicatorMatTried;
        [Unsaved] private Material lackMat;
        [Unsaved] private bool lackMatTried;
        [Unsaved] private Material overloadMat;
        [Unsaved] private bool overloadMatTried;

        Color CelesIM_IThreadNode.PipColor => new Color(0.35f, 0.65f, 0.85f);
        string CelesIM_IThreadNode.GizmoLabel => "CelesIM_Keyed_GizmoConnections".Translate();

        private CelesIM_ThreadNetworkManager Mgr =>
            parent == null || parent.Map == null ? null : CelesIM_ThreadNetworkManager.For(parent.Map);

        // ── 公开查询面（保持签名，内部转管理器/派生——Q7）──
        public bool HasPowerNow => powerComp?.PowerOn ?? true;
        public int TotalCapacity => Props.connectionLimit + devConnectionOffset;
        public int CurrentLoad => ConnectionCount;
        public bool IsOverloaded => false;
        public int ConnectionCount => (Mgr?.GetDirectChildren(parent)?.Count ?? 0) + (IsConnectedToParent ? 1 : 0);
        public int SubtreeLoad => Mgr?.SubtreeLoad(parent) ?? 0;
        // 批 2：无信号段计数（Gizmo 嵌套格数据源——直接子级中绑定但非活跃者 + 自身上行占位槽）
        public int NoSignalLoad
        {
            get
            {
                CelesIM_ThreadNetworkManager mgr = Mgr;
                if (mgr == null) return 0;
                int count = IsStandby ? 1 : 0;   // 上行占位槽（链路不通）
                List<Thing> children = mgr.GetDirectChildren(parent);
                if (children == null) return count;
                for (int i = 0; i < children.Count; i++)
                {
                    Thing child = children[i];
                    if (child == null) continue;
                    CelesIM_CompThreadConsumer consumer = child.TryGetComp<CelesIM_CompThreadConsumer>();
                    if (consumer != null && consumer.IsStandby) { count++; continue; }
                    CelesIM_CompThreadRelay relay = child.TryGetComp<CelesIM_CompThreadRelay>();
                    if (relay != null && relay.IsStandby) count++;
                }
                return count;
            }
        }
        public bool IsConnectedToParent => Mgr != null && Mgr.IsBoundToParent(parent);
        public bool IsAccessible => accessibleMode;
        // 活跃 = 本机有电 ∧ 上行绑定 ∧ 父链通（E 批：中继自检干扰机制删除——干扰=中枢停机，唯一机制）
        public bool IsOnline => HasPowerNow && (Mgr != null && Mgr.IsActiveRelay(this));
        // 「无信号」派生（上行绑定保留·链路不通）——保留旧属性名（Q7）
        public bool IsStandby => IsConnectedToParent && !(Mgr != null && Mgr.IsActiveRelay(this));

        // ============================================================
        //  上行连接（候选走管理器注册表——算法与原全图版逐行等价，Q1）
        // ============================================================
        public void TryConnectToParent(Thing excludeNode = null)
        {
            if (!parent.Spawned || !HasPowerNow)
                return;
            if (IsConnectedToParent)
                return;
            CelesIM_ThreadNetworkManager mgr = Mgr;
            if (mgr == null)
                return;
            List<Thing> candidates = mgr.CandidateNodesForRelay(this, excludeNode);
            if (candidates.Count == 0)
                return;
            mgr.BindRelay(this, candidates[0]);
        }

        public void DisconnectFromParent()
        {
            Mgr?.UnbindRelay(this);
        }

        // 节点消失迁移路径（管理器级联触发；自动重试迁移）
        public void OnProducerLost()
        {
            DisconnectFromParent();
            TryConnectToParent();
        }

        // ============================================================
        //  递归范围圈 + 连线渲染（子级清单走管理器反向索引）
        // ============================================================
        public void DrawSubtreeRangeRings()
        {
            GenDraw.DrawRadiusRing(parent.Position, Props.connectRadius, CelesIM_PlaceWorker_ShowThreadConnection.DimRingColor);
            List<Thing> children = Mgr?.GetDirectChildren(parent);
            if (children == null) return;
            for (int i = 0; i < children.Count; i++)
                children[i]?.TryGetComp<CelesIM_CompThreadRelay>()?.DrawSubtreeRangeRings();
        }

        public void DrawSubtreeRecursive()
        {
            List<Thing> children = Mgr?.GetDirectChildren(parent);
            if (children == null) return;
            for (int i = 0; i < children.Count; i++)
            {
                Thing child = children[i];
                if (child == null)
                    continue;

                CelesIM_CompThreadConsumer consumer = child.TryGetComp<CelesIM_CompThreadConsumer>();
                if (consumer != null)
                {
                    if (consumer.IsActiveConnection)
                        GenDraw.DrawLineBetween(parent.TrueCenter(), child.TrueCenter());
                    else
                        GenDraw.DrawLineBetween(parent.TrueCenter(), child.TrueCenter(),
                            CompAffectedByFacilities.InactiveFacilityLineMat);
                    continue;
                }

                CelesIM_CompThreadRelay relay = child.TryGetComp<CelesIM_CompThreadRelay>();
                if (relay != null)
                {
                    if (!relay.IsStandby)
                        GenDraw.DrawLineBetween(parent.TrueCenter(), child.TrueCenter());
                    else
                        GenDraw.DrawLineBetween(parent.TrueCenter(), child.TrueCenter(),
                            CompAffectedByFacilities.InactiveFacilityLineMat);
                    relay.DrawSubtreeRecursive();
                }
            }
        }

        // ============================================================
        //  特效渲染（优先级 断电>断连>过载 永不叠加——方向指示器批 3 删除）
        // ============================================================
        public override void PostDraw()
        {
            if (powerComp == null || powerComp.PowerOn)
            {
                if (!IsConnectedToParent || IsStandby)
                {
                    if (!lackMatTried)
                    {
                        lackMat = MaterialPool.MatFrom("Celes/UI/Icons/LackThreadConnect", ShaderDatabase.Transparent);
                        lackMatTried = true;
                    }
                    if (lackMat != null)
                    {
                        float alpha = Mathf.Lerp(0.35f, 0.95f, Mathf.PingPong(Time.realtimeSinceStartup * 1.0f, 1f));
                        lackMat.SetColor("_Color", new Color(1f, 1f, 1f, alpha));
                        Vector3 blinkPos = parent.DrawPos;
                        blinkPos.y += 0.3f;
                        Graphics.DrawMesh(MeshPool.plane10,
                            Matrix4x4.TRS(blinkPos, Quaternion.identity, new Vector3(0.6f, 1f, 0.6f)),
                            lackMat, 0);
                    }
                }
                else if (CurrentLoad > TotalCapacity)
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
                        Vector3 blinkPos = parent.DrawPos;
                        blinkPos.y += 0.3f;
                        Graphics.DrawMesh(MeshPool.plane10,
                            Matrix4x4.TRS(blinkPos, Quaternion.identity, new Vector3(0.8f, 1f, 0.8f)),
                            overloadMat, 0);
                    }
                }
            }

            // ── 方向指示器（E10：批 3 删除整段）──
            List<Thing> children = Mgr?.GetDirectChildren(parent);
            Thing parentNode = Mgr?.GetRelayParent(parent);
            if (parentNode == null && (children == null || children.Count == 0))
                return;

            Vector3 baseCenter = parent.DrawPos;

            if (parentNode != null && !Props.indicatorTexPath.NullOrEmpty())
            {
                if (!indicatorMatTried)
                {
                    indicatorMat = MaterialPool.MatFrom(Props.indicatorTexPath, ShaderDatabase.Transparent);
                    indicatorMatTried = true;
                }
                if (indicatorMat != null)
                {
                    Vector3 pos = baseCenter;
                    pos.z += Props.indicatorVerticalOffset;
                    Vector3 dir = parentNode.DrawPos - pos;
                    dir.y = 0f;
                    float angle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                    Graphics.DrawMesh(MeshPool.plane10,
                        Matrix4x4.TRS(pos, Quaternion.AngleAxis(angle, Vector3.up),
                            new Vector3(Props.indicatorDrawSize.x, 1f, Props.indicatorDrawSize.y)),
                        indicatorMat, 0);
                }
            }

            if (children != null && !Props.childIndicatorTexPath.NullOrEmpty())
            {
                if (!childIndicatorMatTried)
                {
                    childIndicatorMat = MaterialPool.MatFrom(Props.childIndicatorTexPath, ShaderDatabase.Transparent);
                    childIndicatorMatTried = true;
                }
                if (childIndicatorMat != null)
                {
                    Vector3 pos = baseCenter;
                    pos.z += Props.childIndicatorVerticalOffset;
                    for (int i = 0; i < children.Count; i++)
                    {
                        Thing child = children[i];
                        if (child == null)
                            continue;
                        Vector3 dir = child.DrawPos - pos;
                        dir.y = 0f;
                        float angle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                        Graphics.DrawMesh(MeshPool.plane10,
                            Matrix4x4.TRS(pos, Quaternion.AngleAxis(angle, Vector3.up),
                                new Vector3(Props.childIndicatorDrawSize.x, 1f, Props.childIndicatorDrawSize.y)),
                            childIndicatorMat, 0);
                    }
                }
            }
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            GenDraw.DrawRadiusRing(parent.Position, Props.connectRadius);

            Thing parentNode = Mgr?.GetRelayParent(parent);
            if (IsConnectedToParent && parentNode != null)
            {
                if (IsStandby)
                    GenDraw.DrawLineBetween(parent.TrueCenter(), parentNode.TrueCenter(),
                        CompAffectedByFacilities.InactiveFacilityLineMat);
                else
                    GenDraw.DrawLineBetween(parent.TrueCenter(), parentNode.TrueCenter());
            }

            DrawSubtreeRecursive();
        }

        // ============================================================
        //  Lifecycle
        // ============================================================
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.GetComp<CompPowerTrader>();
            if (Mgr != null)
                Mgr.Notify_RelaySpawned(this);
            if (!respawningAfterLoad)
            {
                accessibleMode = Props.defaultAccessible;
            }
            // 读档门（2026-09-20 裁决）：读档不自发连接——保留档内状态；仅玩家主动（建造）触发入网
            if (!respawningAfterLoad)
                TryConnectToParent();
        }

        // R7 移除（2026-09-20 裁决）：250tick 未绑定重试轮询删除——
        // 自动连接终局清单=建造/复电/唤醒/迁移/手动五类玩家因果路径；comp 零 tick 职责（ECS 阶段1）

        public override void ReceiveCompSignal(string signal)
        {
            switch (signal)
            {
                case "PowerTurnedOn":
                case "PowerTurnedOff":
                    // ECS 阶段1：统一转发管理器单入口（断电无操作=上行绑定保留派生态）
                    Mgr?.Notify_PowerChanged(parent, signal == "PowerTurnedOn");
                    break;
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            // 解绑与子级级联由管理器 Deregister 阶段处理
            CelesIM_ThreadNetworkManager mgr = map != null ? CelesIM_ThreadNetworkManager.For(map) : null;
            mgr?.Notify_RelayDespawned(this);
        }

        // ============================================================
        //  Gizmo
        // ============================================================
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            yield return new CelesIM_Gizmo_ThreadBandwidth { node = this };

            // 允许终端接入 toggle 前置（2026-09-23 裁决：位于重置线程连接之前）；icon=ThreadConnect（与终端启用开关同图）
            yield return new Command_Toggle
            {
                defaultLabel = "CelesIM_Keyed_Accessible".Translate(),
                defaultDesc = "CelesIM_Keyed_AccessibleDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("Celes/UI/Icons/ThreadConnect"),
                isActive = () => accessibleMode,
                toggleAction = delegate
                {
                    accessibleMode = !accessibleMode;
                    if (accessibleMode)
                    {
                        WakeNearbyDisconnectedConsumers();
                    }
                    else
                    {
                        List<Thing> children = Mgr?.GetDirectChildren(parent);
                        if (children != null)
                        {
                            for (int i = children.Count - 1; i >= 0; i--)
                                children[i]?.TryGetComp<CelesIM_CompThreadConsumer>()?.OnProducerLost();
                        }
                    }
                }
            };

            yield return new Command_Action
            {
                defaultLabel = "CelesIM_Keyed_ResetConnection".Translate(),
                defaultDesc = "CelesIM_Keyed_ResetConnectionDescRelay".Translate(),
                icon = ContentFinder<Texture2D>.Get("Celes/UI/Icons/TryThreadConnect"),
                action = delegate
                {
                    Thing prev = Mgr?.GetRelayParent(parent);
                    DisconnectFromParent();
                    TryConnectToParent(prev);
                    if (!IsConnectedToParent)
                        TryConnectToParent();
                    Thing nowParent = Mgr?.GetRelayParent(parent);
                    if (nowParent != null)
                    {
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        for (int i = 0; i < 5; i++)
                            FleckMaker.ThrowMetaPuff(nowParent.DrawPos, nowParent.Map);
                    }
                }
            };

            if (DebugSettings.godMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: +1 连接上限",
                    action = delegate { devConnectionOffset += 1; }
                };
                yield return new Command_Action
                {
                    defaultLabel = "DEV: -1 连接上限",
                    action = delegate { devConnectionOffset -= 1; }
                };
                yield return new Command_Action
                {
                    defaultLabel = "DEV: 重置DEV连接调整",
                    action = delegate { devConnectionOffset = 0; }
                };
            }
        }

        // ============================================================
        //  Save / Load
        // ============================================================
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref accessibleMode, "accessibleMode", true);
        }

        // ============================================================
        //  UI
        // ============================================================
        public override string CompInspectStringExtra()
        {
            string status;
            if (IsConnectedToParent)
            {
                Thing parentNode = Mgr?.GetRelayParent(parent);
                string parentLabel = parentNode?.LabelCap ?? " ";
                if (IsStandby)
                    status = "CelesIM_Keyed_StatusNoSignal".Translate();
                else
                    status = "CelesIM_Keyed_StatusConnected".Translate(parentLabel);
            }
            else
            {
                status = "CelesIM_Keyed_StatusNotConnected".Translate();
            }

            string mode = accessibleMode
                ? "CelesIM_Keyed_ModeAccessible".Translate()
                : "CelesIM_Keyed_ModeForwardOnly".Translate();

            return "CelesIM_Keyed_RelayInspect".Translate(status, mode, ConnectionCount, TotalCapacity);
        }

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
                if (consumer == null || consumer.parent == null)
                    continue;
                if (!parent.Position.InHorDistOf(consumer.parent.Position, Props.connectRadius))
                    continue;
                if (!consumer.IsConnected)
                    consumer.TryConnect();
            }
        }
    }
}
