using System.Collections.Generic;
using Verse;
using Verse.Sound;
using RimWorld;
using UnityEngine;

namespace CelesFeature
{
    public class CompProperties_ThreadRelay : CompProperties
    {
        public int connectionLimit = 3;
        public float connectRadius = 7.9f;
        public bool defaultAccessible = true;

        public string indicatorTexPath = "";
        public Vector2 indicatorDrawSize = new Vector2(1f, 1f);
        public float indicatorVerticalOffset = 0f;

        public string childIndicatorTexPath = "";
        public Vector2 childIndicatorDrawSize = new Vector2(0.6f, 0.6f);
        public float childIndicatorVerticalOffset = 0f;

        public CompProperties_ThreadRelay()
        {
            compClass = typeof(CompThreadRelay);
        }
    }

    public class CompThreadRelay : ThingComp, IThreadNode
    {
        public CompProperties_ThreadRelay Props => (CompProperties_ThreadRelay)props;

        private List<Thing> connectedChildren = new List<Thing>();
        private Thing parentNode;
        private bool accessibleMode = true;
        private bool hubOverloaded;
        private bool isStandby;
        private bool isDisabledByInterference;

        [Unsaved] private IThreadNode parentNodeCached;
        [Unsaved] private CompPowerTrader powerComp;
        [Unsaved] private int devConnectionOffset;
        [Unsaved] private int cachedSubtreeLoad = -1;
        [Unsaved] private Material indicatorMat;
        [Unsaved] private bool indicatorMatTried;
        [Unsaved] private Material childIndicatorMat;
        [Unsaved] private bool childIndicatorMatTried;
        [Unsaved] private Material lackMat;
        [Unsaved] private bool lackMatTried;
        [Unsaved] private Material overloadMat;
        [Unsaved] private bool overloadMatTried;

        Color IThreadNode.PipColor => new Color(0.35f, 0.65f, 0.85f);
        string IThreadNode.GizmoLabel => "连接数";

        public int TotalCapacity => Props.connectionLimit + devConnectionOffset;
        public int CurrentLoad => ConnectionCount;
        public bool IsOverloaded => false;
        public int ConnectionCount => connectedChildren.Count + (IsConnectedToParent ? 1 : 0);
        public int SubtreeLoad => ComputeSubtreeLoad(0);

        private int ComputeSubtreeLoad(int depth)
        {
            if (depth > 15)
                Log.Error("我真的不建议你堆叠这么多中继，因为我没写好代码导致你堆太多会炸档XD \nCompThreadRelay: SubtreeLoad exceeded max depth 15 at " + parent.LabelCap);
            if (cachedSubtreeLoad < 0)
            {
                cachedSubtreeLoad = 0;
                for (int i = 0; i < connectedChildren.Count; i++)
                {
                    CompThreadConsumer c = connectedChildren[i]?.TryGetComp<CompThreadConsumer>();
                    if (c != null) { cachedSubtreeLoad += c.ThreadCost; continue; }
                    CompThreadRelay r = connectedChildren[i]?.TryGetComp<CompThreadRelay>();
                    if (r != null) cachedSubtreeLoad += r.ComputeSubtreeLoad(depth + 1);
                }
            }
            return cachedSubtreeLoad;
        }
        public bool IsConnectedToParent => parentNode != null && parentNodeCached != null;
        public bool IsOnline => HasPower && !isDisabledByInterference && HasValidUplink();
        public bool IsStandby => isStandby;
        public bool IsDisabledByInterference => isDisabledByInterference;
        public bool IsAccessible => accessibleMode;
        private bool HasPower => powerComp?.PowerOn ?? true;

        // ============================================================
        //  上行链路自检
        // ============================================================
        private bool HasValidUplink()
        {
            if (parentNode == null || parentNodeCached == null)
                return false;
            Thing cursor = parentNode;
            int safety = 0;
            while (cursor != null && safety++ < 100)
            {
                CompThreadProducer producer = cursor.TryGetComp<CompThreadProducer>();
                if (producer != null)
                {
                    hubOverloaded = producer.IsBrokenDownByOverload;
                    return producer.IsOnline;
                }
                CompThreadRelay relay = cursor.TryGetComp<CompThreadRelay>();
                if (relay == null || !relay.HasPower)
                    return false;
                cursor = relay.parentNode;
            }
            return false;
        }

