using System.Collections.Generic;
using RimWorld;
using Verse;

namespace CelesFeature
{
    public class Celes_Recipe_Surgery_AddHediff:Recipe_Surgery
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            if (!base.AvailableOnNow(thing, part))
            {
                return false;
            }
            if (!(thing is Pawn pawn))
            {
                return false;
            }
            if (pawn.health.hediffSet.HasHediff(Celes_HediffDefOf.Celes_NanoImplanted_II) || pawn.health.hediffSet.HasHediff(Celes_HediffDefOf.Celes_NanoImplanted_I)||pawn.health.hediffSet.HasHediff(Celes_HediffDefOf.Celes_NanoImplanted_III))
            {
                return false;
            }
            return false;
        }
    }
    
    public class Celes_Recipe_Surgery_NotForCeles:Celes_Recipe_AddHediff
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            if (!base.AvailableOnNow(thing, part))
            {
                return false;
            }
            if (!(thing is Pawn pawn))
            {
                return false;
            }
            if (pawn.kindDef.race.defName == "Celes_Race")
            {
                return false;
            }
            return false;
        }
    }
    
    public class Celes_Recipe_Surgery_ForCeles:Celes_Recipe_AddHediff
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            if (!base.AvailableOnNow(thing, part))
            {
                return false;
            }
            if (!(thing is Pawn pawn))
            {
                return false;
            }
            if (!(pawn.kindDef.race.defName == "Celes_Race"))
            {
                return false;
            }
            return false;
        }
    }
}