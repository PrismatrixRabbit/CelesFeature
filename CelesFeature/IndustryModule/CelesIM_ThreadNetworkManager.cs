using System.Collections.Generic;
using Verse;

namespace CelesFeature
{
    // ═══════════════════════════════════════════════════════════════════════════
    // CelesIM_ThreadNetworkManager — 编译线程网络管理器（批 1 核心，E17/策划案 v5.1 §0）
    //
    // 架构：注册表 + 绑定图 + 反向索引 + 派生查询；候选扫描走注册表（O(注册数)，消灭全图遍历）。
    //   注：注册/注销为同步 O(1) 操作——PowerNetManager 的延迟队列存在理由是净重算昂贵，
    //   本场景不成立；且同步消除"同 tick 放置即连"竞态、与原版 PostDeSpawn 级联时序等价
    //   （2026-09-13 落码时定案，偏差已登记执行要点）。
    //
    // 数据所有权（2026-09-13 (b) 方案裁决 + 豁免：IM 线程三件套未发布，无玩家存档）：
    //   绑定图（consumerToNode / relayToParent）= 本管理器唯一真相源，随存档持久化；
    //   反向索引（directChildren）= 运行期派生，FinalizeInit 与变更时维护；
    //   Comp 侧只保留 预热/accessibleMode/超载计时/容量偏移 等本机状态。
    //
    // 语义基准（2026-09-13 终版裁决）：
    //   绑定（持久）   ：断电不解除；解除 = 手动 / 节点消失迁移 / 超载级联断（F 批）
    //   活跃连接（派生）：绑定 ∧ 全链有电 ∧ 中枢在线；「无信号」= 绑定在链路不通（恢复即回）
    //   预热（正交量） ：活跃=递减（与生产无关）/ 无信号=冻结 / 未连接=0——非独立状态
    //   递归深度       ：无上限（绑定防环保证有界）；depth>1000 仅为图损坏告警断言
    //
    // 分步启用：本批（C'+D'）＝图迁移+断电语义；E＝干扰存在数化；F＝超载维修恢复+级联断
    // ═══════════════════════════════════════════════════════════════════════════
    public class CelesIM_ThreadNetworkManager : MapComponent
    {
        // MapComponent 仅有 Map 形参构造（MapComponent.cs:7-10）
        public CelesIM_ThreadNetworkManager(Map map) : base(map) { }

        // 取或建（Map.GetComponent 不自动创建——Map.cs:1205-1215 实证；mod 标准注册路径）
        public static CelesIM_ThreadNetworkManager For(Map map)
        {
            CelesIM_ThreadNetworkManager m = map.GetComponent<CelesIM_ThreadNetworkManager>();
            if (m == null)
            {
                m = new CelesIM_ThreadNetworkManager(map);
                map.components.Add(m);
            }
            return m;
        }

        // ── 注册表（[Unsaved]——运行期由 Spawn/Despawn 同步维护；FinalizeInit 全量重建兜底）──
        [Unsaved] private readonly List<CelesIM_CompThreadProducer> hubs = new List<CelesIM_CompThreadProducer>();
        [Unsaved] private readonly List<CelesIM_CompThreadRelay> relays = new List<CelesIM_CompThreadRelay>();
        [Unsaved] private readonly List<CelesIM_CompThreadConsumer> consumers = new List<CelesIM_CompThreadConsumer>();

        // ── 绑定图（唯一真相源；存档持久化。key/value 均 Thing 引用经 loadID 解析）──
        private Dictionary<Thing, Thing> consumerToNode = new Dictionary<Thing, Thing>();
        private Dictionary<Thing, Thing> relayToParent = new Dictionary<Thing, Thing>();

        // ── 反向索引（派生：节点 → 直接子级；FinalizeInit 重建 + 绑定变更维护）──
        [Unsaved] private readonly Dictionary<Thing, List<Thing>> directChildren = new Dictionary<Thing, List<Thing>>();

        // ═══ 只读注册表暴露（G：双 Alert 数据源；I：PlaceWorker 预览与唤醒扫描——替代全图遍历）═══
        public List<CelesIM_CompThreadProducer> HubsForReading => hubs;
        public List<CelesIM_CompThreadRelay> RelaysForReading => relays;
        public List<CelesIM_CompThreadConsumer> ConsumersForReading => consumers;

        // ═══ 注册查询 ═══

