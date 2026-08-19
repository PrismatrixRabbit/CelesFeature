using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace CelesFeature
{
    // 发射平台待机/发射双档耗电 + 前摇状态机（裁决 2026-08-10，电力参数原版化 2026-08-11）：
    // Idle → Charging（前摇 10s，PowerOutput 切发射耗电暂态大负载，每 tick 递减计数器——快进=暂停）
    //   ├─ 被断电（轮询平台 CompPowerTrader.PowerOn）→ 恢复待机耗电 → 消息"发射中断" → Idle（装载保留）
    //   └─ 前摇结束 → 恢复待机耗电 → TryLaunch(最近信标站, null)（G6 换真实 arrival action）
    // 发射目标：自动最近信标站直发（不打开世界选点）；按钮禁用：无站/超距/层不可达/未装填
    // 电力参数单一来源 = 平台原版 CompPowerTrader（原版双档语义）：
    //   idlePowerDraw = 待机/常态耗电（前摇恢复值）；basePowerConsumption = 发射/工作态耗电（前摇暂态）；
    //   前摇时长 = 平台轻量 CelesFD_CompLaunchCharge（唯一自定义——原版无"暂态时长"概念）
    public class CelesFD_CompLaunchable : CompLaunchable_TransportPod
    {
        private bool charging;
        private int ticksRemaining;

        private new CelesFD_CompProperties_Launchable Props => (CelesFD_CompProperties_Launchable)props;

        public bool Charging => charging;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref charging, "charging", defaultValue: false);
            Scribe_Values.Look(ref ticksRemaining, "ticksRemaining", 0);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!charging)
            {
                return;
            }
            if (!parent.Spawned)
            {
                CancelCharging();
                return;
            }
            // 防御（2026-08-15）：前摇中若电网事件（PowerNetManager SetUpPowerVars 等）把 PowerOutput 覆盖为
            // 非发射耗电（如新增负载触发电网重建）→ 下 tick 恢复发射态；正常前摇中恒为发射态，条件不触发零开销
            CompPowerTrader padPc = PadPower;
            if (padPc != null && padPc.PowerOutput > -padPc.Props.PowerConsumption + 1f)
                SetPadPower(padPc.Props.PowerConsumption);
            // 前摇中装载状态被破坏（取消装载/卸载 → groupID=-1）→ 取消前摇（防 TryLaunch 报 "not in any group"）
            if (!Transporter.LoadingInProgressOrReadyToLaunch)
            {
                CancelCharging();
                return;
            }
            // 轮询组内所有舱对应平台 PowerOn（前摇期间被电网随机关电 → 取消）
            if (!AllPadsPowered())
            {
                CancelCharging();
                Messages.Message("CelesFD_ChargeInterrupted".Translate(), parent, MessageTypeDefOf.NegativeEvent, historical: false);
                return;
            }
            ticksRemaining--;
            if (ticksRemaining <= 0)
            {
                CancelCharging();
                LaunchToNearestStation();
            }
        }

        public override string CompInspectStringExtra()
        {
            // 仅前摇中显示充能；电力不足等禁用原因只在 gizmo 悬浮窗提示（用户裁决：不必要在建筑信息中显示）
            if (charging)
            {
                return "CelesFD_LaunchChargingInspect".Translate(ticksRemaining.ToStringTicksToPeriod());
            }
            return base.CompInspectStringExtra();
        }

        // 发射按钮：替换原版世界选点发射（不调用 base.CompGetGizmosExtra）
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            Command_Action command_Action = new Command_Action
            {
                defaultLabel = "CelesFD_LaunchToFaction".Translate(),
                defaultDesc = "CelesFD_LaunchToFactionDesc".Translate(),
                icon = CompLaunchable.LaunchCommandTex,
                action = delegate
                {
                    if (charging)
                    {
                        return;
                    }
                    // 点击时二次校验（站存在/距离/层可达）——防静默失败
                    if (!TryGetNearestStation(out _, out _, out _))
                    {
                        Messages.Message("CelesFD_NoBeaconStation".Translate(), parent, MessageTypeDefOf.RejectInput, historical: false);
                        return;
                    }
                    StartCharging();
                }
            };
            AcceptanceReport acceptanceReport = CanLaunch();
            if (!acceptanceReport.Accepted)
            {
                command_Action.Disable(acceptanceReport.Reason);
            }
            yield return command_Action;
        }

        public override AcceptanceReport CanLaunch(float? overrideFuelLevel = null)
        {
            if (charging)
            {
                return "CelesFD_LaunchCharging".Translate();
            }
            AcceptanceReport acceptanceReport = base.CanLaunch(overrideFuelLevel);
            if (!acceptanceReport.Accepted)
            {
                return acceptanceReport;
            }
            // 电力检查（仿原版 AllLaunchablesInGroupHaveFuelForLaunch 组内遍历结构）：组内任一舱对应平台断电 → 禁用
            if (!AllPadsPowered())
            {
                return "CelesFD_LaunchGroupNoPower".Translate();
            }
            if (!OrderSatisfied())
            {
                return "CelesFD_NothingToSend".Translate();
            }
            if (!TryGetNearestStation(out _, out _, out _))
            {
                return "CelesFD_NoBeaconStation".Translate();
            }
            return true;
        }

        // M6-4 发射门槛（§5.7 定稿）：舱内含已接取订单需求物（>0）即可发射（累计交付，不要求装满）；
        // 无已接取订单 → 回退 G5 现状内容物非空（旧档/无订单场景兼容）
        // 注：本方法是本类自有的 virtual（G8 预留），非基类覆写——不能加 override
        protected virtual bool OrderSatisfied()
        {
            List<CompTransporter> group = Transporter.TransportersInGroup(parent.Map);
            if (group == null)
            {
                // 无组兜底：本舱自身的 CompTransporter（this 是发射 Comp，非 CompTransporter——不能用 this）
                var own = parent.TryGetComp<CompTransporter>();
                group = own != null ? new List<CompTransporter> { own } : new List<CompTransporter>();
            }
            var gc = CelesFD_GameComponent.Instance;
            if (gc == null) return group.Any(t => t.GetDirectlyHeldThings().Any());
            // 已接取订单需求物集合（v2：候选集展开——"任意此类商品"；材质/耐久装填侧校验裁决不做，结算端直接匹配）
            var wanted = new HashSet<ThingDef>();
            foreach (CelesFD_Order o in gc.MarketOrders)
                if (o.state == CelesFD_OrderState.Accepted && o.remaining > 0)
                    foreach (ThingDef d in o.ThingDefs) wanted.Add(d);
            if (wanted.Count == 0) return group.Any(t => t.GetDirectlyHeldThings().Any());   // 无订单回退非空
            foreach (CompTransporter t in group)
            {
                foreach (Thing thing in t.GetDirectlyHeldThings())
                {
                    if (!wanted.Contains(thing.def)) continue;
                    // M6-6：突发单介入标记（舱内含突发需求物 = 开始装载 → 逾期计入成败，§5.7）
                    foreach (CelesFD_Order o in gc.MarketOrders)
                        if (o.state == CelesFD_OrderState.Accepted && !o.intervened
                            && o.TemplateDef != null && o.TemplateDef.category == CelesFD_MarketCategory.Urgent
                            && o.ThingDefs.Contains(thing.def))
                            o.intervened = true;
                    return true;
                }
            }
            return false;
        }

        // ============ 信标站目标 ============

        // 最近信标站 + 距离 + 层可达校验（信标站恒在地面层；太空玩家跨层发射）
        private bool TryGetNearestStation(out Settlement station, out int dist, out PlanetTile tile)
        {
            station = null;
            dist = int.MaxValue;
            tile = PlanetTile.Invalid;
            var stations = CelesFD_BeaconUtility.GetStations();
            if (stations.Count == 0)
            {
                return false;
            }
            foreach (Settlement s in stations)
            {
                int d = Find.WorldGrid.TraversalDistanceBetween(parent.Map.Tile, s.Tile, passImpassable: true, int.MaxValue, canTraverseLayers: true);
                if (d < dist)
                {
                    dist = d;
                    station = s;
                    tile = s.Tile;
                }
            }
            if (station == null)
            {
                return false;
            }
            // 层可达：仅跨层时检查（同层恒通过）
            if (parent.Map.Tile.Layer != tile.Layer && !parent.Map.Tile.Layer.HasConnectionPathTo(tile.Layer))
            {
                return false;
            }
            // 射程（fixedLaunchDistanceMax 按目标层因子；无燃料路径 MaxLaunchDistanceAtFuelLevel 返回有限/无限）
            if (dist > MaxLaunchDistanceAtFuelLevel(float.PositiveInfinity, tile.Layer))
            {
                return false;
            }
            return true;
        }

        private void LaunchToNearestStation()
        {
            if (!TryGetNearestStation(out Settlement station, out _, out PlanetTile tile))
            {
                Messages.Message("CelesFD_NoBeaconStation".Translate(), parent, MessageTypeDefOf.NegativeEvent, historical: false);
                return;
            }
            TryLaunch(tile, new CelesFD_ArrivalAction_BeaconOrderTest(station)); // G6 占位结算（订单机制完成时替换）
        }

        // ============ 前摇状态机（组级） ============

        private void StartCharging()
        {
            foreach (CelesFD_CompLaunchable comp in GroupComps())
            {
                comp.BeginChargeLocal();
            }
        }

        private void BeginChargeLocal()
        {
            charging = true;
            ticksRemaining = ChargeProps?.chargeDurationTicks ?? 900;
            SetPadPower(PadPower?.Props.PowerConsumption ?? 0f); // 发射暂态 = 平台工作态耗电（basePowerConsumption，含 upgrade）
        }

        private void CancelCharging()
        {
            foreach (CelesFD_CompLaunchable comp in GroupComps())
            {
                comp.CancelChargeLocal();
            }
        }

        private void CancelChargeLocal()
        {
            if (!charging)
            {
                return;
            }
            charging = false;
            // 恢复待机 = 平台 idlePowerDraw（未配置则回落满载）
            float standby = PadPower?.Props.idlePowerDraw > 0f ? PadPower.Props.idlePowerDraw : (PadPower?.Props.PowerConsumption ?? 0f);
            SetPadPower(standby);
        }

        private List<CelesFD_CompLaunchable> GroupComps()
        {
            var list = new List<CelesFD_CompLaunchable>();
            List<CompTransporter> group = Transporter.TransportersInGroup(parent.Map);
            if (group == null)
            {
                list.Add(this);
                return list;
            }
            foreach (CompTransporter t in group)
            {
                if (t.parent.TryGetComp<CelesFD_CompLaunchable>() is CelesFD_CompLaunchable comp)
                {
                    list.Add(comp);
                }
            }
            if (list.Count == 0)
            {
                list.Add(this);
            }
            return list;
        }

        private bool AllPadsPowered()
        {
            List<CompTransporter> group = Transporter.TransportersInGroup(parent.Map);
            if (group == null)
            {
                return PadPowerOf(parent as Building)?.PowerOn != false;
            }
            foreach (CompTransporter t in group)
            {
                CompPowerTrader pc = PadPowerOf(t.parent as Building);
                if (pc != null && !pc.PowerOn)
                {
                    return false;
                }
            }
            return true;
        }

        // 平台充能参数（轻量 Comp：仅前摇时长——挂平台）
        private CelesFD_CompProperties_LaunchCharge ChargeProps
        {
            get
            {
                Building pad = CelesFD_LaunchPortUtility.GetLaunchPadForPod(parent as Building);
                return (pad?.GetComp<CelesFD_CompLaunchCharge>()?.Props) as CelesFD_CompProperties_LaunchCharge;
            }
        }

        // 平台电力（原版 CompPowerTrader——发射耗电 = PowerConsumption / 待机 = idlePowerDraw，直接读原版字段）
        private CompPowerTrader PadPower => PadPowerOf(parent as Building);

        // 舱体 → 平台 → CompPowerTrader（平台无电力 Comp 时视为恒满足）
        private static CompPowerTrader PadPowerOf(Building pod)
        {
            if (pod == null)
            {
                return null;
            }
            Building pad = CelesFD_LaunchPortUtility.GetLaunchPadForPod(pod);
            return pad?.GetComp<CompPowerTrader>();
        }

        private void SetPadPower(float watts)
        {
            CompPowerTrader pc = PadPowerOf(parent as Building);
            if (pc != null)
            {
                pc.PowerOutput = -watts; // 负 = 耗电
            }
        }
    }

    // 舱体发射 Comp（字段全部继承原版——耗电/时长参数已移至平台 CelesFD_CompProperties_LaunchPadPower）
    public class CelesFD_CompProperties_Launchable : CompProperties_Launchable_TransportPod
    {
        public CelesFD_CompProperties_Launchable()
        {
            compClass = typeof(CelesFD_CompLaunchable);
        }
    }

    // 充能参数 Comp（挂发射平台，2026-08-11）：仅前摇时长字段 + 平台常态耗电初始化。
    // 电力参数 = 平台原版 CompPowerTrader（basePowerConsumption=发射/idlePowerDraw=待机——原版双档语义，单一来源），
    // 前摇时长是发射行为特有概念（原版无"暂态时长"），独立轻量 Comp 承载。
    public class CelesFD_CompProperties_LaunchCharge : CompProperties
    {
        public int chargeDurationTicks = 900;

        public CelesFD_CompProperties_LaunchCharge()
        {
            compClass = typeof(CelesFD_CompLaunchCharge);
        }
    }

    // 充能参数 Comp 本体：平台常态耗电初始化（原版 PowerTrader 无常态调用方——PostSpawnSetup 统一设置 = idlePowerDraw 单一来源，覆盖初始与读档）
    public class CelesFD_CompLaunchCharge : ThingComp
    {
        // ThingComp 基类无 Props 属性（只有 props 字段）——按原版 Comp 惯例提供
        public CelesFD_CompProperties_LaunchCharge Props => (CelesFD_CompProperties_LaunchCharge)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            CompPowerTrader pc = parent.GetComp<CompPowerTrader>();
            if (pc != null && pc.Props.idlePowerDraw > 0f)
            {
                pc.PowerOutput = -pc.Props.idlePowerDraw;
            }
        }
    }
}
