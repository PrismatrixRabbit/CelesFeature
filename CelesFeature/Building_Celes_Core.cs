using RimWorld;
using Verse;

namespace CelesFeature
{
    public class Building_Celes_Core : Building
    {
        public CompPowerTrader compPowerTrader;
        public Comp_Celes_Linker compLinker;
        public Comp_Celes_Refuelable compRefuelable;
        public Comp_Celes_ConsumptionFactors compConsumptionFactors;
        public Comp_Celes_Maintenance compMaintenance;

        private int tickRare = 0;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<int>(ref tickRare, "Celes_SaveKey_int_Building_Core_tickRare");
        }
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            compPowerTrader = GetComp<CompPowerTrader>();
            compLinker = GetComp<Comp_Celes_Linker>();
            compRefuelable = GetComp<Comp_Celes_Refuelable>();
            compConsumptionFactors = GetComp<Comp_Celes_ConsumptionFactors>();
            compMaintenance = GetComp<Comp_Celes_Maintenance>();
        }

        public float GetCoreEfficiency()
        {
            float num = 1f;
            if (compMaintenance != null)
            {
                num *= compMaintenance.Efficiency;
            }
            return num;
        }

        public bool IsActive()
        {
            if (compRefuelable != null)
            {
                if (!compRefuelable.HasFuel)
                {
                    return false;
                }
            }
            if (compPowerTrader != null)
            {
                if (!compPowerTrader.PowerOn)
                {
                    return false;
                }
            }
            return true;
        }
        protected override void Tick()
        {
            base.Tick();
            tickRare++;
            if (tickRare >= 250)
            {
                tickRare = 0;
                ExtraTickRare();
            }
        }
        public void ExtraTickRare()
        {
            if (base.Spawned && compPowerTrader.PowerOn)
            {
                float num = compPowerTrader.Props.PowerConsumption;
                if (compLinker != null && compLinker.linkedThings.Count > 0)
                {
                    foreach (Thing thing in compLinker.linkedThings)
                    {
                        Comp_Celes_Terminal comp = thing.TryGetComp<Comp_Celes_Terminal>();
                        if (comp != null)
                        {
                            if (comp.Props.extraPowerConsumptionForCore > 0)
                            {
                                num += comp.Props.extraPowerConsumptionForCore;
                            }
                        }
                    }
                }
                if (!this.IsIdle())
                {
                    compPowerTrader.PowerOutput = 0f - num;
                }
                else
                {
                    compPowerTrader.PowerOutput = (0f - num) * compConsumptionFactors.Props.powerIdleFactor;
                }
            }
        }
    }
}
