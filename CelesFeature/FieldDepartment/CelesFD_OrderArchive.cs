using Verse;

namespace CelesFeature
{
    // 外勤档案单条记录（§5.7/§6.6：M6 结算/违约归档；M7 物流页历史卡片显示；超 20 删最旧）
    // 2026-08-15 扩展：完整信息快照（物品名/数量/报酬——历史卡片"类同交易页卡片"渲染数据源）+ absTick（完成时间"年.象.日"）
    public class CelesFD_OrderArchiveEntry : IExposable
    {
        public string label;            // 订单显示名（ResolveLabel 快照——模板 Def 删除后仍可读）
        public bool succeeded;          // true = 完成 / false = 违约
        public long tick;               // 归档时刻（旧字段：TicksGame；新档由 absTick 承载显示）
        public long absTick;            // 归档时刻（TicksAbs——日期显示"年.象.日"用；旧档 0 = 不显示时间）
        public string thingDefName;     // 物品快照（历史卡片 icon 用；多候选收购 = 字母序首物）
        public string thingLabel;       // 物品名快照（历史卡片品名）
        public int amount;              // 数量快照
        public int creditReward;        // 信用额报酬快照
        public int keyReward;           // 密钥报酬快照

        public CelesFD_OrderArchiveEntry()
        {
        }

        public CelesFD_OrderArchiveEntry(string label, bool succeeded, long tick)
        {
            this.label = label;
            this.succeeded = succeeded;
            this.tick = tick;
        }

        public CelesFD_OrderArchiveEntry(string label, bool succeeded, long tick, long absTick,
            string thingDefName, string thingLabel, int amount, int creditReward, int keyReward)
        {
            this.label = label;
            this.succeeded = succeeded;
            this.tick = tick;
            this.absTick = absTick;
            this.thingDefName = thingDefName;
            this.thingLabel = thingLabel;
            this.amount = amount;
            this.creditReward = creditReward;
            this.keyReward = keyReward;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref label, "label", null);
            Scribe_Values.Look(ref succeeded, "succeeded", false);
            Scribe_Values.Look(ref tick, "tick", 0L);
            Scribe_Values.Look(ref absTick, "absTick", 0L);
            Scribe_Values.Look(ref thingDefName, "thingDefName", null);
            Scribe_Values.Look(ref thingLabel, "thingLabel", null);
            Scribe_Values.Look(ref amount, "amount", 0);
            Scribe_Values.Look(ref creditReward, "creditReward", 0);
            Scribe_Values.Look(ref keyReward, "keyReward", 0);
        }
    }
}
