using RimWorld;
using Verse;

namespace CelesFeature
{
    // W-2b（2026-09-03 用户裁决）：Alert 槽位池——per-信标独立行（每支援一行不堆叠；label 即完整行文本：
    //   "xxx 即将抵达：00:05秒" / "xxx 正在进行：00:0X秒"【正计时】；描述"武备抵达中"）
    // 方案（替代 Harmony 注入，评审 R1）：预声明 6 个槽位子类——AlertsReadout 构造期自动实例化全部
    //   Alert 叶子子类（零注册），每类一行；信标 Spawn 时取最低空槽绑定、Destroy 释放；
    //   >6 并发信标 → 第 7+ 不显示（用户裁决"多出的不显示"）——全系统零 Harmony 新增、零反射
    // 纪律：自定义 Alert 子类必须有无参构造（AlertsReadout Activator.CreateInstance——A2-18 事故根因）
    public class CelesFD_Alert_SupportSlotBase : Alert
    {
        public virtual int SlotIndex => -1;   // 子类覆写（空壳子类仅存在性）

        public CelesFD_Alert_SupportSlotBase()
        {
            defaultPriority = AlertPriority.Medium;
        }

        public CelesFD_SupportBeacon Beacon
        {
            get { return CelesFD_SupportAlertSlots.GetBeacon(SlotIndex); }
        }

        public override AlertReport GetReport()
        {
            AlertReport report = default;
            CelesFD_SupportBeacon beacon = Beacon;
            report.active = beacon != null && !beacon.Destroyed && beacon.Spawned && beacon.SupportDef != null;
            return report;
        }

        public override string GetLabel()
        {
            CelesFD_SupportBeacon beacon = Beacon;
            if (beacon == null || beacon.SupportDef == null) return "星铃支援";
            if (beacon.phase == CelesFD_SupportBeacon.BeaconPhase.Waiting)
            {
                return "CelesFD_Keyed_SupportIncomingLine".Translate(beacon.SupportDef.LabelCap,
                    FormatTicks(beacon.WaitingTicksLeft));
            }
            return "CelesFD_Keyed_SupportOngoingLine".Translate(beacon.SupportDef.LabelCap,
                FormatTicks(beacon.OngoingTicksElapsed));   // 正计时（用户裁决）
        }

        public override TaggedString GetExplanation()
        {
            return "CelesFD_Keyed_SupportAlertDesc".Translate();
        }

        // 00:05秒（分:秒；60 tick = 1 秒）——字符串预格式化后入 Translate（防裸数值换行 bug 纪律）
        internal static string FormatTicks(int ticks)
        {
            if (ticks < 0) ticks = 0;
            int totalSeconds = ticks / 60;
            return (totalSeconds / 60).ToString("00") + ":" + (totalSeconds % 60).ToString("00") + "秒";
        }
    }

    // 6 槽位空壳（AlertsReadout 自动扫描载体；每类一行）
    public class CelesFD_Alert_SupportSlot1 : CelesFD_Alert_SupportSlotBase { public override int SlotIndex => 0; }
    public class CelesFD_Alert_SupportSlot2 : CelesFD_Alert_SupportSlotBase { public override int SlotIndex => 1; }
    public class CelesFD_Alert_SupportSlot3 : CelesFD_Alert_SupportSlotBase { public override int SlotIndex => 2; }
    public class CelesFD_Alert_SupportSlot4 : CelesFD_Alert_SupportSlotBase { public override int SlotIndex => 3; }
    public class CelesFD_Alert_SupportSlot5 : CelesFD_Alert_SupportSlotBase { public override int SlotIndex => 4; }
    public class CelesFD_Alert_SupportSlot6 : CelesFD_Alert_SupportSlotBase { public override int SlotIndex => 5; }

    // 槽位注册表（[Unsaved] 运行期：信标 Spawn 绑定 / Destroy 释放；信标销毁后槽位 GetReport 自然失活）
    public static class CelesFD_SupportAlertSlots
    {
        public const int SlotCount = 6;
        private static readonly CelesFD_SupportBeacon[] slotBeacons = new CelesFD_SupportBeacon[SlotCount];

        public static CelesFD_SupportBeacon GetBeacon(int index)
        {
            if (index < 0 || index >= SlotCount) return null;
            CelesFD_SupportBeacon beacon = slotBeacons[index];
            if (beacon != null && beacon.Destroyed)
            {
                slotBeacons[index] = null;   // 惰性清理（Destroy 释放的兜底）
                return null;
            }
            return beacon;
        }

        // 信标落地时取最低空槽；无空槽（>6 并发）→ 不显示（用户裁决）
        public static void Assign(CelesFD_SupportBeacon beacon)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (GetBeacon(i) == null)
                {
                    slotBeacons[i] = beacon;
                    return;
                }
            }
        }

        public static void Release(CelesFD_SupportBeacon beacon)
        {
            for (int i = 0; i < SlotCount; i++)
                if (ReferenceEquals(slotBeacons[i], beacon))
                    slotBeacons[i] = null;
        }

        // 跨存档清理（static 数组进程内存活——切档时旧信标引用残留[未走 Destroy]，槽位被占满/计时延续；
        //   GameComponent 构造函数每次新游戏/读档均执行——在此清空）
        public static void ClearAll()
        {
            for (int i = 0; i < SlotCount; i++)
                slotBeacons[i] = null;
        }
    }
}
