using System.Collections.Generic;
using UnityEngine;
using Verse;

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
            if (EqComp == null || EqComp.Props.verbInfos == null)
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
                action = SwitchToNextVerb
            };
        }
        public override void CompPostMake()
        {
            base.CompPostMake();
            if (EqComp == null)
                compShouldRemove = true;
        }
        private void SwitchToNextVerb()
        {
            EqComp.SetNextVerbIndex();
        }
    }
}