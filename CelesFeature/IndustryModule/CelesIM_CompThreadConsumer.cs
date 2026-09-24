using System.Collections.Generic;
using Verse;
using Verse.Sound;
using RimWorld;
using UnityEngine;

namespace CelesFeature
{
    public class CelesIM_CompProperties_ThreadConsumer : CompProperties
    {
        public int baseThreadsCost = 4;
        public float produceEffic = 1.0f;
        public bool canWorkOffline = true;
        public float offlineFactor = 0.5f;
        public bool haveWarmUpCount = false;
        public int warmUpTicks = 12500;
        public string overclockTech = "";
        public CelesIM_CompProperties_ThreadConsumer()
        {
            compClass = typeof(CelesIM_CompThreadConsumer);
        }
    }

    // 终端（2026-09-13 C'+D'：绑定图迁管理器 + 断电语义终版）
    //   状态机（三态 + 批2 注入关机）：活跃连接 / 无信号（绑定保留·恢复即回）/ 未连接（无绑定）
    //   预热 = 正交量非状态：活跃递减（与生产无关）/ 无信号冻结 / 未连接清零
    //   断电不解除绑定（ReceiveCompSignal PowerTurnedOff 无操作）——显示全派生
    public class CelesIM_CompThreadConsumer : ThingComp
    {
        public CelesIM_CompProperties_ThreadConsumer Props => (CelesIM_CompProperties_ThreadConsumer)props;

        // ── 本机持久状态（绑定经管理器——(b) 方案；字段 label 即存档接口）──
        private int warmUpRemaining;
        // 启用线程连接开关（2026-09-23 终端专属）：false=手动断开（守卫 TryConnect 咽喉——
        // 五类自动路径全拦）；B2 指派开关（flick 链）落地后操作同一字段
        private bool wantSwitchOn = true;

        // ── 运行期缓存 ──
        [Unsaved] private CompPowerTrader powerComp;

        // ── 公开访问器（管理器/渲染/派生态查询）──
        public int ThreadCost => Props.baseThreadsCost;
        public bool HasPowerNow => powerComp?.PowerOn ?? true;
        public bool IsWarmingUp => warmUpRemaining > 0;

        private CelesIM_ThreadNetworkManager Mgr =>
            parent == null || parent.Map == null ? null : CelesIM_ThreadNetworkManager.For(parent.Map);

        // ── 派生态查询（管理器图）──
        public bool IsConnected => Mgr != null && Mgr.IsBound(parent);
        public Thing BoundNode => Mgr?.GetBoundNode(parent);
        public bool IsActiveConnection => Mgr != null && Mgr.IsActiveConnection(this);
        // 「无信号」派生（绑定保留·链路不通）——保留旧属性名供既有读取方（Q7 公开面保持）
        public bool IsStandby => IsConnected && !IsActiveConnection;

        public float GetWorkSpeedFactor()
        {
            return 1.0f;   // 批 4 接入效率因子（E5）
        }

        // ============================================================
        //  事件（节点消失迁移路径——管理器级联触发，自动重试；超载级联断为另一路径，F 批实装）
        // ============================================================
        public void OnProducerLost()
        {
            Mgr?.UnbindConsumer(this);
            warmUpRemaining = 0;
            TryConnect();
        }

        // F 批：超载级联断路径（管理器 SeverSubtree 调用）——解绑 + 预热清零，**无重试**
        //（与 OnProducerLost 的迁移重试分立，Q5 终版）
        public void SeverFromNetwork()
        {
            Mgr?.UnbindConsumer(this);
            warmUpRemaining = 0;
        }

        // ============================================================
        //  批 2：预热递减（ECS——管理器 MapComponentTick 统一调度）
        //  三态裁决：递减 ⟺ 活跃连接（与生产无关）；无信号=冻结；断联清零由 Sever/Disconnect 覆盖
        // ============================================================
        internal void TickWarmup()
        {
            if (warmUpRemaining > 0 && Mgr != null && Mgr.IsActiveConnection(this))
                warmUpRemaining--;
        }

        // ============================================================
        //  连接（候选扫描走管理器注册表——算法与原全图版逐行等价，Q1）
        // ============================================================
        public void TryConnect(Thing excludeNode = null)
        {
            if (!wantSwitchOn) return;   // 手动断开守卫（单一咽喉——含建造/复电/唤醒/迁移/重置全路径）
            if (!parent.Spawned || !HasPowerNow)
                return;
            if (IsConnected)
                return;
            CelesIM_ThreadNetworkManager mgr = Mgr;
            if (mgr == null)
                return;
            List<Thing> candidates = mgr.CandidateNodesForConsumer(parent.Position, ThreadCost, excludeNode);
            if (candidates.Count == 0)
                return;
            mgr.BindConsumer(this, candidates[0]);
            if (Props.haveWarmUpCount)
                warmUpRemaining = Props.warmUpTicks;
        }

        public void Disconnect()
        {
            Mgr?.UnbindConsumer(this);
            warmUpRemaining = 0;
        }

        public void ResetConnection()
        {
            wantSwitchOn = true;   // 重置即复位手动断开守卫（重新允许连接）
            Thing prev = BoundNode;
            Disconnect();
            TryConnect(prev);
            if (!IsConnected)
                TryConnect();
        }

