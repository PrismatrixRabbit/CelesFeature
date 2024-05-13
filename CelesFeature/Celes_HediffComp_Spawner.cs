using System.Collections.Generic;
using RimWorld;
using Verse;

namespace CelesFeature
{
	public class Celes_HediffCompSpawner : HediffComp
	{
		private int ticksUntilSpawn;

		public Celes_CompProperties_Spawner PropsSpawner => (Celes_CompProperties_Spawner)props;

		private bool PowerOn => Pawn.GetComp<CompPowerTrader>()?.PowerOn ?? false;

		public override void  CompPostMake()
		{
				ResetCountdown();
		}

		public override void CompPostTick(ref float severityAdjustment)
		{
			TickInterval(1);
		}
		
		private void TickInterval(int interval)
		{
			if (!Pawn.Spawned)
			{
				return;
			}

			CompCanBeDormant comp = Pawn.GetComp<CompCanBeDormant>();
			if (comp != null)
			{
				if (!comp.Awake)
				{
					return;
				}
			}
			else if (Pawn.Position.Fogged(Pawn.Map))
			{
				return;
			}

			if (!PropsSpawner.requiresPower || PowerOn)
			{
				ticksUntilSpawn -= interval;
				CheckShouldSpawn();
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
			if (!Pawn.Spawned)
			{
				return false;
			}

			if (PropsSpawner.spawnMaxAdjacent >= 0)
			{
				int num = 0;
				for (int i = 0; i < 9; i++)
				{
					IntVec3 c = Pawn.Position + GenAdj.AdjacentCellsAndInside[i];
					if (!c.InBounds(Pawn.Map))
					{
						continue;
					}

					List<Thing> thingList = c.GetThingList(Pawn.Map);
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

			if (TryFindSpawnCell(Pawn, PropsSpawner.thingToSpawn, PropsSpawner.spawnCount, out var result))
			{
				Thing thing = ThingMaker.MakeThing(PropsSpawner.thingToSpawn);
				thing.stackCount = PropsSpawner.spawnCount;
				if (thing == null)
				{
					Log.Error("Could not spawn anything for " + Pawn);
				}

				if (PropsSpawner.inheritFaction && thing.Faction != Pawn.Faction)
				{
					thing.SetFaction(Pawn.Faction);
				}

				GenPlace.TryPlaceThing(thing, result, Pawn.Map, ThingPlaceMode.Direct, out var lastResultingThing);
				if (PropsSpawner.spawnForbidden)
				{
					lastResultingThing.SetForbidden(value: true);
				}

				if (PropsSpawner.showMessageIfOwned && Pawn.Faction == Faction.OfPlayer)
				{
					Messages.Message("MessageCompSpawnerSpawnedItem".Translate(PropsSpawner.thingToSpawn.LabelCap),
						thing, MessageTypeDefOf.PositiveEvent);
				}

				return true;
			}

			return false;
		}

		public static bool TryFindSpawnCell(Thing Pawn, ThingDef thingToSpawn, int spawnCount, out IntVec3 result)
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
					                                                 spawnCount))
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
			ticksUntilSpawn = PropsSpawner.spawnIntervalRange.RandomInRange;
		}

		public override void CompExposeData()
		{
			string text = (PropsSpawner.saveKeysPrefix.NullOrEmpty() ? null : (PropsSpawner.saveKeysPrefix + "_"));
			Scribe_Values.Look(ref ticksUntilSpawn, text + "ticksUntilSpawn", 0);
		}

		public override IEnumerable<Gizmo> CompGetGizmos()
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
	}
}