        public int ExistingHubCount => hubs.Count;

        // E9：thingIDNumber 升序首个存在的中枢 = 活跃中枢（存在数口径；断电/故障者同样占位）
        public CelesIM_CompThreadProducer ActiveCore
        {
            get
            {
                CelesIM_CompThreadProducer best = null;
                foreach (CelesIM_CompThreadProducer hub in hubs)
                {
                    if (hub == null || hub.parent == null || !hub.parent.Spawned) continue;
                    if (best == null || hub.parent.thingIDNumber < best.parent.thingIDNumber)
                        best = hub;
                }
                return best;
            }
        }

        public bool IsOnlineHub(Thing thing)
        {
            CelesIM_CompThreadProducer hub = thing?.TryGetComp<CelesIM_CompThreadProducer>();
            return hub != null && hub.IsOnline;
        }

        public bool IsInterferenceShutdown(Thing thing)
        {
            CelesIM_CompThreadProducer hub = thing?.TryGetComp<CelesIM_CompThreadProducer>();
            return hub != null && hub.IsShutdownByInterference;
        }

        // ═══ 绑定图查询 ═══

        public Thing GetBoundNode(Thing consumerThing) =>
            consumerToNode.TryGetValue(consumerThing, out Thing node) ? node : null;

        public Thing GetRelayParent(Thing relayThing) =>
            relayToParent.TryGetValue(relayThing, out Thing parent) ? parent : null;

        public bool IsBound(Thing consumerThing) => consumerToNode.ContainsKey(consumerThing);
        public bool IsBoundToParent(Thing relayThing) => relayToParent.ContainsKey(relayThing);

        public List<Thing> GetDirectChildren(Thing node)
        {
            return directChildren.TryGetValue(node, out List<Thing> list) ? list : null;
        }

        // 中枢负载 = 直连终端 ThreadCost + 直连中继子树负载（无缓存——2026-09-13 Q2 裁决）
        public int LoadOf(Thing hubThing)
        {
            if (hubThing == null) return 0;
            CelesIM_CompThreadProducer hub = hubThing.TryGetComp<CelesIM_CompThreadProducer>();
            if (hub == null) return 0;
            int load = 0;
            List<Thing> children = GetDirectChildren(hubThing);
            if (children == null) return 0;
            for (int i = 0; i < children.Count; i++)
            {
                Thing child = children[i];
                if (child == null) continue;
                CelesIM_CompThreadConsumer consumer = child.TryGetComp<CelesIM_CompThreadConsumer>();
                if (consumer != null) { load += consumer.ThreadCost; continue; }
                load += SubtreeLoad(child);
            }
            return load;
        }

        // 中继子树负载（递归无上限——绑定时防环保证有界；depth 断言仅图损坏告警）
        public int SubtreeLoad(Thing relayThing) => SubtreeLoadInner(relayThing, 0);

        // ═══ 批 2：负载拆双段（活跃/无信号——Gizmo 嵌套格数据源；SubtreeLoad 同形扩展）═══

        public void LoadOfSplit(Thing hubThing, out int activeLoad, out int noSignalLoad)
        {
            activeLoad = 0;
            noSignalLoad = 0;
            if (hubThing == null) return;
            SplitChildren(hubThing, ref activeLoad, ref noSignalLoad);
        }

        private void SplitChildren(Thing node, ref int activeLoad, ref int noSignalLoad)
        {
            List<Thing> children = GetDirectChildren(node);
            if (children == null) return;
            for (int i = 0; i < children.Count; i++)
            {
                Thing child = children[i];
                if (child == null) continue;
                CelesIM_CompThreadConsumer consumer = child.TryGetComp<CelesIM_CompThreadConsumer>();
                if (consumer != null)
                {
                    if (consumer.IsActiveConnection) activeLoad += consumer.ThreadCost;
                    else noSignalLoad += consumer.ThreadCost;
                    continue;
                }
                CelesIM_CompThreadRelay relay = child.TryGetComp<CelesIM_CompThreadRelay>();
                if (relay != null)
                {
                    if (relay.IsStandby)
                        noSignalLoad += SubtreeLoad(child);          // 链断=整树无信号
                    else
                        SplitChildren(child, ref activeLoad, ref noSignalLoad);  // 链活=按子级分摊
                }
            }
        }

