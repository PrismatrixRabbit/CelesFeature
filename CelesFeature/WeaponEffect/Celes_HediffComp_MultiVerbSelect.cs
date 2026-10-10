using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace CelesFeature
{
    public class Celes_HediffCompProperties_MultiVerbSelect : HediffCompProperties
    {
        public Celes_HediffCompProperties_MultiVerbSelect()
        {
            compClass = typeof(Celes_HediffComp_MultiVerbSelect);
        }
    }
    
    // [事实] 参照 AzWeaponLib HediffComp_MultiVerbSelect（HediffComp_MultiVerbSelect.md:15-246）
    public class Celes_HediffComp_MultiVerbSelect : HediffComp
    {
        private bool compShouldRemove = false;
        private Celes_CompMultiVerb eqCompInt;
        public override bool CompShouldRemove => compShouldRemove;
        public Celes_CompMultiVerb EqComp
        {
            get
            {
                if (Pawn == null || Pawn.equipment?.Primary == null)
                    return null;
                if (eqCompInt == null)
                    eqCompInt = Pawn.equipment.Primary.TryGetComp<Celes_CompMultiVerb>();
                return eqCompInt;
            }
        }
        public override IEnumerable<Gizmo> CompGetGizmos()
        {
            if (EqComp == null || !EqComp.Props.showGizmos || EqComp.Props.verbInfos == null)
                yield break;
            int idx = EqComp.verbIndex;
            if (idx < 0 || idx >= EqComp.Props.verbInfos.Count)
                yield break;
            var info = EqComp.Props.verbInfos[idx];
            yield return new Command_Action
            {
                icon = ContentFinder<Texture2D>.Get(info.iconPath),
                defaultLabel = info.defaultLabel,
                defaultDesc = info.defaultDesc,
                activateSound = SoundDefOf.Click,   // [吸收 AWL 2026-10-11] 点击音
                groupKey = 0x51C30000 + idx,        // [吸收 AWL 2026-10-11] 随模式变化，避免按钮合并
                action = SwitchToNextVerb
            };
        }
        public override void CompPostMake()
        {
            base.CompPostMake();
            if (EqComp == null)
                compShouldRemove = true;
        }
        public override void CompExposeData()
        {
            base.CompExposeData();
            // [吸收 AWL 2026-10-11] 读档时武器已不在 → 自清（防残留空 hediff）
            if (Scribe.mode == LoadSaveMode.PostLoadInit && EqComp == null)
            {
                compShouldRemove = true;
            }
        }
        private void SwitchToNextVerb()
        {
            Celes_CompMultiVerb comp = EqComp;
            if (comp == null)
            {
                compShouldRemove = true;   // [吸收 AWL] 武器已不在手 → 自清
                return;
            }
            // [修复 + 吸收 AWL 2026-10-11] 切换时迁移当前 job 的 verbToUse：
            //   玩家攻击指令（AttackStatic）逐发动态解析（TryStartAttack → PrimaryEq.PrimaryVerb → Prefix），
            //   本就不依赖 job.verbToUse；此迁移服务的是 job.verbToUse 的执行读取方
            //   （Toils_Combat.cs:89/:54 的 AI 战斗 job、近战 job、能力 job）与不变量一致性。
            Job curJob = Pawn?.CurJob;                                              // AWL 缺此守卫，我们补
            Verb oldVerb = comp.VerbByIndex(comp.verbIndex);
            comp.SetNextVerbIndex();
            if (curJob != null && curJob.verbToUse == oldVerb)
            {
                curJob.verbToUse = comp.VerbByIndex(comp.verbIndex);
            }
        }
    }
}