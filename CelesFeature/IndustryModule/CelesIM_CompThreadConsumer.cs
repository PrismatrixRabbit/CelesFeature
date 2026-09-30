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
        // ── 批 5 前置修订（2026-09-27 单源化）：haveWarmUpCount/warmUpTicks 废弃——
        // 预热时长唯一源=stat Celes_WarmUpDuration（defaultBaseValue 0=无预热；statBases 配 ticks）；
        // canWorkOffline→stat Celes_ThreadDependency（1=依赖/0=可离网——值义反转修订）；
        // offlineFactor→stat Celes_UnconnectedEfficiency（依赖型 default 0.1=预热起点，可离网型 statBases 配值）；
        // produceEfic 废弃（建筑倍速走原版 WorkTableWorkSpeedFactor 的 statBases）
        // 每 tick 冷却的预热进度（float 速率）。小数点不精确：≥1 截断取整为每 tick 流失 N；
        // (0,1) 倒数取整为每 ~1/rate tick 流失 1（0.5→每 2 tick 流失 1）；0=禁用渐降；负值 ConfigErrors 修复为默认
        public float warmUpDecayPerTick = 0.5f;

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef))
                yield return e;
            if (warmUpDecayPerTick < 0f)
            {
                yield return "[CelesIM] warmUpDecayPerTick 不能为负——已取默认 0.5（def: " + parentDef?.defName + "）";
                warmUpDecayPerTick = 0.5f;
            }
        }
        public string overclockTech = "";
        // ── 批 3 B3：连接状态标志（markTexPath 空 = 不启用；无绑定不显示；绑定+活跃=蓝/链不通=红）──
        public string markTexPath = "";                              // 标志贴图路径（XML 可配，不写死）
        public float linkHeight = 0f;                                // 标志 z+ 偏移（伪 3D 视觉高度）
        public float markDrawSize = 1.2f;                            // 标志显示尺寸（格）
        public Color markColorActive = new Color(0.4f, 0.7f, 1f);    // 蓝（与连线 lineColorActive 同值）
        public Color markColorBroken = new Color(1f, 0.25f, 0.2f);   // 红（与连线 lineColorBroken 同值）
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
        // DEV 虚拟连接态（批 4 终版裁决 2026-09-26）：视作连接到「DEV中枢」——绕过一切网络判定
        //（链路/电力/容量/范围）；Manager.IsActiveConnection 首行短路全链传导；真实绑定成功自动解除
        [Unsaved] internal bool devForcedActive;

        // ── 公开访问器（管理器/渲染/派生态查询）──
        public int ThreadCost => Props.baseThreadsCost;
        public bool HasPowerNow => powerComp?.PowerOn ?? true;

        // 预热时长（ticks）——唯一源=stat Celes_WarmUpDuration（单源化 2026-09-27：0=无预热）
        public int WarmUpDurationTicks => parent != null
            ? Mathf.CeilToInt(parent.GetStatValue(CelesIM_DefOf.Celes_WarmUpDuration))
            : 0;

        // 依赖型且未连接（DEV 虚拟连接豁免）——无法工作态（效率归零+文字提示的统一判定）
        public bool CannotWorkUnconnected =>
            !IsConnected && !devForcedActive
            && parent.GetStatValue(CelesIM_DefOf.Celes_ThreadDependency) >= 0.5f;   // 1=依赖

        public bool IsWarmingUp => WarmUpDurationTicks > 0 && warmUpRemaining > 0;

        private CelesIM_ThreadNetworkManager Mgr =>
            parent == null || parent.Map == null ? null : CelesIM_ThreadNetworkManager.For(parent.Map);

        // ── 派生态查询（管理器图）──
        public bool IsConnected => Mgr != null && Mgr.IsBound(parent);
        public Thing BoundNode => Mgr?.GetBoundNode(parent);
        public bool IsActiveConnection => Mgr != null && Mgr.IsActiveConnection(this);
        // 「无信号」派生（绑定保留·链路不通）——保留旧属性名供既有读取方（Q7 公开面保持）
        public bool IsStandby => IsConnected && !IsActiveConnection;

        // 批 4 终版（单式统一 2026-09-26）：效率=floor + (1−floor) × 热度——全态单一公式
        // （预热爬升/完成满效/未连接按余温/冷透钉底/无信号冷却滑落自然回归 floor，无满效无信号）；
        // 唯一起点源=stat Celes_UnconnectedEfficiency（依赖型 default 0.1，可离网型 statBases 配值）
        public float EfficiencyFloor => parent.GetStatValue(CelesIM_DefOf.Celes_UnconnectedEfficiency);

        public float WarmUpFactor => IsWarmingUp
            ? EfficiencyFloor + (1f - EfficiencyFloor) * (1f - (float)warmUpRemaining / WarmUpDurationTicks)
            : 1f;

        // 批 4（E5·B' 终案）：效率因子单一真相源=尘构机效率 stat 的 parts 乘法链——
        // 本方法转发 GetStatValue（postfix/Inspect/效率系数分解行 全部消费方经此单出口，公式零重复）；
        // 无循环依赖：Celes_ThreadLinkEfficiency 的计算链（produceEffic 基础值→两 part 乘）不触及本 stat
        public float GetWorkSpeedFactor()
        {
            return parent.GetStatValue(CelesIM_DefOf.Celes_ThreadLinkEfficiency);
        }

        // ============================================================
        //  事件（节点消失迁移路径——管理器级联触发，自动重试；超载级联断为另一路径，F 批实装）
        // ============================================================
        public void OnProducerLost()
        {
            Mgr?.UnbindConsumer(this);
            // 渐降：不清零——解绑后进度自然进入冷却态（TickWarmup 流失分支）
            TryConnect();
        }

        // F 批：超载级联断路径（管理器 SeverSubtree 调用）——解绑 + 预热冷透（写满值，Q5 强制冷却），**无重试**
        //（与 OnProducerLost 的迁移重试分立，Q5 终版）
        public void SeverFromNetwork()
        {
            Mgr?.UnbindConsumer(this);
            warmUpRemaining = WarmUpDurationTicks;
        }

        // ============================================================
        //  批 2 → 批 4 终版：预热调度（ECS——管理器 MapComponentTick 统一）
        //  两态（Q4「无信号冻结」改判 2026-09-26）：活跃连接=加热；其余一切（未连接+无信号）=冷却——
        //  无信号随冷却滑落自然回归 floor，不存在满效无信号
        // ============================================================
        internal void TickWarmup()
        {
            if (WarmUpDurationTicks <= 0)
                return;
            if (Mgr != null && Mgr.IsActiveConnection(this))
            {
                if (warmUpRemaining > 0)
                    warmUpRemaining--;                                   // 加热：剩余时间递减
            }
            else if (!IsConnected && warmUpRemaining < WarmUpDurationTicks)
            {
                // 冷却（仅未连接——无信号=冻结，裁决 2026-09-26 复位：绑定保留态进度不增不减）：
                // 速率双档换算（小数不精确——见 Props.warmUpDecayPerTick 注释；无状态直算）
                float rate = Props.warmUpDecayPerTick;
                if (rate >= 1f)
                    warmUpRemaining = Mathf.Min(WarmUpDurationTicks, warmUpRemaining + (int)rate);
                else if (rate > 0f && Find.TickManager.TicksGame % Mathf.Max(1, Mathf.RoundToInt(1f / rate)) == 0)
                    warmUpRemaining = Mathf.Min(WarmUpDurationTicks, warmUpRemaining + 1);
            }
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
            devForcedActive = false;   // 真实绑定接管虚拟态（直改+让位）
            // 渐降 v3：绑定不写 remaining——重连从当前冷热程度继续加热（首连冷态来自 PostSpawnSetup 出厂满值）
        }

        public void Disconnect()
        {
            devForcedActive = false;   // DEV 虚拟态随断开解除
            // 渐降 v3.1：解绑不动 remaining（一切断开路径统一冷却渐降——S2 遗忘语义作废；
            // 唯一强制冷却=Q5 超载级联走 SeverFromNetwork）
            Mgr?.UnbindConsumer(this);
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
                        Disconnect();   // 渐降 v3.1（裁决变更 2026-09-26）：toggle off 不再遗忘直清——
                                        // 与重置/拆除同待遇（冷却渐降，S2 遗忘语义作废；Q5 超载强制冷却保留）
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
                    defaultDesc = "虚拟连接到 DEV中枢，绕过链路/电力/容量/范围全部判定。再按解除；真实绑定成功时自动解除。",
                    action = delegate
                    {
                        devForcedActive = !devForcedActive;
                        Log.Message("[CelesIM] DEV force-active " + parent.def.defName + ": " + devForcedActive);
                    }
                };
                // 批 4 终版 DEV：预热状态直改（归零=字段清零→进度 100% 完成态；重置=回满值→重新预热起点）
                yield return new Command_Action
                {
                    defaultLabel = "DEV: 预热完成",
                    action = delegate
                    {
                        warmUpRemaining = 0;
                        Log.Message("[CelesIM] DEV warm-up zeroed (complete): " + parent.def.defName);
                    }
                };
                yield return new Command_Action
                {
                    defaultLabel = "DEV: 预热重置",
                    action = delegate
                    {
                        warmUpRemaining = WarmUpDurationTicks;
                        Log.Message("[CelesIM] DEV warm-up reset (cold start): " + parent.def.defName);
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
        // ── 批 3 B3：连接状态标志（与 Lack 脉动并存——两个不同含义：标志=连接状态，Lack=断连警示）──
        [Unsaved] private Material markMatActive;
        [Unsaved] private Material markMatBroken;
        [Unsaved] private bool markMatsTried;
        public override void PostDraw()
        {
            // 标志：绑定即显示（活跃=蓝/链不通=红，断电也红）——先于下方断电/活跃守卫
            DrawLinkMark();

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
            // R-IM1 原版范式（OverlayDrawer.cs:267）：thingIDNumber 相位散列错拍 + FadedMaterialPool 档位池化，共享材质只读零 SetColor
            float pulse = (Mathf.Sin((Time.realtimeSinceStartup + 397f * (parent.thingIDNumber % 571)) * 4f) + 1f) * 0.5f;
            float alpha = 0.35f + pulse * 0.6f;
            Vector3 iconPos = parent.DrawPos;
            iconPos.y += 0.3f;
            Graphics.DrawMesh(MeshPool.plane10,
                Matrix4x4.TRS(iconPos, Quaternion.identity, new Vector3(0.6f, 1f, 0.6f)),
                FadedMaterialPool.FadedVersionOf(lackMat, alpha), 0);
        }

        // 标志绘制（批 3 B3）：无绑定不显示；绑定+IsActiveConnection=蓝 / 链不通=红（含断电）；
        // 贴图路径 XML 配置（markTexPath 空=不启用）；材质 def 色惰性双份（渲染线程，坑#15）
        private void DrawLinkMark()
        {
            if (Props.markTexPath.NullOrEmpty() || (!IsConnected && !devForcedActive))
                return;
            if (!markMatsTried)
            {
                markMatActive = MaterialPool.MatFrom(Props.markTexPath, ShaderDatabase.MoteGlow, Props.markColorActive);
                markMatBroken = MaterialPool.MatFrom(Props.markTexPath, ShaderDatabase.MoteGlow, Props.markColorBroken);
                markMatsTried = true;
            }
            Material mat = IsActiveConnection ? markMatActive : markMatBroken;
            if (mat == null)
                return;
            Vector3 pos = parent.TrueCenter();
            pos.z += Props.linkHeight;                            // 伪 3D 视觉高度（z+ 偏移——轴语义实证）
            pos.y = AltitudeLayer.MetaOverlays.AltitudeFor();    // 与连线同排序层（不被建筑遮）
            Graphics.DrawMesh(MeshPool.plane10,
                Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(Props.markDrawSize, 1f, Props.markDrawSize)),
                mat, 0);
        }

        // ── Lifecycle ──
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.GetComp<CompPowerTrader>();
            if (Mgr != null)
                Mgr.Notify_ConsumerSpawned(this);
            if (!respawningAfterLoad)
                warmUpRemaining = WarmUpDurationTicks;   // 渐降 v3：新建=冷态出厂（满值=进度 0%；无预热 comp 写满无碍——IsWarmingUp 防御已滤）
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

        // 冷却进度行（未连接/无信号共用——渐降可感知）：复用连接态同键同式（百分比+预计占位）；
        // 两端稳态对称隐藏（完成 0 / 冷透满值返回空）
        private string CoolingStatusPart()
        {
            if (WarmUpDurationTicks <= 0 || warmUpRemaining <= 0 || warmUpRemaining >= WarmUpDurationTicks)
                return "";
            int coolPercent = Mathf.RoundToInt(100f * (1f - (float)warmUpRemaining / WarmUpDurationTicks));
            return "CelesIM_Keyed_WarmingUp".Translate(coolPercent, "CelesIM_Keyed_WarmUpEtaNA".Translate());
        }

        public override string CompInspectStringExtra()
        {
            string status;
            if (devForcedActive)
            {
                status = "CelesIM_Keyed_StatusConnected".Translate("DEV中枢");   // DEV 豁免字面
                if (IsWarmingUp)
                {
                    int percent = WarmUpDurationTicks > 0
                        ? Mathf.RoundToInt(100f * (1f - (float)warmUpRemaining / WarmUpDurationTicks))
                        : 100;
                    status += "CelesIM_Keyed_WarmingUp".Translate(percent, warmUpRemaining.ToStringTicksToPeriod());
                }
            }
            else if (!IsConnected)
            {
                status = "CelesIM_Keyed_StatusNotConnected".Translate();
                status += CoolingStatusPart();   // 余温冷却行（渐降可感知）
            }
            else if (!IsActiveConnection)
            {
                status = "CelesIM_Keyed_StatusNoSignal".Translate();
                status += CoolingStatusPart();   // 批 4 终版：无信号同冷却（Q4 冻结改判）——滑落可见
            }
            else
            {
                string nodeLabel = BoundNode?.LabelCap ?? " ";
                status = "CelesIM_Keyed_StatusConnected".Translate(nodeLabel);
                if (IsWarmingUp)
                {
                    // 批 2 修正：进度百分比（C# 预格式）+ 剩余时间走原版统一格式化器（GenDate:254）
                    int percent = WarmUpDurationTicks > 0
                        ? Mathf.RoundToInt(100f * (1f - (float)warmUpRemaining / WarmUpDurationTicks))
                        : 100;
                    status += "CelesIM_Keyed_WarmingUp".Translate(percent, warmUpRemaining.ToStringTicksToPeriod());
                }
            }
            // 「尘构机效率: x50%（未连接, 预热中）」——x 前缀在键内字面（此值=纯百分比，勿再拼 x——
            // 曾双 x 叠加返工）；括号原因=原版 CompReportWorkSpeed 行同构；
            // 值与详情报文（StatWorker）/工作速度乘数（postfix）三处同源 GetWorkSpeedFactor
            // 依赖型未连接=无法工作（文字替代数值——计算层同步归零，裁决 2026-09-27）
            if (CannotWorkUnconnected)
                return "CelesIM_Keyed_TerminalInspect".Translate(ThreadCost, status,
                    "CelesIM_Keyed_CannotWorkUnlinked".Translate());
            string factorText = GetWorkSpeedFactor().ToStringPercent();
            // 括号原因=顺序互斥（照 StatPart 信息行同款裁决 2026-09-26）：
            // 未连接→「未连接」（冷却/余温的身份语境）；连接预热中→「预热中」；无信号→由状态行承担
            string reasons;
            if (!IsConnected && !devForcedActive)
                reasons = "CelesIM_Keyed_StatusNotConnected".Translate();
            else if (IsWarmingUp)
                reasons = "CelesIM_Keyed_ReasonWarming".Translate();
            else
                reasons = "";
            if (reasons.Length > 0)
                factorText += "（" + reasons + "）";
            return "CelesIM_Keyed_TerminalInspect".Translate(ThreadCost, status, factorText);
        }
    }
}