        private int SubtreeLoadInner(Thing node, int depth)
        {
            if (depth > 1000)
            {
                Log.Error("[CelesIM] Thread network graph depth exceeded 1000 (cycle guard) at " + node.ToStringSafe());
                return 0;
            }
            int sum = 0;
            List<Thing> children = GetDirectChildren(node);
            if (children == null) return 0;
            for (int i = 0; i < children.Count; i++)
            {
                Thing child = children[i];
                if (child == null) continue;
                CelesIM_CompThreadConsumer consumer = child.TryGetComp<CelesIM_CompThreadConsumer>();
                if (consumer != null) { sum += consumer.ThreadCost; continue; }
                sum += SubtreeLoadInner(child, depth + 1);
            }
            return sum;
        }

        // ═══ 活跃连接判定（派生态唯一出口）═══

        // 链路判定：节点上溯可达活跃中枢（中枢=自身 IsOnline；中继=本机有电 ∧ 父链递归）
        public bool IsNodeChainActive(Thing node)
        {
            if (node == null || !node.Spawned) return false;
            CelesIM_CompThreadProducer hub = node.TryGetComp<CelesIM_CompThreadProducer>();
            if (hub != null) return hub.IsOnline;
            CelesIM_CompThreadRelay relay = node.TryGetComp<CelesIM_CompThreadRelay>();
            return relay != null && relay.HasPowerNow && IsNodeChainActive(GetRelayParent(node));
        }

        // 终端活跃连接：绑定 ∧ 链路活跃 ∧ 终端有电
        public bool IsActiveConnection(CelesIM_CompThreadConsumer consumer)
        {
            if (consumer == null || consumer.parent == null) return false;
            Thing node = GetBoundNode(consumer.parent);
            return node != null && consumer.HasPowerNow && IsNodeChainActive(node);
        }

        // 中继活跃：上行绑定存在 ∧ 本机有电 ∧ 父链活跃（干扰旗标由 Comp 侧 IsOnline 叠加——E 批并入）
        public bool IsActiveRelay(CelesIM_CompThreadRelay relay)
        {
            if (relay == null || relay.parent == null) return false;
            Thing parent = GetRelayParent(relay.parent);
            return parent != null && relay.HasPowerNow && IsNodeChainActive(parent);
        }

        // ═══ 候选扫描（原 Comp.TryConnect 全图扫描的注册表版——算法逐行等价迁移，Q1 裁决）═══

        // 终端候选：中枢优先(0)→中继(1)，距离平方升序；容量/在线/可接入过滤
        public List<Thing> CandidateNodesForConsumer(IntVec3 fromPos, int threadCost, Thing excludeNode)
        {
            var result = new List<(Thing node, int priority, float distSq)>();
            foreach (CelesIM_CompThreadProducer hub in hubs)
            {
                if (hub == null || hub.parent == null || !hub.parent.Spawned || hub.parent == excludeNode) continue;
                if (!hub.IsOnline) continue;
                float distSq = DistanceSquared(fromPos, hub.parent.Position);
                if (distSq > hub.Props.connectRadius * hub.Props.connectRadius) continue;
                if (!hub.CanAcceptConnection(threadCost)) continue;
                result.Add((hub.parent, 0, distSq));
            }
            foreach (CelesIM_CompThreadRelay relay in relays)
            {
                if (relay == null || relay.parent == null || !relay.parent.Spawned || relay.parent == excludeNode) continue;
                if (!relay.IsOnline || !relay.IsAccessible) continue;
                float distSq = DistanceSquared(fromPos, relay.parent.Position);
                if (distSq > relay.Props.connectRadius * relay.Props.connectRadius) continue;
                if (relay.ConnectionCount >= relay.TotalCapacity) continue;
                result.Add((relay.parent, 1, distSq));
            }
            result.Sort((a, b) => a.priority != b.priority ? a.priority.CompareTo(b.priority) : a.distSq.CompareTo(b.distSq));
            var nodes = new List<Thing>(result.Count);
            for (int i = 0; i < result.Count; i++) nodes.Add(result[i].node);
            return nodes;
        }