        // ============================================================
        //  子级管理
        // ============================================================
        public void RegisterChild(Thing thing)
        {
            if (thing != null && !connectedChildren.Contains(thing))
            {
                connectedChildren.Add(thing);
                InvalidateSubtreeLoad();
            }
        }

        public void UnregisterChild(Thing thing)
        {
            connectedChildren.Remove(thing);
            InvalidateSubtreeLoad();
        }

        // ============================================================
        //  父级连接
        // ============================================================
        public void TryConnectToParent(Thing excludeNode = null)
        {
            if (!parent.Spawned || !HasPower)
                return;
            if (IsConnectedToParent)
                return;
            Map map = parent.Map;
            if (map == null)
                return;

            List<Building> allBuildings = map.listerBuildings.allBuildingsColonist;
            List<(Building building, int priority, float distSq)> candidates =
                new List<(Building, int, float)>();

            for (int i = 0; i < allBuildings.Count; i++)
            {
                Building building = allBuildings[i];
                if (building == parent || building == excludeNode)
                    continue;

                CompThreadProducer producer = building.TryGetComp<CompThreadProducer>();
                if (producer != null && producer.IsOnline)
                {
                    float maxRadius = Mathf.Max(Props.connectRadius, producer.Props.connectRadius);
                    if (parent.Position.InHorDistOf(building.Position, maxRadius))
                    {
                        if (!WouldCreateCycle(building))
                            candidates.Add((building, 0, parent.Position.DistanceToSquared(building.Position)));
                    }
                    continue;
                }

                CompThreadRelay relay = building.TryGetComp<CompThreadRelay>();
                if (relay != null && relay != this && relay.IsOnline)
                {
                    if (relay.ConnectionCount >= relay.TotalCapacity)
                        continue;
                    float maxRadius = Mathf.Max(Props.connectRadius, relay.Props.connectRadius);
                    if (parent.Position.InHorDistOf(building.Position, maxRadius))
                    {
                        if (!WouldCreateCycle(building))
                            candidates.Add((building, 1, parent.Position.DistanceToSquared(building.Position)));
                    }
                }
            }

            if (candidates.Count == 0)
                return;

            candidates.Sort((a, b) =>
            {
                if (a.priority != b.priority)
                    return a.priority.CompareTo(b.priority);
                return a.distSq.CompareTo(b.distSq);
            });

            var best = candidates[0];
            parentNode = best.building;
            ResolveParentCache();

            parentNode.TryGetComp<CompThreadProducer>()?.RegisterNode(parent);
            parentNode.TryGetComp<CompThreadRelay>()?.RegisterChild(parent);

            InvalidateSubtreeLoad();
        }

        public void DisconnectFromParent()
        {
            if (parentNode == null)
                return;
            parentNode.TryGetComp<CompThreadProducer>()?.UnregisterNode(parent);
            parentNode.TryGetComp<CompThreadRelay>()?.UnregisterChild(parent);
            parentNode = null;
            parentNodeCached = null;
            InvalidateSubtreeLoad();
        }

        private bool WouldCreateCycle(Thing candidate)
        {
            Thing cursor = candidate;
            int safety = 0;
            while (cursor != null && safety++ < 100)
            {
                if (cursor == parent)
                    return true;
                cursor = cursor.TryGetComp<CompThreadRelay>()?.parentNode;
            }
            return false;
        }

        // ============================================================
        //  通知链（仅 Consumer 子级，Relay 子级自行上溯）
        // ============================================================
        public void OnProducerOnline()
        {
            isStandby = false;
            NotifyChildrenOnline();
        }

        public void OnProducerOffline()
        {
            isStandby = true;
            NotifyChildrenOffline();
        }

        public void OnProducerLost()
        {
            DisconnectFromParent();
            TryConnectToParent();
        }

        private void NotifyChildrenOnline()
        {
            for (int i = connectedChildren.Count - 1; i >= 0; i--)
                connectedChildren[i]?.TryGetComp<CompThreadConsumer>()?.OnProducerOnline();
        }

        private void NotifyChildrenOffline()
        {
            for (int i = connectedChildren.Count - 1; i >= 0; i--)
                connectedChildren[i]?.TryGetComp<CompThreadConsumer>()?.OnProducerOffline();
        }

