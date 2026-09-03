using System.Collections.Generic;
using Verse;
using Verse.Sound;
using RimWorld;
using UnityEngine;

namespace CelesFeature
{
    public class CompProperties_ThreadConsumer : CompProperties
    {
        public int baseThreadsCost = 4;
        public float produceEffic = 1.0f;
        public bool canWorkOffline = true;
        public float offlineFactor = 0.5f;
        public bool haveWarmUpCount = false;
        public int warmUpTicks = 12500;
        public string overclockTech = "";
        public CompProperties_ThreadConsumer()
        {
            compClass = typeof(CompThreadConsumer);
        }
    }

    public class CompThreadConsumer : ThingComp
    {
        public CompProperties_ThreadConsumer Props => (CompProperties_ThreadConsumer)props;
        // ── Persisted state ──
        private Thing connectedNode;
        private bool isManuallyDisconnected;
        private int warmUpRemaining;
        public bool isStandby;
        // ── Non-persisted cache ──
        [Unsaved] private CompThreadProducer connectedProducer;
        [Unsaved] private CompThreadRelay connectedRelayComp;
        [Unsaved] private CompPowerTrader powerComp;
        // ── Computed properties ──
        public int ThreadCost => Props.baseThreadsCost;
        public bool IsConnected => connectedProducer != null || connectedRelayComp != null;
        public bool IsWarmingUp => warmUpRemaining > 0;
        public bool IsStandby => isStandby;
        public CompThreadProducer ConnectedProducer => connectedProducer;
        private bool HasPower => powerComp?.PowerOn ?? true;
        public float GetWorkSpeedFactor()
        {
            return 1.0f;
        }
        // ============================================================
        //  状态回调
        // ============================================================
        public void OnProducerOnline()
        {
            isStandby = false;
        }
        public void OnProducerOffline()
        {
            isStandby = true;
        }
        public void OnProducerLost()
        {
            connectedProducer?.UnregisterNode(parent);
            connectedRelayComp?.UnregisterChild(parent);
            connectedProducer = null;
            connectedRelayComp = null;
            connectedNode = null;
            warmUpRemaining = 0;
            isStandby = false;
            TryConnect();
        }
        // ============================================================
        //  连接管理
        // ============================================================
        public void TryConnect(Thing excludeNode = null)
        {
            if (!parent.Spawned || !HasPower || isManuallyDisconnected)
                return;
            if (IsConnected)
                return;
            Map map = parent.Map;
            if (map == null)
                return;

            List<Building> allBuildings = map.listerBuildings.allBuildingsColonist;
            List<(Building building, int priority, float distSq)> candidates =
                new List<(Building, int, float)>();

            // Pass 1: Producers (priority 0)
            for (int i = 0; i < allBuildings.Count; i++)
            {
                Building building = allBuildings[i];
                if (building == parent || building == excludeNode)
                    continue;
                CompThreadProducer producer = building.TryGetComp<CompThreadProducer>();
                if (producer == null || !producer.IsOnline)
                    continue;
                float distSq = DistanceSquaredTo(building.Position);
                if (distSq > producer.Props.connectRadius * producer.Props.connectRadius)
                    continue;
                if (!producer.CanAcceptConnection(ThreadCost))
                    continue;
                candidates.Add((building, 0, distSq));
            }

            // Pass 2: Relays (priority 1)
            for (int i = 0; i < allBuildings.Count; i++)
            {
                Building building = allBuildings[i];
                if (building == parent || building == excludeNode)
                    continue;
                CompThreadRelay relay = building.TryGetComp<CompThreadRelay>();
                if (relay == null || !relay.IsOnline || !relay.IsAccessible)
                    continue;
                float distSq = DistanceSquaredTo(building.Position);
                if (distSq > relay.Props.connectRadius * relay.Props.connectRadius)
                    continue;
                if (relay.ConnectionCount >= relay.TotalCapacity)
                    continue;
                candidates.Add((building, 1, distSq));
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
            connectedNode = best.building;

            CompThreadProducer bestProducer = best.building.TryGetComp<CompThreadProducer>();
            if (bestProducer != null)
            {
                connectedProducer = bestProducer;
                connectedProducer.RegisterNode(parent);
            }
            else
            {
                CompThreadRelay bestRelay = best.building.TryGetComp<CompThreadRelay>();
                connectedRelayComp = bestRelay;
                bestRelay.RegisterChild(parent);
            }

            isStandby = false;
            if (Props.haveWarmUpCount)
                warmUpRemaining = Props.warmUpTicks;
        }

        public void Disconnect()
        {
            if (connectedProducer != null)
            {
                connectedProducer.UnregisterNode(parent);
                connectedProducer = null;
            }
            if (connectedRelayComp != null)
            {
                connectedRelayComp.UnregisterChild(parent);
                connectedRelayComp = null;
            }
            connectedNode = null;
            warmUpRemaining = 0;
            isStandby = false;
        }

        public void ResetConnection()
        {
            Thing prev = connectedNode;
            Disconnect();
            isManuallyDisconnected = false;
            TryConnect(prev);
            if (!IsConnected)
                TryConnect();
        }

        private float DistanceSquaredTo(IntVec3 target)
        {
            float dx = parent.Position.x - target.x;
            float dz = parent.Position.z - target.z;
            return dx * dx + dz * dz;
        }
        // ── Gizmo ──
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            yield return new Command_Action
            {
                defaultLabel = "重置线程连接",
                defaultDesc = "手动重置编译线程连接，优先连接至非当前目标的中枢。",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/TryReconnect"),
                action = delegate
                {
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    ResetConnection();
                    if (connectedNode != null)
                    {
                        for (int i = 0; i < 5; i++)
                            FleckMaker.ThrowMetaPuff(connectedNode.DrawPos, connectedNode.Map);
                    }
                }
            };
        }
        // ============================================================
        //  渲染
        // ============================================================
        public override void PostDrawExtraSelectionOverlays()
        {
            if (!IsConnected || connectedNode == null)
                return;
            if (!isStandby)
                GenDraw.DrawLineBetween(parent.TrueCenter(), connectedNode.TrueCenter());
            else
                GenDraw.DrawLineBetween(parent.TrueCenter(), connectedNode.TrueCenter(),
                    CompAffectedByFacilities.InactiveFacilityLineMat);
        }

