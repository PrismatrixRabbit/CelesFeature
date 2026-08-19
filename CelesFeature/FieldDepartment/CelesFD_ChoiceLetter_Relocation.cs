using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace CelesFeature
{
    // 搬迁询问 letter（F5）：全局仅一次；接受 = 回收最早信标站 + 近点新建；拒绝 = 保持现状
    // 无自定义存档字段——执行时现查站列表（letter 挂起/读档期间站变化也能正确执行）
    public class CelesFD_ChoiceLetter_Relocation : ChoiceLetter
    {
        public override bool CanDismissWithRightClick => false;   // 防误关丢失事件（仿 ChoiceLetter_AcceptJoiner）

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                if (base.ArchivedOnly)
                {
                    yield return base.Option_Close;
                    yield break;
                }
                yield return new DiaOption("CelesFD_Keyed_Letter_RelocationAccept".Translate())
                {
                    action = delegate
                    {
                        AcceptRelocation();
                        Find.LetterStack.RemoveLetter(this);
                    },
                    resolveTree = true
                };
                yield return new DiaOption("CelesFD_Keyed_Letter_RelocationDecline".Translate())
                {
                    action = delegate
                    {
                        Find.LetterStack.RemoveLetter(this);
                    },
                    resolveTree = true
                };
            }
        }

        // 接受：触发"搬迁接受"事件（回收最早站→近点生成→letter，逻辑内聚于 CelesFD_IncidentWorker_RelocationAccepted）
        private void AcceptRelocation()
        {
            var parms = new IncidentParms
            {
                target = Find.World,
                faction = CelesFD_BeaconUtility.BeaconFaction
            };
            CelesFD_DefOf.CelesFD_RelocationAcceptedSignal.Worker.TryExecute(parms);
        }
    }
}
