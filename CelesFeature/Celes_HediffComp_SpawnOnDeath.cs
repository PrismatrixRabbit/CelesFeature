using RimWorld;
using Verse;

namespace CelesFeature
{
    public class Celes_HediffComp_SpawnOnDeath : HediffComp
    {
        public Celes_HediffCompProperties_SpawnOnDeath Props => (Celes_HediffCompProperties_SpawnOnDeath)props;

        public override void Notify_PawnKilled()
        {
            base.Pawn.Corpse.Destroy();
            base.Pawn.apparel.DropAll(base.Pawn.Position);
            base.Pawn.equipment.DropAllEquipment(base.Pawn.Position);
            for (int i = 0; i < Props.spawnCount; i++)
            {
                GenSpawn.Spawn(Props.spawnThing,base.Pawn.Position,base.Pawn.Map);
            }
            if (Props.spawnPawn)
            {
                GenSpawn.Spawn(Props.spawnPawnDef,base.Pawn.Position,base.Pawn.Map);
            }
        }
    }
}