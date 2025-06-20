using System.Collections.Generic;
using RimWorld;
using Verse;

namespace CelesFeature
{
	public class Celes_CompSpawner : ThingComp
	{
		private int ticksUntilSpawn;

		public Celes_CompProperties_Spawner PropsSpawner => (Celes_CompProperties_Spawner)props;

		private bool PowerOn => parent.GetComp<CompPowerTrader>()?.PowerOn ?? false;

		private bool HasFuel => parent.GetComp<CompRefuelable>()?.HasFuel ?? false;

		private float spawnIntervalH => parent.GetStatValue(Celes_StatDefOf.Celes_spawnInterval);

		private int spawnInterval => (int)(spawnIntervalH * 2500f);
			
		private int spawnCount => (int)parent.GetStatValue(Celes_StatDefOf.Celes_spawnCount);

		public override void PostSpawnSetup(bool respawningAfterLoad)
		{
			if (!respawningAfterLoad)
			{
				ResetCountdown();
			}
		}
		
		public override void CompTick()
		{
			TickInterval(1);
		}

		public override void CompTickRare()
		{
			TickInterval(250);
		}
		
		private void TickInterval(int interval)
		{
			if (!parent.Spawned)
			{
				return;
			}

			CompCanBeDormant comp = parent.GetComp<CompCanBeDormant>();
			if (comp != null)
			{
				if (!comp.Awake)
				{
					return;
				}
			}
			else if (parent.Position.Fogged(parent.Map))
			{
				return;
			}

			if (!PropsSpawner.requiresPower || PowerOn)
			{
				if (!PropsSpawner.needFuel || HasFuel)
				{
					ticksUntilSpawn -= interval;
					CheckShouldSpawn();
				}
			}
		}

		private void CheckShouldSpawn()
		{
			if (ticksUntilSpawn <= 0)
			{
				ResetCountdown();
				TryDoSpawn();
			}
		}

		public bool TryDoSpawn()
		{
			if (!parent.Spawned)
			{
				return false;
			}

			if (PropsSpawner.spawnMaxAdjacent >= 0)
			{
				int num = 0;
				for (int i = 0; i < 9; i++)
				{
					IntVec3 c = parent.Position + GenAdj.AdjacentCellsAndInside[i];
					if (!c.InBounds(parent.Map))
					{
						continue;
					}

					List<Thing> thingList = c.GetThingList(parent.Map);
					for (int j = 0; j < thingList.Count; j++)
					{
						if (thingList[j].def == PropsSpawner.thingToSpawn)
						{
							num += thingList[j].stackCount;
							if (num >= PropsSpawner.spawnMaxAdjacent)
							{
								return false;
							}
						}
					}
				}
			}

			if (TryFindSpawnCell(parent, PropsSpawner.thingToSpawn, spawnCount, out var result))
			{
				Thing thing = ThingMaker.MakeThing(PropsSpawner.thingToSpawn);
				thing.stackCount = spawnCount;
				if (thing == null)
				{
					Log.Error("Could not spawn anything for " + parent);
				}

				if (PropsSpawner.inheritFaction && thing.Faction != parent.Faction)
				{
					thing.SetFaction(parent.Faction);
				}

				GenPlace.TryPlaceThing(thing, result, parent.Map, ThingPlaceMode.Direct, out var lastResultingThing);
				if (PropsSpawner.spawnForbidden)
				{
					lastResultingThing.SetForbidden(value: true);
				}

				if (PropsSpawner.showMessageIfOwned && parent.Faction == Faction.OfPlayer)
				{
					Messages.Message("MessageCompSpawnerSpawnedItem".Translate(PropsSpawner.thingToSpawn.LabelCap),
						thing, MessageTypeDefOf.PositiveEvent);
				}

				return true;
			}

			return false;
		}

		public static bool TryFindSpawnCell(Thing Pawn, ThingDef thingToSpawn, int _spawnCount, out IntVec3 result)
		{
			foreach (IntVec3 item in GenAdj.CellsAdjacent8Way(Pawn).InRandomOrder())
			{
				if (!item.Walkable(Pawn.Map))
				{
					continue;
				}

				Building edifice = item.GetEdifice(Pawn.Map);
				if ((edifice != null && thingToSpawn.IsEdifice()) || (edifice is Building_Door building_Door && !building_Door.FreePassage) ||
				    (Pawn.def.passability != Traversability.Impassable &&
				     !GenSight.LineOfSight(Pawn.Position, item, Pawn.Map)))
				{
					continue;
				}

				bool flag = false;
				List<Thing> thingList = item.GetThingList(Pawn.Map);
				for (int i = 0; i < thingList.Count; i++)
				{
					Thing thing = thingList[i];
					if (thing.def.category == ThingCategory.Item && (thing.def != thingToSpawn ||
					                                                 thing.stackCount > thingToSpawn.stackLimit -
					                                                 _spawnCount))
					{
						flag = true;
						break;
					}
				}

				if (!flag)
				{
					result = item;
					return true;
				}
			}

			result = IntVec3.Invalid;
			return false;
		}

		private void ResetCountdown()
		{
			ticksUntilSpawn = spawnInterval;
		}

		public override void PostExposeData()
		{
			string text = (PropsSpawner.saveKeysPrefix.NullOrEmpty() ? null : (PropsSpawner.saveKeysPrefix + "_"));
			Scribe_Values.Look(ref ticksUntilSpawn, text + "ticksUntilSpawn", 0);
		}

		public override IEnumerable<Gizmo> CompGetGizmosExtra()
		{
			if (DebugSettings.ShowDevGizmos)
			{
				Command_Action command_Action = new Command_Action();
				command_Action.defaultLabel = "DEV: Spawn " + PropsSpawner.thingToSpawn.label;
				command_Action.icon = TexCommand.DesirePower;
				command_Action.action = delegate
				{
					ResetCountdown();
					TryDoSpawn();
				};
				yield return command_Action;
			}
		}

		public override string CompInspectStringExtra()
		{
			if (PropsSpawner.writeTimeLeftToSpawn && (!PropsSpawner.requiresPower || PowerOn))
			{
				return "NextSpawnedItemIn".Translate(GenLabel.ThingLabel(PropsSpawner.thingToSpawn, null, spawnCount)).Resolve() + ": " + ticksUntilSpawn.ToStringTicksToPeriod().Colorize(ColoredText.DateTimeColor);
			}
			return null;
		}
	}
}