        // 中继上行候选：中枢优先(0)→中继(1)，距离平方升序；双方半径取大 + 防环
        public List<Thing> CandidateNodesForRelay(CelesIM_CompThreadRelay requester, Thing excludeNode)
        {
            var result = new List<(Thing node, int priority, float distSq)>();
            IntVec3 fromPos = requester.parent.Position;
            float selfRadius = requester.Props.connectRadius;
            foreach (CelesIM_CompThreadProducer hub in hubs)
            {
                if (hub == null || hub.parent == null || !hub.parent.Spawned || hub.parent == excludeNode) continue;
                if (!hub.IsOnline) continue;
                float maxRadius = selfRadius > hub.Props.connectRadius ? selfRadius : hub.Props.connectRadius;
                if (!fromPos.InHorDistOf(hub.parent.Position, maxRadius)) continue;
                if (WouldCreateCycle(requester.parent, hub.parent)) continue;
                result.Add((hub.parent, 0, fromPos.DistanceToSquared(hub.parent.Position)));
            }
            foreach (CelesIM_CompThreadRelay relay in relays)
            {
                if (relay == null || relay.parent == null || !relay.parent.Spawned || relay.parent == excludeNode) continue;
                if (!relay.IsOnline) continue;
                if (relay.ConnectionCount >= relay.TotalCapacity) continue;
                float maxRadius = selfRadius > relay.Props.connectRadius ? selfRadius : relay.Props.connectRadius;
                if (!fromPos.InHorDistOf(relay.parent.Position, maxRadius)) continue;
                if (WouldCreateCycle(requester.parent, relay.parent)) continue;
                result.Add((relay.parent, 1, fromPos.DistanceToSquared(relay.parent.Position)));
            }
            result.Sort((a, b) => a.priority != b.priority ? a.priority.CompareTo(b.priority) : a.distSq.CompareTo(b.distSq));
            var nodes = new List<Thing>(result.Count);
            for (int i = 0; i < result.Count; i++) nodes.Add(result[i].node);
            return nodes;
        }

        // 防环：候选的祖先链若含 requester 则拒绝（无环森林保证 → 递归天然有界，Q2 裁决）
        public bool WouldCreateCycle(Thing requester, Thing candidate)
        {
            int safety = 0;
            for (Thing cursor = candidate; cursor != null && safety++ < 1000; )
            {
                if (cursor == requester) return true;
                cursor = GetRelayParent(cursor);
            }
            return false;
        }

