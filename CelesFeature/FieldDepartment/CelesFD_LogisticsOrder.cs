using System.Collections.Generic;
using RimWorld;
using Verse;

namespace CelesFeature
{
    // 物流单物品项（M7 合并：一批购物车 = 一个物流单，多物品；材质/品质快照——2026-08-15 发货正确性）
    public class CelesFD_LogisticsItem : IExposable
    {
        public string thingDefName;
        public int amount;
        public string stuffDefName;   // 材质快照（订单复制——发货 MakeThing(td, stuff) 修复 madeFromStuff 报错）
        public QualityCategory qualityCategory = QualityCategory.Normal;
        public bool qualitySet;

        public ThingDef ThingDef =>
            thingDefName.NullOrEmpty() ? null : DefDatabase<ThingDef>.GetNamedSilentFail(thingDefName);

        public ThingDef StuffDef =>
            stuffDefName.NullOrEmpty() ? null : DefDatabase<ThingDef>.GetNamedSilentFail(stuffDefName);

        public CelesFD_LogisticsItem()
        {
        }

        public CelesFD_LogisticsItem(string thingDefName, int amount, string stuffDefName = null,
            QualityCategory qualityCategory = QualityCategory.Normal, bool qualitySet = false)
        {
            this.thingDefName = thingDefName;
            this.amount = amount;
            this.stuffDefName = stuffDefName;
            this.qualityCategory = qualityCategory;
            this.qualitySet = qualitySet;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref thingDefName, "thingDefName", null);
            Scribe_Values.Look(ref amount, "amount", 0);
            Scribe_Values.Look(ref stuffDefName, "stuffDefName", null);
            Scribe_Values.Look(ref qualityCategory, "qualityCategory", QualityCategory.Normal);
            Scribe_Values.Look(ref qualitySet, "qualitySet", false);
        }
    }

    // M7 出售物流单（一批购物车 = 一个物流单——同批次合并，用户裁决 2026-08-15；抵达 tick 判定交付——自建计时天然 dev 快进兼容）
    // 时长（2026-08-15 修正，草案为准）：标准 60000 ticks（24 游戏小时）/ 加急 2500 ticks（1 游戏小时）——固定不随内容变化
    public class CelesFD_LogisticsOrder : IExposable
    {
        public List<CelesFD_LogisticsItem> items = new List<CelesFD_LogisticsItem>();   // 本批物品（合并，≥1）
        public long startTick;         // 出发时刻（下单 tick）
        public long arrivalTick;       // 抵达时刻（start + 标准 60000 / 加急 2500）
        public bool expedited;         // 加急（四节点显示）
        public bool notifyArrival;     // 订阅到货提醒（下单时勾选快照——letter/Alert 控制）

        // W-4（2026-09-04 用户需求）：即将抵达倒计时——两阶段交付（机制预演）
        public long countdownStartTick = -1;   // -1 = 未进入倒计时（阶段 1 运输中）；>0 = 已进入（阶段 2）
        public const int CountdownTicks = 300; // 倒计时时长 5 秒（对照支援 arrivalDelayTicks 默认 300）

        public void ExposeData()
        {
            Scribe_Collections.Look(ref items, "items", LookMode.Deep);
            Scribe_Values.Look(ref startTick, "startTick", 0L);
            Scribe_Values.Look(ref arrivalTick, "arrivalTick", 0L);
            Scribe_Values.Look(ref expedited, "expedited", false);
            Scribe_Values.Look(ref notifyArrival, "notifyArrival", false);
            Scribe_Values.Look(ref countdownStartTick, "countdownStartTick", -1L);
            if (items == null) items = new List<CelesFD_LogisticsItem>();
        }
    }
}
