using System.Collections.Generic;
using Verse;
using RimWorld;
using UnityEngine;

namespace CelesFeature
{
    public class CompProperties_ThreadProducer : CompProperties
    {
        public int baseThreadsProduce = 8;
        public float connectRadius = 24.9f;
        public int overloadCount = 7500;
        public string indicatorTexPath = "";
        public Vector2 indicatorDrawSize = new Vector2(1f, 1f);
        public float indicatorVerticalOffset = 0f;
        public CompProperties_ThreadProducer()
        {
            compClass = typeof(CompThreadProducer);
        }
    }
    
    public class CompThreadProducer : ThingComp, IThreadNode
    {
        public CompProperties_ThreadProducer Props => (CompProperties_ThreadProducer)props;
        [Unsaved] private CompPowerTrader powerComp;
        private List<Thing> connectedThings = new List<Thing>();
        private bool isShutdownByInterference;
        private int overloadTimer;
        private bool brokenDownByOverload;
        private int capacityOffset;
        [Unsaved] private int devCapacityOffset;
        // ── Public API ──
        public bool IsShutdownByInterference => isShutdownByInterference;
        public bool IsBrokenDownByOverload => brokenDownByOverload;
        public int OverloadTimer => overloadTimer;
        Color IThreadNode.PipColor => ColorLibrary.Yellow;
        string IThreadNode.GizmoLabel => "线程";
        public int TotalCapacity =>
            isShutdownByInterference ? 0 : Mathf.Max(0, Props.baseThreadsProduce + capacityOffset + devCapacityOffset);
        public int CurrentLoad
        {
            get
            {
                int load = 0;
                for (int i = connectedThings.Count - 1; i >= 0; i--)
                {
                    Thing thing = connectedThings[i];
                    if (thing == null)
                    {
                        connectedThings.RemoveAt(i);
                        continue;
                    }
                    CompThreadConsumer consumer = thing.TryGetComp<CompThreadConsumer>();
                    if (consumer != null)
                    {
                        load += consumer.ThreadCost;
                        continue;
                    }
                    CompThreadRelay relay = thing.TryGetComp<CompThreadRelay>();
                    if (relay != null)
                    {
                        load += relay.SubtreeLoad;
                        continue;
                    }
                }
                return load;
            }
        }
        public float LoadRate =>
            TotalCapacity > 0 ? (float)CurrentLoad / TotalCapacity : 0f;
        public bool IsOverloaded => CurrentLoad > TotalCapacity;
        private bool HasPower => powerComp?.PowerOn ?? true;
        public bool IsOnline =>
            HasPower && !isShutdownByInterference && !parent.IsBrokenDown();
        public bool IsInRange(IntVec3 targetPos)
        {
            return parent.Position.InHorDistOf(targetPos, Props.connectRadius);
        }
        public bool CanAcceptConnection(int threadCost)
        {
            return IsOnline && CurrentLoad + threadCost <= TotalCapacity;
        }
        public void RegisterNode(Thing thing)
        {
            if (thing != null && !connectedThings.Contains(thing))
                connectedThings.Add(thing);
        }
        public void UnregisterNode(Thing thing)
        {
            connectedThings.Remove(thing);
        }
        public void ModifyCapacityOffset(int delta)
        {
            capacityOffset += delta;
        }
        public static int CountActiveHubs(Map map)
        {
            int count = 0;
            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                CompThreadProducer p = buildings[i].TryGetComp<CompThreadProducer>();
                if (p != null)
                    count++;
            }
            return count;
        }

