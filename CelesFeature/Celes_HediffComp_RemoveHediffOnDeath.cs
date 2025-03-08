using RimWorld;
using Verse;

namespace CelesFeature
{
    public class Celes_HediffComp_RemoveHediffOnDeath:HediffComp
    {
        public override void Notify_PawnKilled()
        {
            if (base.Pawn.def == Celes_ThingDefOf.Celes_Race)
            {
                base.Pawn.health.RemoveHediff(base.Pawn.health.hediffSet.GetFirstHediffOfDef(Celes_HediffDefOf.MechlinkImplant));
                base.Pawn.health.AddHediff(Celes_HediffDefOf.CelesSleepMechlinkImplant);
            }
        }

        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit = null)
        {
            if (base.Pawn.def == Celes_ThingDefOf.Celes_Race)
            {
                base.Pawn.health.RemoveHediff(base.Pawn.health.hediffSet.GetFirstHediffOfDef(Celes_HediffDefOf.MechlinkImplant));
                base.Pawn.health.AddHediff(Celes_HediffDefOf.CelesSleepMechlinkImplant);
            }
        }
    }
}