        [Unsaved] private Material lackMat;
        [Unsaved] private bool lackMatTried;
        public override void PostDraw()
        {
            if (powerComp != null && !powerComp.PowerOn)
                return;
            if (IsConnected && !isStandby)
                return;
            if (!lackMatTried)
            {
                lackMat = MaterialPool.MatFrom("Celes/UI/Overlays/Celes_ThreadLack", ShaderDatabase.Transparent);
                lackMatTried = true;
            }
            if (lackMat == null)
                return;
            float alpha = Mathf.Lerp(0.35f, 0.95f, Mathf.PingPong(Time.realtimeSinceStartup * 1.0f, 1f));
            lackMat.SetColor("_Color", new Color(1f, 1f, 1f, alpha));
            Vector3 iconPos = parent.DrawPos;
            iconPos.y += 0.3f;
            Graphics.DrawMesh(MeshPool.plane10,
                Matrix4x4.TRS(iconPos, Quaternion.identity, new Vector3(0.6f, 1f, 0.6f)),
                lackMat, 0);
        }
        // ── Lifecycle ──
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.GetComp<CompPowerTrader>();
            ResolveNodeCache();
            if (!respawningAfterLoad)
            {
                warmUpRemaining = 0;
                isManuallyDisconnected = false;
                isStandby = false;
            }
            if (HasPower && !IsConnected)
                TryConnect();
        }
        public override void ReceiveCompSignal(string signal)
        {
            switch (signal)
            {
                case "PowerTurnedOn":
                    if (!IsConnected && !isManuallyDisconnected)
                        TryConnect();
                    break;
                case "PowerTurnedOff":
                    Disconnect();
                    break;
            }
        }
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            Disconnect();
        }
        private void ResolveNodeCache()
        {
            connectedProducer = null;
            connectedRelayComp = null;
            if (connectedNode != null)
            {
                connectedProducer = connectedNode.TryGetComp<CompThreadProducer>();
                if (connectedProducer == null)
                    connectedRelayComp = connectedNode.TryGetComp<CompThreadRelay>();
                if (connectedProducer == null && connectedRelayComp == null)
                    connectedNode = null;
            }
        }
        public override void CompTick()
        {
            if (!parent.Spawned)
                return;
            if (connectedNode != null && connectedProducer == null && connectedRelayComp == null)
            {
                Disconnect();
                TryConnect();
            }
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref connectedNode, "connectedNode");
            Scribe_Values.Look(ref isManuallyDisconnected, "isManuallyDisconnected", false);
            Scribe_Values.Look(ref warmUpRemaining, "warmUpRemaining", 0);
            Scribe_Values.Look(ref isStandby, "isStandby", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                ResolveNodeCache();
        }
        public override string CompInspectStringExtra()
        {
            string status;
            if (!IsConnected)
                status = "未连接";
            else if (isStandby)
                status = "待机(中枢离线)";
            else if (IsWarmingUp)
                status = "预热中";
            else
            {
                string nodeLabel = connectedNode?.LabelCap ?? " ";
                status = string.Format("已连接: {0}", nodeLabel);
            }
            return string.Format(
                "占用线程: {0}\n连接状态: {1}\n工作系数: {2:F2}",
                ThreadCost,
                status,
                GetWorkSpeedFactor()
            );
        }
    }
}