        private static float DistanceSquared(IntVec3 a, IntVec3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        // ═══ 绑定变更（唯一入口；维护正向字典 + 反向索引）═══

        public void BindConsumer(CelesIM_CompThreadConsumer consumer, Thing node)
        {
            if (consumer == null || consumer.parent == null || node == null) return;
            UnbindConsumer(consumer);
            consumerToNode[consumer.parent] = node;
            AddChild(node, consumer.parent);
        }

        public void UnbindConsumer(CelesIM_CompThreadConsumer consumer)
        {
            if (consumer == null || consumer.parent == null) return;
            if (consumerToNode.TryGetValue(consumer.parent, out Thing node))
            {
                consumerToNode.Remove(consumer.parent);
                RemoveChild(node, consumer.parent);
            }
        }

        public void BindRelay(CelesIM_CompThreadRelay relay, Thing parent)
        {
            if (relay == null || relay.parent == null || parent == null) return;
            UnbindRelay(relay);
            relayToParent[relay.parent] = parent;
            AddChild(parent, relay.parent);
        }

        public void UnbindRelay(CelesIM_CompThreadRelay relay)
        {
            if (relay == null || relay.parent == null) return;
            if (relayToParent.TryGetValue(relay.parent, out Thing parent))
            {
                relayToParent.Remove(relay.parent);
                RemoveChild(parent, relay.parent);
            }
        }

        private void AddChild(Thing node, Thing child)
        {
            if (!directChildren.TryGetValue(node, out List<Thing> list))
            {
                list = new List<Thing>();
                directChildren[node] = list;
            }
            if (!list.Contains(child)) list.Add(child);
        }

        private void RemoveChild(Thing node, Thing child)
        {
            if (directChildren.TryGetValue(node, out List<Thing> list))
            {
                list.Remove(child);
                if (list.Count == 0) directChildren.Remove(node);
            }
        }

        // ═══ 注册/注销（同步 O(1)；注销含节点消失级联——迁移路径，与超载级联断不同路）═══
        // hub 注册/注销尾部触发干扰重算（存在数口径——E 批）
        public void Notify_HubSpawned(CelesIM_CompThreadProducer hub)
        {
            if (hub != null && !hubs.Contains(hub)) hubs.Add(hub);
            RecalcInterference();
        }

        public void Notify_HubDespawned(CelesIM_CompThreadProducer hub)
        {
            if (hub == null) return;
            hubs.Remove(hub);
            CascadeChildrenLost(hub.parent);
            RecalcInterference();
        }

        public void Notify_RelaySpawned(CelesIM_CompThreadRelay relay)
        {
            if (relay != null && !relays.Contains(relay)) relays.Add(relay);
        }

        public void Notify_RelayDespawned(CelesIM_CompThreadRelay relay)
        {
            if (relay == null) return;
            relays.Remove(relay);
            UnbindRelay(relay);
            CascadeChildrenLost(relay.parent);
        }

        public void Notify_ConsumerSpawned(CelesIM_CompThreadConsumer consumer)
        {
            if (consumer != null && !consumers.Contains(consumer)) consumers.Add(consumer);
        }

        public void Notify_ConsumerDespawned(CelesIM_CompThreadConsumer consumer)
        {
            if (consumer == null) return;
            consumers.Remove(consumer);
            UnbindConsumer(consumer);
        }

        // ═══ E 批：干扰存在数重算（2026-09-13 终版口径）═══
        // 非数值纯停机；thingIDNumber 升序首个存在者为活跃（断电/故障者同样占位），
        // 其余全部停机（订阅者经派生态显「无信号」，绑定保留）；活跃者恢复时经 TryAutoWake
        // 唤醒无绑定者（抑制消费点 4——见 Producer 抑制算法）
        public void RecalcInterference()
        {
            CelesIM_CompThreadProducer active = ActiveCore;
            foreach (CelesIM_CompThreadProducer hub in hubs)
            {
                if (hub == null || hub.parent == null || !hub.parent.Spawned) continue;
                bool shouldStop = hub != active;
                if (shouldStop && !hub.IsShutdownByInterference)
                    hub.SetInterferenceShutdown(true);
                else if (!shouldStop && hub.IsShutdownByInterference)
                {
                    hub.SetInterferenceShutdown(false);
                    hub.TryAutoWake();
                }
            }
        }

        // 故障中枢清单（WorkGiver 数据源——注册表过滤，零全图扫描）
        public List<Thing> FaultedHubsForReading
        {
            get
            {
                var list = new List<Thing>();
                foreach (CelesIM_CompThreadProducer hub in hubs)
                    if (hub != null && hub.parent != null && hub.parent.Spawned && hub.FaultActive)
                        list.Add(hub.parent);
                return list;
            }
        }

        // ═══ F 批：超载全子树级联断（Q5 终版——与节点消失迁移不同路：无重试、预热清零）═══
        public void SeverSubtree(Thing node)
        {
            if (node == null) return;
            List<Thing> children = GetDirectChildren(node);
            if (children == null) return;
            for (int i = children.Count - 1; i >= 0; i--)
            {
                Thing child = children[i];
                if (child == null) { children.RemoveAt(i); continue; }
                CelesIM_CompThreadConsumer consumer = child.TryGetComp<CelesIM_CompThreadConsumer>();
                if (consumer != null) { consumer.SeverFromNetwork(); continue; }
                CelesIM_CompThreadRelay relay = child.TryGetComp<CelesIM_CompThreadRelay>();
                if (relay != null) { UnbindRelay(relay); SeverSubtree(child); }
            }
        }

        // 节点消失级联：直接子级全部解绑并通知（OnProducerLost → 自动重试迁移）
        private void CascadeChildrenLost(Thing node)
        {
            if (node == null) return;
            List<Thing> children = GetDirectChildren(node);
            if (children == null) return;
            for (int i = children.Count - 1; i >= 0; i--)
            {
                Thing child = children[i];
                if (child == null) { children.RemoveAt(i); continue; }
                CelesIM_CompThreadConsumer consumer = child.TryGetComp<CelesIM_CompThreadConsumer>();
                if (consumer != null) { UnbindConsumer(consumer); consumer.OnProducerLost(); continue; }
                CelesIM_CompThreadRelay relay = child.TryGetComp<CelesIM_CompThreadRelay>();
                if (relay != null) { UnbindRelay(relay); relay.OnProducerLost(); }
            }
        }

        // ═══ ECS 阶段1：电力信号单入口（三 Comp 转发，逻辑集中调度）═══
        // 复电路径保留（2026-09-20 (ii) 裁决：因果=玩家拉闸/建造供电/维修，与 Q6/F3 一致）；
        // 断电无操作（绑定保留——显示派生）
        public void Notify_PowerChanged(Thing thing, bool poweredOn)
        {
            if (!poweredOn || thing == null)
                return;
            CelesIM_CompThreadProducer hub = thing.TryGetComp<CelesIM_CompThreadProducer>();
            if (hub != null)
            {
                if (hub.IsOnline)
                    hub.TryAutoWake();   // 抑制消费点 3
                return;
            }
            CelesIM_CompThreadConsumer consumer = thing.TryGetComp<CelesIM_CompThreadConsumer>();
            if (consumer != null)
            {
                if (!IsBound(thing))
                    consumer.TryConnect();
                return;
            }
            CelesIM_CompThreadRelay relay = thing.TryGetComp<CelesIM_CompThreadRelay>();
            if (relay != null)
            {
                if (!IsBoundToParent(thing))
                    relay.TryConnectToParent();
            }
        }

        // ═══ 帧调度（ECS 阶段1：超载推进 + 预热递减统一在此——comp 零 tick 职责）═══

        public override void MapComponentTick()
        {
            for (int i = 0; i < hubs.Count; i++)
                hubs[i]?.TickOverload();
            // 批 2：预热三态递减（活跃连接且 remaining>0 → 递减；无信号冻结；断联清零由 Sever/Disconnect 覆盖）
            for (int i = 0; i < consumers.Count; i++)
                consumers[i]?.TickWarmup();
        }

        // ═══ 载入收尾（FinalizeInit：注册表全量重建 + 反向索引重建 + 绑定健全性清理）═══

        public override void FinalizeInit()
        {
            hubs.Clear(); relays.Clear(); consumers.Clear();
            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                CelesIM_CompThreadProducer hub = buildings[i].TryGetComp<CelesIM_CompThreadProducer>();
                if (hub != null) { hubs.Add(hub); continue; }
                CelesIM_CompThreadRelay relay = buildings[i].TryGetComp<CelesIM_CompThreadRelay>();
                if (relay != null) { relays.Add(relay); continue; }
                CelesIM_CompThreadConsumer consumer = buildings[i].TryGetComp<CelesIM_CompThreadConsumer>();
                if (consumer != null) consumers.Add(consumer);
            }
            RebuildIndexesAndSanitize();
            // 对账重连已取消（2026-09-20 裁决：读档保留档内状态，不被动重构——
            // 超载被断/手动断开设备读档后保持未绑定，仅玩家主动行为触发连接）
            RecalcInterference();   // 读档终态干扰重算（[Unsaved] 停机旗标重导出）
            Log.Message("[CelesIM] ThreadNetwork FinalizeInit: " + DebugString());
        }

