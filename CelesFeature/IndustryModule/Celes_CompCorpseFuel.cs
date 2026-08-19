/* 尸体投入机制暂时注释（K7 搁置，恢复时移除首尾注释）
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // 尸体燃料通道（原版无此机制，必要自建）：
    // [事实] 结算直调 public Refuel(amount)（CompRefuelable.cs:275-283）进入原版燃料池，
    // 消耗/停机/告警/自动补料全走原版；Refuelable 分组为 HasAssignableCompFrom（ThingListGroupHelper.cs:97），无需子类化
    public class Celes_CompProperties_CorpseFuel : CompProperties
    {
        public List<ThingDef> corpseBlacklist;
        public float maxCorpseCharge = 2.0f;

        public Celes_CompProperties_CorpseFuel()
        {
            compClass = typeof(Celes_CompCorpseFuel);
        }
    }

    public class Celes_CompCorpseFuel : ThingComp
    {
        public Celes_CompProperties_CorpseFuel Props => (Celes_CompProperties_CorpseFuel)props;

        public bool CanAcceptCorpse(Corpse corpse)
        {
            if (corpse == null || corpse.InnerPawn == null)
                return false;
            if (Props.corpseBlacklist != null && Props.corpseBlacklist.Contains(corpse.InnerPawn.def))
                return false;
            CompRefuelable refuelable = parent.GetComp<CompRefuelable>();
            return refuelable != null && !refuelable.IsFull;
        }

        // [事实] 体型点数取 InnerPawn.BodySize（Corpse.cs:450 同款用法），封顶 maxCorpseCharge
        public float GetCorpseFuelAmount(Corpse corpse)
        {
            if (corpse == null || corpse.InnerPawn == null)
                return 0f;
            return Mathf.Min(corpse.InnerPawn.BodySize, Props.maxCorpseCharge);
        }
    }
}
*/
