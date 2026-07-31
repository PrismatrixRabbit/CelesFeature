using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    public class Celes_CompProperties_MultiVerb : CompProperties
    {
        public List<Celes_VerbInfo> verbInfos;

        public Celes_CompProperties_MultiVerb()
        {
            compClass = typeof(Celes_CompMultiVerb);
        }
    }
    
    public class Celes_CompMultiVerb : ThingComp
    {
        public int verbIndex = 0;
        public Celes_CompProperties_MultiVerb Props => (Celes_CompProperties_MultiVerb)props;
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref verbIndex, "verbIndex", 0);
        }
        // [事实] 参照 CompMultiVerbByHediff.Notify_Equipped（CompProperties_MultiVerb.md:363-371）
        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            Hediff hediff = HediffMaker.MakeHediff(Celes_HediffDefOf.Celes_MultiVerbSelect, pawn);
            pawn.health.AddHediff(hediff);
        }
        // [事实] 参照 CompMultiVerbByHediff.Notify_Unequipped（CompProperties_MultiVerb.md:373-384）
        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(Celes_HediffDefOf.Celes_MultiVerbSelect);
            if (existing != null)
                pawn.health.RemoveHediff(existing);
        }
        public void SetNextVerbIndex()
        {
            verbIndex++;
            if (verbIndex >= parent.def.Verbs.Count)
                verbIndex = 0;
        }
    }
}