        private void RebuildIndexesAndSanitize()
        {
            directChildren.Clear();
            SanitizeDict(consumerToNode);
            SanitizeDict(relayToParent);
            foreach (KeyValuePair<Thing, Thing> kv in consumerToNode) AddChild(kv.Value, kv.Key);
            foreach (KeyValuePair<Thing, Thing> kv in relayToParent) AddChild(kv.Value, kv.Key);
        }

        private static void SanitizeDict(Dictionary<Thing, Thing> dict)
        {
            List<Thing> dead = null;
            foreach (KeyValuePair<Thing, Thing> kv in dict)
            {
                if (kv.Key == null || kv.Key.Destroyed || !kv.Key.Spawned ||
                    kv.Value == null || kv.Value.Destroyed || !kv.Value.Spawned)
                {
                    if (dead == null) dead = new List<Thing>();
                    dead.Add(kv.Key);
                }
            }
            if (dead != null)
                for (int i = 0; i < dead.Count; i++) dict.Remove(dead[i]);
        }

        // ═══ 存档 ═══

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref consumerToNode, "consumerToNode", LookMode.Reference, LookMode.Reference);
            Scribe_Collections.Look(ref relayToParent, "relayToParent", LookMode.Reference, LookMode.Reference);
        }

        // ═══ 诊断（dev）═══

        public string DebugString()
        {
            return $"[CelesIM] ThreadNetwork: hubs={hubs.Count} relays={relays.Count} consumers={consumers.Count} " +
                   $"activeCore={ActiveCore?.parent.LabelCap ?? "无"} bindings(c2n)={consumerToNode.Count} (r2p)={relayToParent.Count}";
        }
    }
}