        private void NotifyChildrenLost()
        {
            for (int i = connectedChildren.Count - 1; i >= 0; i--)
            {
                Thing child = connectedChildren[i];
                if (child == null)
                    continue;
                child.TryGetComp<CompThreadConsumer>()?.OnProducerLost();
                child.TryGetComp<CompThreadRelay>()?.OnProducerLost();
            }
        }

        public void InvalidateSubtreeLoad()
        {
            cachedSubtreeLoad = -1;
            parentNode?.TryGetComp<CompThreadRelay>()?.InvalidateSubtreeLoad();
        }

        // ============================================================
        //  递归范围圈 + 连线渲染
        // ============================================================
        public void DrawSubtreeRangeRings()
        {
            GenDraw.DrawRadiusRing(parent.Position, Props.connectRadius, PlaceWorker_ShowThreadConnection.DimRingColor);
            for (int i = 0; i < connectedChildren.Count; i++)
                connectedChildren[i]?.TryGetComp<CompThreadRelay>()?.DrawSubtreeRangeRings();
        }

        // ============================================================
        public void DrawSubtreeRecursive()
        {
            for (int i = 0; i < connectedChildren.Count; i++)
            {
                Thing child = connectedChildren[i];
                if (child == null)
                    continue;

                CompThreadConsumer consumer = child.TryGetComp<CompThreadConsumer>();
                if (consumer != null)
                {
                    if (!consumer.IsStandby)
                        GenDraw.DrawLineBetween(parent.TrueCenter(), child.TrueCenter());
                    else
                        GenDraw.DrawLineBetween(parent.TrueCenter(), child.TrueCenter(),
                            CompAffectedByFacilities.InactiveFacilityLineMat);
                    continue;
                }

                CompThreadRelay relay = child.TryGetComp<CompThreadRelay>();
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
        //  方向指示器
        // ============================================================
        public override void PostDraw()
        {
            // 特效优先级：断电 > 断连 > 过载，永不叠加
            if (powerComp == null || powerComp.PowerOn)
            {
                if (!IsConnectedToParent || isStandby || isDisabledByInterference)
                {
                    if (!lackMatTried)
                    {
                        lackMat = MaterialPool.MatFrom("Celes/UI/Overlays/Celes_ThreadLack", ShaderDatabase.Transparent);
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
                        overloadMat = MaterialPool.MatFrom("Celes/UI/Overlays/Celes_ThreadOverload", ShaderDatabase.Transparent);
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

            if (!IsConnectedToParent && connectedChildren.Count == 0)
                return;

            Vector3 baseCenter = parent.DrawPos;

            if (IsConnectedToParent && !Props.indicatorTexPath.NullOrEmpty())
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

            if (!Props.childIndicatorTexPath.NullOrEmpty())
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
                    for (int i = 0; i < connectedChildren.Count; i++)
                    {
                        Thing child = connectedChildren[i];
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

            if (IsConnectedToParent && parentNode != null)
            {
                if (isDisabledByInterference)
                    GenDraw.DrawLineBetween(parent.TrueCenter(), parentNode.TrueCenter(),
                        CompAffectedByFacilities.InactiveFacilityLineMat);
                else if (isStandby)
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
            if (!respawningAfterLoad)
            {
                accessibleMode = Props.defaultAccessible;
                hubOverloaded = false;
                isStandby = false;
                isDisabledByInterference = false;
            }
            TryConnectToParent();
        }

        [Unsaved] private int tickCounter;

        public override void CompTick()
        {
            if (!parent.Spawned)
                return;

            if (++tickCounter < 250)
                return;
            tickCounter = 0;

            // 干扰检测
            Map map = parent.Map;
            if (map != null)
            {
                bool interference = CompThreadProducer.CountActiveHubs(map) >= 2;
                if (interference && !isDisabledByInterference)
                {
                    isDisabledByInterference = true;
                    NotifyChildrenOffline();
                }
                else if (!interference && isDisabledByInterference)
                {
                    isDisabledByInterference = false;
                    TryConnectToParent();
                }
            }

            if (parentNode != null && parentNodeCached == null)
            {
                DisconnectFromParent();
                TryConnectToParent();
                return;
            }

            if (IsConnectedToParent && !parentNode.Spawned)
            {
                DisconnectFromParent();
                TryConnectToParent();
                return;
            }

            if (!IsConnectedToParent)
            {
                TryConnectToParent();
                return;
            }

            bool uplinkValid = HasValidUplink();
            if (uplinkValid && isStandby)
            {
                isStandby = false;
                NotifyChildrenOnline();
            }
            else if (!uplinkValid && !isStandby)
            {
                isStandby = true;
                NotifyChildrenOffline();
            }
        }

        public override void ReceiveCompSignal(string signal)
        {
            switch (signal)
            {
                case "PowerTurnedOn":
                    TryConnectToParent();
                    if (IsConnectedToParent)
                    {
                        isStandby = false;
                        NotifyChildrenOnline();
                    }
                    break;
                case "PowerTurnedOff":
                    NotifyChildrenLost();
                    DisconnectFromParent();
                    break;
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            DisconnectFromParent();
            NotifyChildrenLost();
            connectedChildren.Clear();
            base.PostDeSpawn(map, mode);
        }

        private void ResolveParentCache()
        {
            if (parentNode != null)
            {
                parentNodeCached = parentNode.TryGetComp<CompThreadProducer>() as IThreadNode
                                ?? parentNode.TryGetComp<CompThreadRelay>() as IThreadNode;
            }
            else
            {
                parentNodeCached = null;
            }
        }

        // ============================================================
        //  Gizmo
        // ============================================================
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            yield return new Gizmo_ThreadBandwidth { node = this };

            yield return new Command_Action
            {
                defaultLabel = "重新连接",
                defaultDesc = "手动重置中继信号塔连接，优先连接至非当前目标的父级。",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/TryReconnect"),
                action = delegate
                {
                    Thing prev = parentNode;
                    DisconnectFromParent();
                    TryConnectToParent(prev);
                    if (!IsConnectedToParent)
                        TryConnectToParent();
                    if (parentNode != null)
                    {
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        for (int i = 0; i < 5; i++)
                            FleckMaker.ThrowMetaPuff(parentNode.DrawPos, parentNode.Map);
                    }
                }
            };

            yield return new Command_Toggle
            {
                defaultLabel = "可接入",
                defaultDesc = "切换为中继信号塔仅转发模式，已连接的终端将断开并尝试重连至其他目标。",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/TryReconnect"),
                isActive = () => accessibleMode,
                toggleAction = delegate
                {
                    accessibleMode = !accessibleMode;
                    if (accessibleMode)
                        WakeNearbyDisconnectedConsumers();
                    else
                    {
                        for (int i = connectedChildren.Count - 1; i >= 0; i--)
                            connectedChildren[i]?.TryGetComp<CompThreadConsumer>()?.OnProducerLost();
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
            Scribe_Collections.Look(ref connectedChildren, "connectedChildren", LookMode.Reference);
            Scribe_References.Look(ref parentNode, "parentNode");
            Scribe_Values.Look(ref accessibleMode, "accessibleMode", true);
            Scribe_Values.Look(ref hubOverloaded, "hubOverloaded", false);
            Scribe_Values.Look(ref isStandby, "isStandby", false);
            Scribe_Values.Look(ref isDisabledByInterference, "isDisabledByInterference", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                ResolveParentCache();
        }

        // ============================================================
        //  UI
        // ============================================================
        public override string CompInspectStringExtra()
        {
            string status;
            if (isDisabledByInterference)
                status = "中枢干扰，中继离线";
            else if (parentNode != null)
            {
                string parentLabel = parentNode.LabelCap ?? " ";
                if (parentNodeCached == null)
                    status = string.Format("已连接: {0}（无效对象）", parentLabel);
                else if (isStandby)
                    status = "待机(中枢离线)";
                else
                    status = string.Format("已连接: {0}", parentLabel);
            }
            else
            {
                status = "未连接";
            }

            string mode = accessibleMode ? "可接入" : "仅转发";

            string result = string.Format(
                "{0}\n{1}\n连接数: {2} / {3}",
                status,
                mode,
                ConnectionCount,
                TotalCapacity
            );

            if (hubOverloaded)
                result += "\n中枢过载，已暂停接入";

            return result;
        }

        private void WakeNearbyDisconnectedConsumers()
        {
            Map map = parent.Map;
            if (map == null)
                return;
            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                if (!parent.Position.InHorDistOf(buildings[i].Position, Props.connectRadius))
                    continue;
                CompThreadConsumer consumer = buildings[i].TryGetComp<CompThreadConsumer>();
                if (consumer != null && !consumer.IsConnected)
                    consumer.TryConnect();
            }
        }
    }
}
