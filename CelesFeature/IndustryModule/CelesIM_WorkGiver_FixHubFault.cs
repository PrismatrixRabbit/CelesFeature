using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;

namespace CelesFeature
{
    // 中枢故障维修 WorkGiver（E+F 批 2026-09-13；2026-09-20 修复多堆装配）
    //   复刻 WorkGiver_FixBrokenDownBuilding.cs:7-93 守卫族 + 数据源 = 管理器注册表（FaultedHubsForReading）；
    //   多堆凑数装配 = ResourceDeliverJobFor.cs:226-256 装配算法的单 def 化（验证器回归原版三条件——
    //   无单堆数量要求；原 bug 根因 = 验证器误加 stackCount >= count 致 2×1 堆判无资源）
    public class CelesIM_WorkGiver_FixHubFault : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return CelesIM_ThreadNetworkManager.For(pawn.Map).FaultedHubsForReading;
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            return CelesIM_ThreadNetworkManager.For(pawn.Map).FaultedHubsForReading.Count == 0;
        }

        public override Danger MaxPathDanger(Pawn pawn)
        {
            return Danger.Deadly;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building building))
                return false;
            if (!building.def.building.repairable)
                return false;
            if (t.Faction != pawn.Faction)
                return false;
            CelesIM_CompThreadProducer hub = building.TryGetComp<CelesIM_CompThreadProducer>();
            if (hub == null || !hub.FaultActive)
                return false;
            if (hub.Props.faultRepairResource == null || hub.Props.faultRepairCount < 1)
                return false;   // 未配置维修资源的 def 不提供维修 Job
            if (pawn.Faction == Faction.OfPlayer && !pawn.Map.areaManager.Home[t.Position])
            {
                JobFailReason.Is("NotInHomeArea".Translate());
                return false;
            }
            if (!pawn.CanReserve(building, 1, -1, null, forced))
                return false;
            if (pawn.Map.designationManager.DesignationOn(building, DesignationDefOf.Deconstruct) != null)
                return false;
            if (building.IsBurning())
                return false;
            if (!TryAssembleRepairResources(pawn, hub.Props.faultRepairResource, hub.Props.faultRepairCount, out _, out _))
            {
                JobFailReason.Is("CelesIM_Keyed_NoFaultResource".Translate());
                return false;
            }
            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CelesIM_CompThreadProducer hub = t.TryGetComp<CelesIM_CompThreadProducer>();
            Job job = JobMaker.MakeJob(CelesIM_DefOf.CelesIM_FixHubFault, t);
            TryAssembleRepairResources(pawn, hub.Props.faultRepairResource, hub.Props.faultRepairCount,
                out List<Thing> stacks, out List<int> shares);
            // 全堆入队（含首堆）+ 份额对齐——ExtractNextTargetFromQueue 逐项弹出并同步 job.count
            //（Toils_JobTransforms.cs:28-33 实证：读的是单一 job.countQueue，与被出队队列配对；镜像 DoBill 装配）
            job.targetQueueB = new List<LocalTargetInfo>();
            for (int i = 0; i < stacks.Count; i++)
                job.targetQueueB.Add(stacks[i]);
            job.countQueue = shares;
            job.count = hub.Props.faultRepairCount;
            return job;
        }

        // 多堆装配（ResourceDeliverJobFor.cs:226-256 装配算法单 def 化）：
        // 全图总量门（原版 :172）→ 按 def 索引取候选 → 距离升序累加截断；验证器 = 原版三条件
        // （def 匹配 / 非禁用 / 可预留——:87-102）+ 可达性
        private static bool TryAssembleRepairResources(Pawn pawn, ThingDef def, int count,
            out List<Thing> stacks, out List<int> shares)
        {
            stacks = new List<Thing>();
            shares = new List<int>();
            if (!pawn.Map.itemAvailability.ThingsAvailableAnywhere(def, count, pawn))
                return false;
            List<Thing> candidates = pawn.Map.listerThings.ThingsOfDef(def);
            int remaining = count;
            // 距离升序（单 def 规模小，直接排序；累加截断同原版 do/while）
            candidates.Sort((a, b) => (pawn.Position - a.Position).LengthHorizontalSquared.CompareTo((pawn.Position - b.Position).LengthHorizontalSquared));
            for (int i = 0; i < candidates.Count && remaining > 0; i++)
            {
                Thing t = candidates[i];
                if (t == null || t.IsForbidden(pawn) || !pawn.CanReserve(t))
                    continue;
                if (!pawn.CanReach(t, PathEndMode.ClosestTouch, pawn.NormalMaxDanger()))
                    continue;
                int take = remaining < t.stackCount ? remaining : t.stackCount;
                stacks.Add(t);
                shares.Add(take);
                remaining -= take;
            }
            return remaining <= 0;
        }
    }
}
