using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    public class Celes_ExtraDamage:DamageWorker_AddInjury
    {
        public override DamageResult Apply(DamageInfo damageInfo, Thing thing)
        {
            if(!(thing is Pawn pawn))
            {
                return base.Apply(damageInfo, thing);
            }
            if (damageInfo.Def == Celes_DamageDefOf.Celes_Particle)
            {
                DamageInfo NewDamageInfo=new DamageInfo(damageInfo);
                NewDamageInfo.SetAmount(NewDamageInfo.Amount*0.5f);
                ApplyToPawn(damageInfo, pawn);
                return ApplyToPawn(NewDamageInfo, pawn);
            }
            if (damageInfo.Def == Celes_DamageDefOf.Celes_MassiveParticle)
            {
	            DamageInfo NewDamageInfo=new DamageInfo(damageInfo);
	            NewDamageInfo.SetAmount(NewDamageInfo.Amount*0.7f);
	            ApplyToPawn(damageInfo, pawn);
	            return ApplyToPawn(NewDamageInfo, pawn);
            }
            return ApplyToPawn(damageInfo, pawn);
        }
        private DamageResult ApplyToPawn(DamageInfo dinfo, Pawn pawn)
	{
		DamageResult damageResult = new DamageResult();
		if (dinfo.Amount <= 0f)
		{
			return damageResult;
		}
		if (!DebugSettings.enablePlayerDamage && pawn.Faction == Faction.OfPlayer)
		{
			return damageResult;
		}
		Map mapHeld = pawn.MapHeld;
		bool spawnedOrAnyParentSpawned = pawn.SpawnedOrAnyParentSpawned;
		if (dinfo.ApplyAllDamage)
		{
			float num = dinfo.Amount;
			int num2 = 25;
			float b = num / (float)dinfo.DamagePropagationPartsRange.RandomInRange;
			do
			{
				DamageInfo dinfo2 = dinfo;
				dinfo2.SetAmount(Mathf.Min(num, b));
				ApplyDamageToPart(dinfo2, pawn, damageResult);
				num -= damageResult.totalDamageDealt;
			}
			while (num2-- > 0 && num > 0f);
		}
		else if (dinfo.AllowDamagePropagation && dinfo.Amount >= (float)dinfo.Def.minDamageToFragment)
		{
			int randomInRange = dinfo.DamagePropagationPartsRange.RandomInRange;
			for (int i = 0; i < randomInRange; i++)
			{
				DamageInfo dinfo3 = dinfo;
				dinfo3.SetAmount(dinfo.Amount / (float)randomInRange);
				ApplyDamageToPart(dinfo3, pawn, damageResult);
			}
		}
		else
		{
			ApplyDamageToPart(dinfo, pawn, damageResult);
			ApplySmallPawnDamagePropagation(dinfo, pawn, damageResult);
		}
		if (damageResult.wounded)
		{
			PlayWoundedVoiceSound(dinfo, pawn);
			pawn.Drawer.Notify_DamageApplied(dinfo);
			EffecterDef damageEffecter = pawn.RaceProps.FleshType.damageEffecter;
			if (damageEffecter != null)
			{
				if (pawn.health.woundedEffecter != null && pawn.health.woundedEffecter.def != damageEffecter)
				{
					pawn.health.woundedEffecter.Cleanup();
				}
				pawn.health.woundedEffecter = damageEffecter.Spawn();
				pawn.health.woundedEffecter.Trigger(pawn, dinfo.Instigator ?? pawn);
			}
			if (dinfo.Def.damageEffecter != null)
			{
				Effecter effecter = dinfo.Def.damageEffecter.Spawn();
				effecter.Trigger(pawn, pawn);
				effecter.Cleanup();
			}
		}
		if (damageResult.headshot && pawn.Spawned)
		{
			MoteMaker.ThrowText(new Vector3((float)pawn.Position.x + 1f, pawn.Position.y, (float)pawn.Position.z + 1f), pawn.Map, "Headshot".Translate(), Color.white);
			if (dinfo.Instigator != null && dinfo.Instigator is Pawn pawn2)
			{
				pawn2.records.Increment(RecordDefOf.Headshots);
			}
		}
		if ((damageResult.deflected || damageResult.diminished) && spawnedOrAnyParentSpawned)
		{
			EffecterDef effecterDef = (damageResult.deflected ? ((damageResult.deflectedByMetalArmor && dinfo.Def.canUseDeflectMetalEffect) ? ((dinfo.Def != DamageDefOf.Bullet) ? EffecterDefOf.Deflect_Metal : EffecterDefOf.Deflect_Metal_Bullet) : ((dinfo.Def != DamageDefOf.Bullet) ? EffecterDefOf.Deflect_General : EffecterDefOf.Deflect_General_Bullet)) : ((!damageResult.diminishedByMetalArmor) ? EffecterDefOf.DamageDiminished_General : EffecterDefOf.DamageDiminished_Metal));
			if (pawn.health.deflectionEffecter == null || pawn.health.deflectionEffecter.def != effecterDef)
			{
				if (pawn.health.deflectionEffecter != null)
				{
					pawn.health.deflectionEffecter.Cleanup();
					pawn.health.deflectionEffecter = null;
				}
				pawn.health.deflectionEffecter = effecterDef.Spawn();
			}
			pawn.health.deflectionEffecter.Trigger(pawn, dinfo.Instigator ?? pawn);
			if (damageResult.deflected)
			{
				pawn.Drawer.Notify_DamageDeflected(dinfo);
			}
		}
		if (!damageResult.deflected && spawnedOrAnyParentSpawned)
		{
			ImpactSoundUtility.PlayImpactSound(pawn, dinfo.Def.impactSoundType, mapHeld);
		}
		return damageResult;
	}
	private void ApplyDamageToPart(DamageInfo dinfo, Pawn pawn, DamageResult result)
	{
		BodyPartRecord exactPartFromDamageInfo = GetExactPartFromDamageInfo(dinfo, pawn);
		if (exactPartFromDamageInfo == null)
		{
			return;
		}
		dinfo.SetHitPart(exactPartFromDamageInfo);
		float num = dinfo.Amount;
		bool num2 = !dinfo.InstantPermanentInjury && !dinfo.IgnoreArmor;
		bool deflectedByMetalArmor = false;
		if (num2)
		{
			DamageDef damageDef = dinfo.Def;
			num = ArmorUtility.GetPostArmorDamage(pawn, num, dinfo.ArmorPenetrationInt, dinfo.HitPart, ref damageDef, out deflectedByMetalArmor, out var diminishedByMetalArmor);
			dinfo.Def = damageDef;
			if (num < dinfo.Amount)
			{
				result.diminished = true;
				result.diminishedByMetalArmor = diminishedByMetalArmor;
			}
		}
		if (dinfo.Def.ExternalViolenceFor(pawn))
		{
			num *= pawn.GetStatValue(StatDefOf.IncomingDamageFactor);
		}
		if (num <= 0f)
		{
			result.AddPart(pawn, dinfo.HitPart);
			result.deflected = true;
			result.deflectedByMetalArmor = deflectedByMetalArmor;
			return;
		}
		if (IsHeadshot(dinfo, pawn))
		{
			result.headshot = true;
		}
		if (!dinfo.InstantPermanentInjury || (HealthUtility.GetHediffDefFromDamage(dinfo.Def, pawn, dinfo.HitPart).CompPropsFor(typeof(HediffComp_GetsPermanent)) != null && dinfo.HitPart.def.permanentInjuryChanceFactor != 0f && !pawn.health.hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(dinfo.HitPart)))
		{
			if (!dinfo.AllowDamagePropagation)
			{
				FinalizeAndAddInjury(pawn, num, dinfo, result);
			}
			else
			{
				ApplySpecialEffectsToPart(pawn, num, dinfo, result);
			}
		}
	}
	private static bool IsHeadshot(DamageInfo dinfo, Pawn pawn)
	{
		if (dinfo.InstantPermanentInjury)
		{
			return false;
		}
		if (dinfo.HitPart.groups.Contains(BodyPartGroupDefOf.FullHead))
		{
			return dinfo.Def.isRanged;
		}
		return false;
	}
	private BodyPartRecord GetExactPartFromDamageInfo(DamageInfo dinfo, Pawn pawn)
	{
		if (dinfo.HitPart != null)
		{
			if (!pawn.health.hediffSet.GetNotMissingParts().Any((BodyPartRecord x) => x == dinfo.HitPart))
			{
				return null;
			}
			return dinfo.HitPart;
		}
		BodyPartRecord bodyPartRecord = ChooseHitPart(dinfo, pawn);
		if (bodyPartRecord == null)
		{
			Log.Warning("ChooseHitPart returned null (any part).");
		}
		return bodyPartRecord;
	}
	private static void PlayWoundedVoiceSound(DamageInfo dinfo, Pawn pawn)
	{
		if (!pawn.Dead && !dinfo.InstantPermanentInjury && pawn.SpawnedOrAnyParentSpawned && dinfo.Def.ExternalViolenceFor(pawn))
		{
			LifeStageUtility.PlayNearestLifestageSound(pawn, (LifeStageAge lifeStage) => lifeStage.soundWounded, (GeneDef gene) => gene.soundWounded, (MutantDef mutantDef) => mutantDef.soundWounded);
		}
	}
	private void ApplySmallPawnDamagePropagation(DamageInfo dinfo, Pawn pawn, DamageResult result)
	{
		if (dinfo.AllowDamagePropagation && result.LastHitPart != null && dinfo.Def.harmsHealth && result.LastHitPart != pawn.RaceProps.body.corePart && result.LastHitPart.parent != null && pawn.health.hediffSet.GetPartHealth(result.LastHitPart.parent) > 0f && result.LastHitPart.parent.coverageAbs > 0f && dinfo.Amount >= 10f && pawn.HealthScale <= 0.5001f)
		{
			DamageInfo dinfo2 = dinfo;
			dinfo2.SetHitPart(result.LastHitPart.parent);
			ApplyDamageToPart(dinfo2, pawn, result);
		}
	}
    }
}