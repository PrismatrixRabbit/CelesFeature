using RimWorld;
using System.Text;
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
        public Comp_Celes_Linker compLinker;

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
            compLinker = GetComp<Comp_Celes_Linker>();
        }

        public Building_Celes_Core GetCore()
        {
            return core;
        }
        public float GetEfficiency()
        {
            float num = 0;
            if (compLinker != null && compLinker.ModifierList.Count > 0)
            {
                foreach (Comp_Celes_ModifierLinker comp in compLinker.ModifierList)
                {
                    num += comp.Props.extraTerminalEfficiency;
                }
            }
            if (GetCore() != null)
            {
                if (GetCore().IsActive())
                {
                    return GetCore().GetCoreEfficiency() + num;
                }
                else
                {
                    return compTerminal.Props.efficiencyWithoutCore + num;
                }
            }
            else
            {
                return compTerminal.Props.efficiencyWithoutCore + num;
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

        public override string GetInspectString()
        {
            StringBuilder stringBuilder = new StringBuilder();
            string inspectString = base.GetInspectString();
            if (!inspectString.NullOrEmpty())
            {
                stringBuilder.AppendLine(inspectString);
            }

            string text = "Celes_Keyed_CurrentEfficiency".Translate(GetEfficiency().ToStringPercent());
            stringBuilder.AppendLine(text);

            return stringBuilder.ToString().TrimEndNewlines();
        }
    }
}
