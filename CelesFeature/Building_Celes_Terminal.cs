using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    public class Building_Celes_Terminal : Building
    {
        public CompPowerTrader compPowerTrader;
        public Comp_Celes_ConsumptionFactors compConsumptionFactors;
        public Comp_Celes_Terminal compTerminal;
        public Building_Celes_Core core;

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
            compConsumptionFactors = GetComp<Comp_Celes_ConsumptionFactors>();
            compTerminal = GetComp<Comp_Celes_Terminal>();
        }

        public Building_Celes_Core GetCore()
        {
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
            if (core != null && (core.DestroyedOrNull() || !core.Spawned))
            {
                core = null;
            }
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

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            if (core != null)
            {
                if (core.IsActive())
                {
                    GenDraw.DrawLineBetween(this.TrueCenter(), core.TrueCenter());
                }
                else
                {
                    GenDraw.DrawLineBetween(this.TrueCenter(), core.TrueCenter(), MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent, new Color(1f, 0.5f, 0.5f)));
                }
            }
        }
    }
}
