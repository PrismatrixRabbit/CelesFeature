using System.Collections.Generic;
using Verse;
using RimWorld;

namespace CelesFeature
{
    // ═══ 批 2.5 货组机制（2026-09-24）：IOpenable 拆解三件套 ═══
    //   接口判定在 Thing 实例上（FloatMenuOptionProvider_OpenThing:16 / JobDriver_Open:15 实证）——
    //   原版唯一实现者 Building_Casket 同构：Thing 类实现 IOpenable，委托给 Comp 逻辑宿主。
    //   右键菜单/Job/等待/进度条/records 全链原版现成，零自建。

    // XML 参数宿主（openTicks 可调）
    public class CelesIM_CompProperties_Cargo : CompProperties
    {
        public int openTicks = 100;   // 拆解工作时长（原版棺匣 OpenTicks 量级）

        public CelesIM_CompProperties_Cargo()
        {
            compClass = typeof(CelesIM_CompCargo);
        }
    }

    // 逻辑宿主：拆解返还 = 成分表单一真相源（def.CostStuffCount × parent.Stuff + def.CostList 逐项）
    // 改配方则拆解自动跟随——无独立返还表，零冗余
    public class CelesIM_CompCargo : ThingComp
    {
        public CelesIM_CompProperties_Cargo Props => (CelesIM_CompProperties_Cargo)props;

        public bool CanOpen => parent != null && parent.Spawned && !parent.Destroyed;

        public int OpenTicks => Props.openTicks;

        public void Open()
        {
            Map map = parent.Map;
            IntVec3 pos = parent.Position;
            int n = parent.stackCount;   // 堆叠修复（2026-09-24）：Destroy 销毁整叠——返还须 × stackCount
            // 成分构造（快照在销毁前完成）
            var toPlace = new List<Thing>();
            if (parent.Stuff != null && parent.def.CostStuffCount > 0)
            {
                Thing stuffThing = ThingMaker.MakeThing(parent.Stuff);
                stuffThing.stackCount = parent.def.CostStuffCount * n;
                toPlace.Add(stuffThing);
            }
            if (parent.def.CostList != null)
            {
                foreach (ThingDefCountClass cost in parent.def.CostList)
                {
                    Thing t = ThingMaker.MakeThing(cost.thingDef);
                    t.stackCount = cost.count * n;
                    toPlace.Add(t);
                }
            }
            // 先销毁腾出原格 → 原位落物（Direct——CompAbilityEffect_Transmute :65 物品级先例）；
            // 原格被占的极小窗口回退 Near（双保险）
            parent.Destroy();
            foreach (Thing t in toPlace)
            {
                if (!GenPlace.TryPlaceThing(t, pos, map, ThingPlaceMode.Direct))
                    GenPlace.TryPlaceThing(t, pos, map, ThingPlaceMode.Near);
            }
        }
    }

    // IOpenable 接入点（Building_Casket 模式：Thing 类实现，三成员纯转发）
    public class CelesIM_CargoCrate : ThingWithComps, IOpenable
    {
        private CelesIM_CompCargo Comp => GetComp<CelesIM_CompCargo>();

        public bool CanOpen => Comp?.CanOpen ?? false;

        public int OpenTicks => Comp?.OpenTicks ?? 100;

        public void Open() => Comp?.Open();
    }
}
