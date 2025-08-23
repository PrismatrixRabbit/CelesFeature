using RimWorld;
using System.Collections.Generic;
using Verse;

namespace CelesFeature
{
    public class Comp_Celes_Maintenance : ThingComp
    {
        Comp_Celes_Linker Linker;

        public float maintenance = 1.2f;
        public float maintenanceThreshold = 0.6f;
        public int lastMaintenanceTick = -1;
        private int timing = 0;

        public override void PostExposeData()
        {
            Scribe_Values.Look<float>(ref maintenance, "Celes_SaveKey_float_Comp_Maintenance_maintenance");
            Scribe_Values.Look<float>(ref maintenanceThreshold, "Celes_SaveKey_float_Comp_Maintenance_maintenanceThreshold");
            Scribe_Values.Look<int>(ref lastMaintenanceTick, "Celes_SaveKey_int_Comp_Maintenance_lastMaintenanceTick");
            Scribe_Values.Look<int>(ref timing, "Celes_SaveKey_int_Comp_Maintenance_timing");
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            Linker = parent.TryGetComp<Comp_Celes_Linker>();
        }

        public float maintenancePercentOfMax => maintenance / 1.2f;

        public CompProperties_Celes_Maintenance Props
        {
            get
            {
                return (CompProperties_Celes_Maintenance)this.props;
            }
        }
        public float Efficiency
        {
            get
            {
                if (maintenance >= 0.6f)
                {
                    return 1.2f;
                }
                if (maintenance >= 0.3f)
                {
                    return 1f;
                }
                return 0.75f;
            }
        }

        public override void CompTick()
        {
            timing++;
            if (timing >= 250)
            {
                TimingRare();
                timing = 0;
            }
        }
        public void TimingRare()
        {
            if (ShouldDecay())
            {
                float num = Props.decayPerDay;
                if (parent.GetType() == typeof(Building_Celes_Core))
                {
                    if (Linker != null && Linker.linkedThings.Count > 0)
                    {
                        foreach (Thing thing in Linker.linkedThings)
                        {
                            Comp_Celes_Terminal comp = thing.TryGetComp<Comp_Celes_Terminal>();
                            if (comp != null)
                            {
                                if (comp.Props.extraMaintenanceDecayForCore > 0)
                                {
                                    num += comp.Props.extraMaintenanceDecayForCore;
                                }
                            }
                        }
                    }
                }
                maintenance -= (num / 240);
            }
        }

        public void GetMaintenance()
        {
            maintenance += Props.recoveryAmount;
            lastMaintenanceTick = Find.TickManager.TicksGame;
        }
        public bool ShouldDecay()
        {
            if (lastMaintenanceTick > 0)
            {
                if (Find.TickManager.TicksGame < lastMaintenanceTick + (Props.maintenanceFreeDays * 60000))
                {
                    return false;
                }
            }
            return true;
        }
        public bool ShouldMaintain()
        {
            if (maintenance <= maintenanceThreshold)
            {
                return true;
            }
            return false;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction == Faction.OfPlayer)
            {
                yield return new Gizmo_Celes_Maintenance(this);
            }
            yield break;
        }
    }
}