        // ============================================================
        //  F-07: 超载倒计时 + CompBreakdownable 故障
        // ============================================================
        protected virtual void OnOverloadBreakdown()
        {
            for (int i = connectedThings.Count - 1; i >= 0; i--)
            {
                Thing thing = connectedThings[i];
                thing?.TryGetComp<CompThreadConsumer>()?.Disconnect();
            }
            parent.GetComp<CompBreakdownable>()?.DoBreakdown();
        }
        // ============================================================
        //  F-06: 中枢干扰
        // ============================================================
        private void CheckInterferenceAndShutdown()
        {
            Map map = parent.Map;
            if (map == null)
                return;
            bool otherOnlineFound = false;
            List<Building> allBuildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < allBuildings.Count; i++)
            {
                CompThreadProducer other = allBuildings[i].TryGetComp<CompThreadProducer>();
                if (other != null && other != this && other.IsOnline)
                {
                    otherOnlineFound = true;
                    break;
                }
            }
            if (otherOnlineFound)
            {
                if (!isShutdownByInterference)
                {
                    isShutdownByInterference = true;
                    NotifyConnectedLost();
                }
            }
            else
            {
                bool wasShutdown = isShutdownByInterference;
                isShutdownByInterference = false;
                if (wasShutdown)
                    WakeNearbyDisconnectedConsumers();
            }
        }
        private void TryWakeShutdownProducer(Map map)
        {
            if (map == null)
                return;
            CompThreadProducer candidate = null;
            int lowestTick = int.MaxValue;
            List<Building> allBuildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < allBuildings.Count; i++)
            {
                CompThreadProducer other = allBuildings[i].TryGetComp<CompThreadProducer>();
                if (other != null && other != this && other.isShutdownByInterference)
                {
                    int tick = other.parent.spawnedTick;
                    if (tick < lowestTick)
                    {
                        lowestTick = tick;
                        candidate = other;
                    }
                }
            }
            if (candidate != null)
            {
                candidate.isShutdownByInterference = false;
                candidate.WakeNearbyDisconnectedConsumers();
            }
        }
        private void WakeNearbyDisconnectedConsumers()
        {
            Map map = parent.Map;
            if (map == null)
                return;
            List<Building> allBuildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < allBuildings.Count; i++)
            {
                if (!IsInRange(allBuildings[i].Position))
                    continue;
                CompThreadConsumer consumer = allBuildings[i].TryGetComp<CompThreadConsumer>();
                if (consumer != null && !consumer.IsConnected)
                    consumer.TryConnect();
                CompThreadRelay relay = allBuildings[i].TryGetComp<CompThreadRelay>();
                if (relay != null && !relay.IsConnectedToParent)
                    relay.TryConnectToParent();
            }
        }
        // ============================================================
        //  蓝图预览（PlaceWorker 委托）：范围圈
        //  参照 CompAffectedByFacilities.DrawLinesToPotentialThingsToLinkTo 静态方法模式
        // ============================================================
        public static void DrawPlacementPreview(ThingDef def, IntVec3 center, Map map)
        {
            CompProperties_ThreadProducer props = def.GetCompProperties<CompProperties_ThreadProducer>();
            if (props != null)
                GenDraw.DrawRadiusRing(center, props.connectRadius);
        }

        // ============================================================
        //  F-09: 选中连接线渲染
        // ============================================================
        public override void PostDrawExtraSelectionOverlays()
        {
            for (int i = 0; i < connectedThings.Count; i++)
            {
                Thing thing = connectedThings[i];
                if (thing == null)
                    continue;

                CompThreadConsumer consumer = thing.TryGetComp<CompThreadConsumer>();
                if (consumer != null)
                {
                    if (!consumer.IsStandby)
                        GenDraw.DrawLineBetween(parent.TrueCenter(), thing.TrueCenter());
                    else
                        GenDraw.DrawLineBetween(parent.TrueCenter(), thing.TrueCenter(),
                            CompAffectedByFacilities.InactiveFacilityLineMat);
                    continue;
                }

                CompThreadRelay relay = thing.TryGetComp<CompThreadRelay>();
                if (relay != null)
                {
                    if (relay.IsDisabledByInterference)
                        GenDraw.DrawLineBetween(parent.TrueCenter(), thing.TrueCenter(),
                            CompAffectedByFacilities.InactiveFacilityLineMat);
                    else if (relay.IsStandby)
                        GenDraw.DrawLineBetween(parent.TrueCenter(), thing.TrueCenter(),
                            CompAffectedByFacilities.InactiveFacilityLineMat);
                    else
                        GenDraw.DrawLineBetween(parent.TrueCenter(), thing.TrueCenter());
                    relay.DrawSubtreeRangeRings();
                    relay.DrawSubtreeRecursive();
                }
            }
            GenDraw.DrawRadiusRing(parent.Position, Props.connectRadius);
        }
        // ============================================================
        //  F-04: 跨 Thing 通知链
        // ============================================================
        public override void ReceiveCompSignal(string signal)
        {
            switch (signal)
            {
                case "PowerTurnedOn":
                    CheckInterferenceAndShutdown();
                    if (IsOnline)
                    {
                        NotifyConnectedOnline();
                        WakeNearbyDisconnectedConsumers();
                    }
                    break;
                case "PowerTurnedOff":
                    NotifyConnectedOffline();
                    if (!isShutdownByInterference)
                        TryWakeShutdownProducer(parent.Map);
                    break;
                case "Breakdown":
                    for (int i = connectedThings.Count - 1; i >= 0; i--)
                        connectedThings[i]?.TryGetComp<CompThreadConsumer>()?.Disconnect();
                    break;
            }
        }
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            NotifyConnectedLost();
            connectedThings.Clear();
            if (!isShutdownByInterference)
                TryWakeShutdownProducer(map);
            base.PostDeSpawn(map, mode);
        }
        private void NotifyConnectedOnline()
        {
            for (int i = connectedThings.Count - 1; i >= 0; i--)
                connectedThings[i]?.TryGetComp<CompThreadConsumer>()?.OnProducerOnline();
        }
        private void NotifyConnectedOffline()
        {
            for (int i = connectedThings.Count - 1; i >= 0; i--)
                connectedThings[i]?.TryGetComp<CompThreadConsumer>()?.OnProducerOffline();
        }
        private void NotifyConnectedLost()
        {
            for (int i = connectedThings.Count - 1; i >= 0; i--)
                connectedThings[i]?.TryGetComp<CompThreadConsumer>()?.OnProducerLost();
        }
        // ============================================================
        //  F9-b: 方向性特效
        // ============================================================
        [Unsaved] private Material indicatorMat;
        [Unsaved] private bool indicatorMatTried;
        [Unsaved] private Material overloadMat;
        [Unsaved] private bool overloadMatTried;
        public override void PostDraw()
        {
            // ── 过载图标：有电 + 过载/故障 = 脉动（独立于 IsOnline，故障时也能画）──
            if (powerComp == null || powerComp.PowerOn)
            {
                if (IsOverloaded || brokenDownByOverload)
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
                        Vector3 overloadPos = parent.DrawPos;
                        overloadPos.y += 0.3f;
                        Graphics.DrawMesh(MeshPool.plane10,
                            Matrix4x4.TRS(overloadPos, Quaternion.identity, new Vector3(0.8f, 1f, 0.8f)),
                            overloadMat, 0);
                    }
                }
            }

            // ── 方向特效：仅在线时绘制 ──
            if (!IsOnline)
                return;
            if (Props.indicatorTexPath.NullOrEmpty())
                return;
            if (connectedThings.Count == 0)
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
            for (int i = 0; i < connectedThings.Count; i++)
            {
                Thing thing = connectedThings[i];
                if (thing == null)
                    continue;
                CompThreadConsumer consumer = thing.TryGetComp<CompThreadConsumer>();
                if (consumer == null && thing.TryGetComp<CompThreadRelay>() == null)
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
            yield return new Gizmo_ThreadBandwidth { node = this };
            if (DebugSettings.godMode)
            {
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
            if (!respawningAfterLoad)
            {
                overloadTimer = 0;
                isShutdownByInterference = false;
                brokenDownByOverload = false;
                CheckInterferenceAndShutdown();
            }
        }
        public override void CompTick()
        {
            if (!parent.Spawned)
                return;
            if (isShutdownByInterference)
                return;
            if (parent.IsBrokenDown())
                return;
            if (brokenDownByOverload && !parent.IsBrokenDown())
                brokenDownByOverload = false;
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
                        brokenDownByOverload = true;
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
            Scribe_Values.Look(ref isShutdownByInterference, "isShutdownByInterference", false);
            Scribe_Values.Look(ref overloadTimer, "overloadTimer", 0);
            Scribe_Values.Look(ref brokenDownByOverload, "brokenDownByOverload", false);
            Scribe_Values.Look(ref capacityOffset, "capacityOffset", 0);
            Scribe_Collections.Look(ref connectedThings, "connectedConsumerThings", LookMode.Reference);
        }
        public override string CompInspectStringExtra()
        {
            if (isShutdownByInterference)
                return "中枢干扰\n线程产出: 0\n已占用: 0";
            if (parent.IsBrokenDown())
                return "超载故障\n线程产出: 0\n已占用: 0";
            if (IsOverloaded)
            {
                if (!IsOnline)
                    return string.Format(
                        "编译线程产出: {0}\n已占用: {1}\n负载率: {2:P0}\n超载! 中枢离线，恢复电力后继续倒计时",
                        TotalCapacity, CurrentLoad, LoadRate);
                int seconds = (overloadTimer > 0 ? overloadTimer : Props.overloadCount) / 60;
                return string.Format(
                    "编译线程产出: {0}\n已占用: {1}\n负载率: {2:P0}\n超载! {3}秒后故障",
                    TotalCapacity,
                    CurrentLoad,
                    LoadRate,
                    seconds
                );
            }
            return string.Format(
                "编译线程产出: {0}\n已占用: {1}\n负载率: {2:P0}",
                TotalCapacity,
                CurrentLoad,
                LoadRate
            );
        }
    }
}