        // ── Gizmo ──
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // 启用线程连接开关（2026-09-23：镜像可接入 toggle 形状——Command_Toggle 范式
            // CompRefuelable.cs:356-371；isActive=连接意愿而非实际连接）
            yield return new Command_Toggle
            {
                defaultLabel = "CelesIM_Keyed_ThreadSwitchLabel".Translate(),
                defaultDesc = "CelesIM_Keyed_ThreadSwitchDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("Celes/UI/Icons/ThreadConnect"),
                isActive = () => wantSwitchOn,
                toggleAction = delegate
                {
                    wantSwitchOn = !wantSwitchOn;
                    if (wantSwitchOn)
                        TryConnect();
                    else
                        Disconnect();   // 解绑+预热清零（绑定遗忘）
                }
            };
            yield return new Command_Action
            {
                defaultLabel = "CelesIM_Keyed_ResetConnection".Translate(),
                defaultDesc = "CelesIM_Keyed_ResetConnectionDescTerminal".Translate(),
                icon = ContentFinder<Texture2D>.Get("Celes/UI/Icons/TryThreadConnect"),
                action = delegate
                {
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    ResetConnection();
                    Thing node = BoundNode;
                    if (node != null)
                    {
                        for (int i = 0; i < 5; i++)
                            FleckMaker.ThrowMetaPuff(node.DrawPos, node.Map);
                    }
                }
            };
            if (DebugSettings.godMode)
            {
                // 批 2 DEV（Q2 终案 2026-09-23）：仅复位手动断开守卫——不绕电力/候选选择/链路事实
                //（可用连接存在即真实绑定=自动切入正常连接状态；对齐原版 DEV: Toggle power on 直改+正常路径接管）
                yield return new Command_Action
                {
                    defaultLabel = "DEV: 视作已连接",
                    defaultDesc = "复位手动断开守卫并立即尝试连接。不绕过电力与候选检查——测试时请自行供电。",
                    action = delegate
                    {
                        wantSwitchOn = true;
                        TryConnect();
                        Log.Message("[CelesIM] DEV force-connect " + parent.def.defName + ": " +
                            (IsConnected ? "bound -> " + (BoundNode?.LabelCap ?? "?") : "no candidate"));
                    }
                };
            }
        }

        // ============================================================
        //  渲染（线色按派生活跃态：活跃=白 / 无信号=红）
        // ============================================================
        public override void PostDrawExtraSelectionOverlays()
        {
            if (!IsConnected || BoundNode == null)
                return;
            if (IsActiveConnection)
                GenDraw.DrawLineBetween(parent.TrueCenter(), BoundNode.TrueCenter());
            else
                GenDraw.DrawLineBetween(parent.TrueCenter(), BoundNode.TrueCenter(),
                    CompAffectedByFacilities.InactiveFacilityLineMat);
        }

        [Unsaved] private Material lackMat;
        [Unsaved] private bool lackMatTried;
        public override void PostDraw()
        {
            if (powerComp != null && !powerComp.PowerOn)
                return;   // 断电视觉由原版电力系统负责
            if (IsActiveConnection)
                return;
            if (!lackMatTried)
            {
                lackMat = MaterialPool.MatFrom("Celes/UI/Icons/LackThreadConnect", ShaderDatabase.Transparent);
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
            if (Mgr != null)
                Mgr.Notify_ConsumerSpawned(this);
            if (!respawningAfterLoad)
                warmUpRemaining = 0;
            // 读档门（2026-09-20 裁决）：读档不自发连接——保留档内状态；仅玩家主动（建造）触发入网
            if (!respawningAfterLoad && HasPowerNow && !IsConnected)
                TryConnect();
        }

        public override void ReceiveCompSignal(string signal)
        {
            switch (signal)
            {
                case "PowerTurnedOn":
                case "PowerTurnedOff":
                    // ECS 阶段1：统一转发管理器单入口（断电无操作=绑定保留派生态，由管理器分发）
                    Mgr?.Notify_PowerChanged(parent, signal == "PowerTurnedOn");
                    break;
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            // 注销与解绑由管理器 Deregister 阶段处理（map 为旧地图——此时 parent.Map 已空）
            CelesIM_ThreadNetworkManager mgr = map != null ? CelesIM_ThreadNetworkManager.For(map) : null;
            mgr?.Notify_ConsumerDespawned(this);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref warmUpRemaining, "warmUpRemaining", 0);
            Scribe_Values.Look(ref wantSwitchOn, "wantSwitchOn", true);
        }

        public override string CompInspectStringExtra()
        {
            string status;
            if (!IsConnected)
                status = "CelesIM_Keyed_StatusNotConnected".Translate();
            else if (!IsActiveConnection)
                status = "CelesIM_Keyed_StatusNoSignal".Translate();
            else
            {
                string nodeLabel = BoundNode?.LabelCap ?? " ";
                status = "CelesIM_Keyed_StatusConnected".Translate(nodeLabel);
                if (IsWarmingUp)
                {
                    // 批 2 修正：进度百分比（C# 预格式）+ 剩余时间走原版统一格式化器（GenDate:254）
                    int percent = Props.warmUpTicks > 0
                        ? Mathf.RoundToInt(100f * (1f - (float)warmUpRemaining / Props.warmUpTicks))
                        : 100;
                    status += "CelesIM_Keyed_WarmingUp".Translate(percent, warmUpRemaining.ToStringTicksToPeriod());
                }
            }
            return "CelesIM_Keyed_TerminalInspect".Translate(ThreadCost, status, GetWorkSpeedFactor().ToString("F2"));
        }
    }
}
