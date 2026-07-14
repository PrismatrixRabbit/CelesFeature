using Verse;
using RimWorld;

namespace CelesFeature
{
    /* 这个comp实现星铃初始Hediff */
    public class CompProperties_CelesRhythmInitializer : CompProperties
    {
        public HediffDef hediffToApply;
        public float initialSeverity = -1f; // -1 = use HediffDef.initialSeverity

        public CompProperties_CelesRhythmInitializer()
        {
            compClass = typeof(Comp_CelesRhythmInitializer);
        }
    }

    public class Comp_CelesRhythmInitializer : ThingComp
    {
        private bool hasAppliedInitialHediff;

        public CompProperties_CelesRhythmInitializer Props =>
            (CompProperties_CelesRhythmInitializer)props;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref hasAppliedInitialHediff, "hasAppliedInitialHediff");
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            if (hasAppliedInitialHediff)
                return;

            Pawn pawn = parent as Pawn;
            if (pawn == null)
                return;

            Hediff hediff = HediffMaker.MakeHediff(Props.hediffToApply, pawn, null);

            if (Props.initialSeverity >= 0f)
                hediff.Severity = Props.initialSeverity;

            pawn.health.AddHediff(hediff, null, null, null);

            hasAppliedInitialHediff = true;
        }
    }
}