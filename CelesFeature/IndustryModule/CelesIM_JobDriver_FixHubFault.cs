using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace CelesFeature
{
    // 中枢故障维修 JobDriver（E+F 批 2026-09-13；2026-09-20 修复多堆收集）
    //   收集链 = 直接调用原版 public static JobDriver_EnterBiosculpterPod.CollectIngredientsToilsHelper
    //   （逐堆 Goto→StartCarryThing→转入 pawn.inventory 循环——原版双 Driver 复用先例之一）；
    //   预留 = 照抄 JobDriver_CarryToBiosculpterPod.cs:24-38 队列形状；
    //   终结消费 = 自写最小段（按份额销毁背包材料 + 原版成功率判定 + Notify_FaultRepaired）
    public class CelesIM_JobDriver_FixHubFault : JobDriver
    {
        private const TargetIndex BuildingInd = TargetIndex.A;

        private const TargetIndex ResourceInd = TargetIndex.B;

        private Building Building => (Building)job.GetTarget(BuildingInd).Thing;

        // 材料经原版 helper 转入背包后的记录（Biosculpter 模式；存档随 Job）
        private List<Thing> pickedUpIngredients = new List<Thing>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 照抄 CarryToBiosculpterPod：A + 队列逐项（首堆也在队内——装配侧全堆入队）
            if (!pawn.Reserve(Building, job, 1, -1, null, errorOnFailed))
                return false;
            List<LocalTargetInfo> targetQueue = job.GetTargetQueue(ResourceInd);
            for (int i = 0; i < targetQueue.Count; i++)
            {
                if (!pawn.Reserve(targetQueue[i], job, 1, -1, null, errorOnFailed))
                    return false;
            }
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(BuildingInd);
            // 原版收集链（public static 直调）：队列出堆→就近→携带→入背包→循环至凑足
            foreach (Toil item in JobDriver_EnterBiosculpterPod.CollectIngredientsToilsHelper(ResourceInd, pawn, pickedUpIngredients))
            {
                yield return item;
            }
            yield return Toils_Goto.GotoThing(BuildingInd, PathEndMode.Touch)
                .FailOnDespawnedOrNull(BuildingInd);
            CelesIM_CompThreadProducer hub = Building.TryGetComp<CelesIM_CompThreadProducer>();
            Toil wait = Toils_General.Wait(hub != null ? hub.Props.faultRepairWorkTicks : 1000);
            wait.FailOnDespawnedOrNull(BuildingInd);
            wait.FailOnCannotTouch(BuildingInd, PathEndMode.Touch);
            wait.WithEffect(Building.def.repairEffect, BuildingInd);
            wait.WithProgressBarToilDelay(BuildingInd);
            wait.activeSkill = () => SkillDefOf.Construction;
            yield return wait;
            Toil finalize = ToilMaker.MakeToil("MakeNewToils");
            finalize.initAction = delegate
            {
                // 终结消费（自写最小段）：按份额销毁背包材料合计 count 件
                ConsumePickedUpIngredients(hub != null ? hub.Props.faultRepairCount : job.count);
                if (Rand.Value > pawn.GetStatValue(StatDefOf.FixBrokenDownBuildingSuccessChance))
                {
                    MoteMaker.ThrowText((pawn.DrawPos + Building.DrawPos) / 2f, Map,
                        "TextMote_FixBrokenDownBuildingFail".Translate(), 3.65f);
                }
                else
                {
                    Building.TryGetComp<CelesIM_CompThreadProducer>()?.Notify_FaultRepaired();
                }
            };
            yield return finalize;
        }

        private void ConsumePickedUpIngredients(int total)
        {
            int remaining = total;
            for (int i = 0; i < pickedUpIngredients.Count && remaining > 0; i++)
            {
                Thing t = pickedUpIngredients[i];
                if (t == null || t.Destroyed || t.stackCount <= 0)
                    continue;
                if (remaining >= t.stackCount)
                {
                    remaining -= t.stackCount;
                    t.Destroy();
                }
                else
                {
                    Thing piece = t.SplitOff(remaining);
                    piece.Destroy();
                    remaining = 0;
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref pickedUpIngredients, "pickedUpIngredients", LookMode.Reference);
        }
    }
}
