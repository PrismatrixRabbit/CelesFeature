using RimWorld;
using Verse;

namespace CelesFeature
{
    public class Celes_HediffComp_RemoveHediffOnDeath:HediffComp
    {
        public override void Notify_PawnKilled()
        {
            if (base.Pawn.kindDef.race.defName == "Celes_Race")
            {
                base.Pawn.health.RemoveHediff(base.Pawn.health.hediffSet.GetFirstHediffOfDef(Celes_HediffDefOf.MechlinkImplant));
            }
        }

        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit = null)
        {
            if (base.Pawn.kindDef.race.defName == "Celes_Race")
            {
                base.Pawn.health.RemoveHediff(base.Pawn.health.hediffSet.GetFirstHediffOfDef(Celes_HediffDefOf.MechlinkImplant));
            }
        }
    }
}