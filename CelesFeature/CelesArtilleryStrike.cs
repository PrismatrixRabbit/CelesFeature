using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace CelesFeature
{
	[StaticConstructorOnStartup]
	public class CelesArtilleryStrike : Bombardment
	{
		// 不再用 new 遮蔽基类字段,而是直接使用 Bombardment 的公共字段并在构造函数里赋值。
		// 这样基类 StartStrike/ExposeData 读写的是同一份数值,存档不会出现重复键。
		public CelesArtilleryStrike()
		{
			impactAreaRadius = 15f;
			explosionRadiusRange = new FloatRange(4f, 6f);
			randomFireRadius = 10;
			bombIntervalTicks = 10;
			warmupTicks = 60;
			explosionCount = 100;
		}

		private int ticksToNextEffect;

		private IntVec3 nextExplosionCell = IntVec3.Invalid;

		private List<BombardmentProjectile> projectiles = new List<BombardmentProjectile>();

		private const int StartRandomFireEveryTicks = 20;

		private const int EffectDuration = 60;

		private static readonly Material ProjectileMaterial =
			MaterialPool.MatFrom("Things/Projectile/Bullet_Big", ShaderDatabase.Transparent, Color.black);

		public override void SpawnSetup(Map map, bool respawningAfterReload)
		{
			base.SpawnSetup(map, respawningAfterReload);
			if (!respawningAfterReload)
			{
				GetNextExplosionCell();
			}
		}

		protected override void Tick()
		{
			if (base.Destroyed)
			{
				return;
			}

			if (warmupTicks > 0)
			{
				warmupTicks--;
				if (warmupTicks <= 0)
				{
					StartStrike();
				}
			}
			else
			{
				// 不能调用 base.Tick():那会执行整个原版 Bombardment.Tick(含它自身的 EffectTick/StartRandomFire),
				// 造成双重轰炸。这里复刻 OrbitalStrike.Tick 的等价行为:驱动 comps 并在到期后销毁。
				for (int i = 0; i < AllComps.Count; i++)
				{
					AllComps[i].CompTick();
				}
				if (TicksPassed >= duration)
				{
					Destroy();
				}
				if (base.TicksLeft > 0 && this.IsHashIntervalTick(StartRandomFireEveryTicks))
				{
					StartRandomFire();
				}
			}

			EffectTick();
		}

		private void EffectTick()
		{
			if (!nextExplosionCell.IsValid)
			{
				ticksToNextEffect = warmupTicks - bombIntervalTicks;
				GetNextExplosionCell();
			}

			ticksToNextEffect--;
			if (ticksToNextEffect <= 0 && base.TicksLeft >= bombIntervalTicks)
			{
				SoundDefOf.Bombardment_PreImpact.PlayOneShot(new TargetInfo(nextExplosionCell, base.Map));
				projectiles.Add(new BombardmentProjectile(EffectDuration, nextExplosionCell));
				ticksToNextEffect = bombIntervalTicks;
				GetNextExplosionCell();
			}

			for (int num = projectiles.Count - 1; num >= 0; num--)
			{
				projectiles[num].Tick();
				if (projectiles[num].LifeTime <= 0)
				{
					TryDoExplosion(projectiles[num]);
					projectiles.RemoveAt(num);
				}
			}
		}

		private void TryDoExplosion(BombardmentProjectile proj)
		{
			List<Thing> list = base.Map.listerThings.ThingsInGroup(ThingRequestGroup.ProjectileInterceptor);
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i].TryGetComp<CompProjectileInterceptor>().CheckBombardmentIntercept(this, proj))
				{
					return;
				}
			}

			GenExplosion.DoExplosion(proj.targetCell, base.Map, explosionRadiusRange.RandomInRange, DamageDefOf.Bomb,
				instigator, -10, -5f, null, projectile: def, weapon: weaponDef);
		}

		protected override void DrawAt(Vector3 drawLoc, bool flip = false)
		{
			base.DrawAt(drawLoc, flip);
			if (!projectiles.NullOrEmpty())
			{
				for (int i = 0; i < projectiles.Count; i++)
				{
					projectiles[i].Draw(ProjectileMaterial);
				}
			}
		}

		private void StartRandomFire()
		{
			IntVec3 intVec = (from x in GenRadial.RadialCellsAround(base.Position, randomFireRadius, useCenter: true)
				where x.InBounds(base.Map)
				select x).RandomElementByWeight((IntVec3 x) =>
				DistanceChanceFactor.Evaluate(x.DistanceTo(base.Position)));
			List<Thing> list = base.Map.listerThings.ThingsInGroup(ThingRequestGroup.ProjectileInterceptor);
			for (int i = 0; i < list.Count; i++)
			{
				if (!list[i].TryGetComp<CompProjectileInterceptor>().BombardmentCanStartFireAt(this, intVec))
				{
					return;
				}
			}

			FireUtility.TryStartFireIn(intVec, base.Map, Rand.Range(0.1f, 0.925f), instigator);
		}

		private void GetNextExplosionCell()
		{
			nextExplosionCell = (from x in GenRadial.RadialCellsAround(base.Position, impactAreaRadius, useCenter: true)
				where x.InBounds(base.Map)
				select x).RandomElementByWeight((IntVec3 x) =>
				DistanceChanceFactor.Evaluate(x.DistanceTo(base.Position) / impactAreaRadius));
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref ticksToNextEffect, "celesTicksToNextEffect", 0);
			Scribe_Values.Look(ref nextExplosionCell, "celesNextExplosionCell");
			Scribe_Collections.Look(ref projectiles, "celesProjectiles", LookMode.Deep);
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				if (!nextExplosionCell.IsValid)
				{
					GetNextExplosionCell();
				}

				if (projectiles == null)
				{
					projectiles = new List<BombardmentProjectile>();
				}
			}
		}
	}

	public class Verb_ArtilleryStrike : Verb_CastBase
	{
		[DefOf]
		public static class CelesThingDefOf
		{
			public static ThingDef CelesArtilleryStrike;
		}

		protected override bool TryCastShot()
		{
			if (currentTarget.HasThing && currentTarget.Thing.Map != caster.Map)
			{
				return false;
			}

			CelesArtilleryStrike obj = (CelesArtilleryStrike)GenSpawn.Spawn(CelesThingDefOf.CelesArtilleryStrike,
				currentTarget.Cell, caster.Map);
			obj.instigator = caster;
			obj.weaponDef = ((base.EquipmentSource != null) ? base.EquipmentSource.def : null);
			base.ReloadableCompSource?.UsedOnce();
			return true;
		}

		public override float HighlightFieldRadiusAroundTarget(out bool needLOSToCenter)
		{
			needLOSToCenter = false;
			return 23f;
		}
	}
}
