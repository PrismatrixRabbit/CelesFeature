using RimWorld;
using Verse;

namespace CelesFeature
{
    public class CompUseEffect_CallReinforcement : CompUseEffect
    {
        
        public CompProperties_CallReinforcement Props => (CompProperties_CallReinforcement)props;
        public override void DoEffect(Pawn usedby)
        {
            foreach (IntVec3 thingPosition in GenAdj.CellsAdjacent8Way(parent))
            {
                Thing target = parent.Map.thingGrid.ThingAt(thingPosition, Props.needThingDef);
                if (target == null)
                {
                    Log.Message("No specific item to use");
                }
                else
                {
                    GenSpawn.Spawn(Props.thingDef, parent.Position, parent.Map);
                    target.DeSpawn();
                    break;
                }
            }
        }
    }

    public class CompProperties_CallReinforcement : CompProperties
    {
        public ThingDef thingDef;
        public ThingDef needThingDef;
        
        public CompProperties_CallReinforcement()
        {
            compClass = typeof(CompUseEffect_CallReinforcement);
        }
    }
}