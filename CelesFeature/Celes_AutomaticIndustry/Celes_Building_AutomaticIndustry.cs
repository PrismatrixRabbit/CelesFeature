using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace CelesFeature
{
    [StaticConstructorOnStartup]
    public class Celes_Building_AutomaticIndustry : Building_WorkTable, IThingHolder, INotifyHauledTo
    {
        public Sustainer workingSound;
        public CompPowerTrader compPowerTrader;
        public Comp_Celes_Linker compLinker;
        public Comp_Celes_ConsumptionFactors compConsumptionFactors;
        public Comp_Celes_Terminal compTerminal;
        public Comp_Celes_AutomaticIndustry compAutomaticIndustry;

        public Celes_Bill_AutomaticIndustry activeBill;
        public ThingOwner innerContainer;
        private int tickRare = 0;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref innerContainer, "Celes_SaveKey_ThingOwner_Building_AutomaticIndustry_innerContainer", this);
            Scribe_References.Look(ref activeBill, "Celes_SaveKey_Bill_Building_AutomaticIndustry_activeBill");
            Scribe_Values.Look<int>(ref tickRare, "Celes_SaveKey_int_Building_AutomaticIndustry_tickRare");
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            compPowerTrader = GetComp<CompPowerTrader>();
            compLinker = GetComp<Comp_Celes_Linker>();
            compConsumptionFactors = GetComp<Comp_Celes_ConsumptionFactors>();
            compTerminal = GetComp<Comp_Celes_Terminal>();
            compAutomaticIndustry = GetComp<Comp_Celes_AutomaticIndustry>();
        }

        public Celes_Bill_AutomaticIndustry ActiveBill
        {
            get
            {
                return activeBill;
            }
            set
            {
                if (activeBill != value)
                {
                    activeBill = value;
                }
            }
        }

        public bool PoweredOn => compPowerTrader.PowerOn;

        public float CurrentBillFormingPercent
        {
            get
            {
                if (activeBill == null || activeBill.State != FormingState.Forming)
                {
                    return 0f;
                }

                return 1f - activeBill.formingTicks / (float)activeBill.recipe.formingTicks;
            }
        }

        public GenDraw.FillableBarRequest BarDrawData => def.building.BarDrawDataFor(base.Rotation);

        public Celes_Building_AutomaticIndustry()
        {
            innerContainer = new ThingOwner<Thing>(this);
        }

        public virtual void Notify_StartForming(Pawn billDoer)
        {
        }

        public virtual void Notify_FormingCompleted()
        {
            Thing thing = activeBill.CreateProducts();
            CompIngredients compIngredients = thing.TryGetComp<CompIngredients>();
            if (compIngredients != null)
            {
                List<ThingDef> list = new List<ThingDef>();
                foreach (Thing t in this.innerContainer)
                {
                    list.Add(t.def);
                }
                for (int i = 0; i < list.Count; i++)
                {
                    compIngredients.RegisterIngredient(list[i]);
                }
            }
            innerContainer.ClearAndDestroyContents();
            innerContainer.TryAdd(thing);
            if (compAutomaticIndustry.Props.autoEjectProducts)
            {
                EjectContents();
                activeBill.Reset();
            }
        }

        public override void Notify_BillDeleted(Bill bill)
        {
            if (activeBill == bill)
            {
                EjectContents();
                activeBill = null;
            }
        }

        public virtual void Notify_HauledTo(Pawn hauler, Thing thing, int count)
        {
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            activeBill?.Reset();
            EjectContents();
            base.DeSpawn(mode);
        }

        public virtual void EjectContents()
        {
            innerContainer.TryDropAll(InteractionCell, base.Map, ThingPlaceMode.Near);
            activeBill?.Reset();
        }

        public virtual bool CanWork()
        {
            return PoweredOn;
        }

        public Building_Celes_Core GetCore()
        {
            Building_Celes_Core core = (Building_Celes_Core)compLinker.linkedThings.First(x => x.GetType() == typeof(Building_Celes_Core));
            return core;
        }
        public bool IsActive()
        {
            if (!compPowerTrader.PowerOn)
            {
                return false;
            }
            if (activeBill == null || activeBill.State != FormingState.Forming)
            {
                return false;
            }
            return true;
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
            innerContainer.DoTick();
            if (activeBill != null && CanWork())
            {
                activeBill.BillTick();
            }
            if (activeBill != null && PoweredOn && activeBill.State != 0)
            {
                if(workingSound == null)
                {
                    workingSound = activeBill.recipe.soundWorking.TrySpawnSustainer(this);
                }
                workingSound.Maintain();
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

        public override string GetInspectString()
        {
            StringBuilder stringBuilder = new StringBuilder();
            string inspectString = base.GetInspectString();
            if (!inspectString.NullOrEmpty())
            {
                stringBuilder.AppendLine(inspectString);
            }

            string inspectStringExtra = GetInspectStringExtra();
            if (!inspectStringExtra.NullOrEmpty())
            {
                stringBuilder.AppendLine(inspectStringExtra);
            }

            if (CanWork() && activeBill != null)
            {
                activeBill.AppendInspectionData(stringBuilder);
            }

            return stringBuilder.ToString().TrimEndNewlines();
        }

        protected virtual string GetInspectStringExtra()
        {
            if (!(activeBill is Celes_Bill_AutomaticIndustry bill_Mech) || bill_Mech.State != FormingState.Forming)
            {
                return null;
            }

            return string.Format("{0}: {1}", "Celes_Keyed_ProductionCycle_Timeleft".Translate(), Mathf.CeilToInt(bill_Mech.formingTicks * 1f).ToStringTicksToPeriod());
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            if (!DebugSettings.ShowDevGizmos)
            {
                yield break;
            }

            Celes_Bill_AutomaticIndustry bill_Autonomous = ActiveBill;
            if (bill_Autonomous != null && bill_Autonomous.State == FormingState.Forming)
            {
                yield return new Command_Action
                {
                    action = delegate
                    {
                        ActiveBill.formingTicks -= (float)ActiveBill.recipe.formingTicks * 0.25f;
                    },
                    defaultLabel = "DEV: Forming cycle +25%"
                };
                yield return new Command_Action
                {
                    action = delegate
                    {
                        ActiveBill.formingTicks = 0f;
                    },
                    defaultLabel = "DEV: Complete cycle"
                };
                if (ActiveBill != null && ActiveBill.State != 0 && ActiveBill.State != FormingState.Formed)
                {
                    Command_Action command_Action2 = new Command_Action();
                    command_Action2.action = ActiveBill.ForceCompleteAllCycles;
                    command_Action2.defaultLabel = "DEV: Complete all cycles";
                    yield return command_Action2;
                }
            }
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return innerContainer;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);
            if (activeBill != null && activeBill.State != 0 && def.building.formingGraphicData != null)
            {
                def.building.formingGraphicData.Graphic.Draw(drawLoc, Rot4.North, this);
            }

            GenDraw.FillableBarRequest barDrawData = BarDrawData;
            barDrawData.center = drawLoc;
            barDrawData.fillPercent = CurrentBillFormingPercent;
            barDrawData.rotation = Rotation;
            GenDraw.DrawFillableBar(barDrawData);
        }
    }
}
