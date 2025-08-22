using RimWorld;
using System.Linq;
using Verse;

namespace CelesFeature
{
    public class Building_Celes_Terminal : Building
    {
        public CompPowerTrader compPowerTrader;
        public Comp_Celes_Linker compLinker;
        public Comp_Celes_ConsumptionFactors compConsumptionFactors;
        public Comp_Celes_Terminal compTerminal;

        private int tickRare = 0;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<int>(ref tickRare, "Celes_SaveKey_int_Building_Terminal_tickRare");
        }
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            compPowerTrader = GetComp<CompPowerTrader>();
            compLinker = GetComp<Comp_Celes_Linker>();
            compConsumptionFactors = GetComp<Comp_Celes_ConsumptionFactors>();
            compTerminal = GetComp<Comp_Celes_Terminal>();
        }

        public Building_Celes_Core GetCore()
        {
            Building_Celes_Core core = (Building_Celes_Core)compLinker.linkedThings.First(x => x.GetType() == typeof(Building_Celes_Core));
            return core;
        }
        public float GetEfficiency()
        {
            if (GetCore() != null)
            {
                if (GetCore().IsActive())
                {
                    return GetCore().GetCoreEfficiency();
                }
                else
                {
                    return compTerminal.Props.efficiencyWithoutCore;
                }
            }
            else
            {
                return compTerminal.Props.efficiencyWithoutCore;
            }
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
        public virtual void ExtraTickRare()
        {
            if (base.Spawned && compPowerTrader.PowerOn)
            {
                if (!this.IsIdle())
                {
                    compPowerTrader.PowerOutput = 0f - compPowerTrader.Props.PowerConsumption;
                }
                else
                {
                    compPowerTrader.PowerOutput = (0f - compPowerTrader.Props.PowerConsumption) * compConsumptionFactors.Props.powerIdleFactor;
                }
            }
        }
    }
}
