using System;
using System.Text;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace CelesFeature
{
    
    [StaticConstructorOnStartup]
    public class Celes_Comp_MutiFunctionalShield : ThingComp
    {
		protected float energy;
		protected int ticksToReset = -1;

		private static readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();
		private Material _bubbleMat;
		private Vector3 impactAngleVect;
		
		protected int lastKeepDisplayTick = -9999;
		private int lastAbsorbDamageTick = -9999;
		private const float MaxDamagedJitterDist = 0.05f;
		private const int JitterDurationTicks = 8;
		private int KeepDisplayingTicks = 1000;
		private float ApparelScorePerEnergyMax = 0.25f;

		public Celes_CompProperties_MutiFunctionalShield Props => (Celes_CompProperties_MutiFunctionalShield)props;
		private float EnergyMax => parent.GetStatValue(StatDefOf.EnergyShieldEnergyMax);
		private float EnergyGainPerTick => parent.GetStatValue(StatDefOf.EnergyShieldRechargeRate) / 60f;
		public float Energy => energy;
		
		public override void Initialize(CompProperties props)
		{
			base.Initialize(props);
			if (!_materials.TryGetValue(Props.texturePath, out _bubbleMat))
			{
				_bubbleMat = MaterialPool.MatFrom(Props.texturePath, ShaderDatabase.Transparent);
				_materials.Add(Props.texturePath, _bubbleMat);
			}
		}
		
		public ShieldState ShieldState
		{
			get
			{
				if (parent is Pawn p && (p.IsCharging() || p.IsSelfShutdown()))
				{
					return ShieldState.Disabled;
				}
				CompCanBeDormant comp = parent.GetComp<CompCanBeDormant>();
				if (comp != null && !comp.Awake)
				{
					return ShieldState.Disabled;
				}
				if (ticksToReset <= 0)
				{
					return ShieldState.Active;
				}
				return ShieldState.Resetting;
			}
		}
		
		
		protected bool ShouldDisplay
		{
			get
			{
				Pawn pawnOwner = PawnOwner;
				if (!pawnOwner.Spawned || pawnOwner.Dead || pawnOwner.Downed)
				{
					return false;
				}
				if (pawnOwner.InAggroMentalState)
				{
					return true;
				}
				if (pawnOwner.Drafted)
				{
					return true;
				}
				if (pawnOwner.Faction.HostileTo(Faction.OfPlayer) && !pawnOwner.IsPrisoner)
				{
					return true;
				}
				if (Find.TickManager.TicksGame < lastKeepDisplayTick + KeepDisplayingTicks)
				{
					return true;
				}
				if (ModsConfig.BiotechActive && pawnOwner.IsColonyMech && Find.Selector.SingleSelectedThing == pawnOwner)
				{
					return true;
				}
				return false;
			}
		}

		protected Pawn PawnOwner
		{
			get
			{
				if (parent is Apparel apparel)
				{
					return apparel.Wearer;
				}
				if (parent is Pawn result)
				{
					return result;
				}
				return null;
			}
		}

		public bool IsApparel => parent is Apparel;

		private bool IsBuiltIn => !IsApparel;
		
		
		public override void PostExposeData()
		{
			base.PostExposeData();
			Scribe_Values.Look(ref energy, "energy", 0f);
			Scribe_Values.Look(ref ticksToReset, "ticksToReset", -1);
			Scribe_Values.Look(ref lastKeepDisplayTick, "lastKeepDisplayTick", 0);
		}
		
		public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
		{
			foreach (Gizmo item in base.CompGetWornGizmosExtra())
			{
				yield return item;
			}
			if (IsApparel)
			{
				foreach (Gizmo gizmo in GetGizmos())
				{
					yield return gizmo;
				}
			}
			if (!DebugSettings.ShowDevGizmos)
			{
				yield break;
			}
			Command_Action command_Action = new Command_Action();
			command_Action.defaultLabel = "DEV: Break";
			command_Action.action = Break;
			yield return command_Action;
			if (ticksToReset > 0)
			{
				Command_Action command_Action2 = new Command_Action();
				command_Action2.defaultLabel = "DEV: Clear reset";
				command_Action2.action = delegate
				{
					ticksToReset = 0;
				};
				yield return command_Action2;
			}
		}
		
		public override IEnumerable<Gizmo> CompGetGizmosExtra()
		{
			foreach (Gizmo item in base.CompGetGizmosExtra())
			{
				yield return item;
			}
			if (!IsBuiltIn)
			{
				yield break;
			}
			foreach (Gizmo gizmo in GetGizmos())
			{
				yield return gizmo;
			}
		}

		private IEnumerable<Gizmo> GetGizmos()
		{
			if ((PawnOwner.Faction == Faction.OfPlayer || (parent is Pawn pawn && pawn.RaceProps.IsMechanoid)) && Find.Selector.SingleSelectedThing == PawnOwner)
			{
				Celes_Gizmo_MutiFunctionalShieldStatus gizmo_ShieldStatus = new Celes_Gizmo_MutiFunctionalShieldStatus();
				gizmo_ShieldStatus.shield = this;
				yield return gizmo_ShieldStatus;
			}
		}

		public override float CompGetSpecialApparelScoreOffset()
		{
			return EnergyMax * ApparelScorePerEnergyMax;
		}
		
		public override void CompTick()
		{
			base.CompTick();
			if (PawnOwner == null)
			{
				energy = 0f;
			}
			else if (ShieldState == ShieldState.Resetting)
			{
				ticksToReset--;
				if (ticksToReset <= 0)
				{
					Reset();
				}
			}
			else if (ShieldState == ShieldState.Active)
			{
				energy += EnergyGainPerTick;
				if (energy > EnergyMax)
				{
					energy = EnergyMax;
				}
			}
		}

		public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
		{
			
			float celesMutiShieldFactor = 1f;
			bool celesMutiShieldTakeDamage = false;
			bool IsMeleeDamage =
				dinfo.Def == Celes_DamageDefOf.Cut || dinfo.Def == Celes_DamageDefOf.Crush || dinfo.Def == Celes_DamageDefOf.Blunt 
				|| dinfo.Def == Celes_DamageDefOf.Poke || dinfo.Def == Celes_DamageDefOf.Demolish || dinfo.Def == Celes_DamageDefOf.Stab 
				|| dinfo.Def == Celes_DamageDefOf.Scratch || dinfo.Def == Celes_DamageDefOf.ScratchToxic || dinfo.Def == Celes_DamageDefOf.Bite 
				|| dinfo.Def == Celes_DamageDefOf.ToxicBite;
			
			absorbed = false;
			
			if (ShieldState != 0 || PawnOwner == null)
			{
				return;
			}
			
			if (dinfo.Def == DamageDefOf.EMP)
			{
				celesMutiShieldFactor = Props.energyLossEMPFactor;
				celesMutiShieldTakeDamage = true;
			}
			else if (dinfo.Def.isExplosive)
			{
				celesMutiShieldFactor = Props.energyLossExplosiveFactor;
				celesMutiShieldTakeDamage = true;
			}
			else if (dinfo.Def.isRanged || IsMeleeDamage)
			{
				celesMutiShieldFactor = Props.energyLossDefaultFactor;
				celesMutiShieldTakeDamage = true;
			}

			if (!celesMutiShieldTakeDamage) return;

			energy -= dinfo.Amount * celesMutiShieldFactor;
			if (energy < 0f)
			{
				Break();
			}
			else
			{
				AbsorbedDamage(dinfo);
			}
			absorbed = true;
		}

		public void KeepDisplaying()
		{
			lastKeepDisplayTick = Find.TickManager.TicksGame;
		}
			
		private void AbsorbedDamage(DamageInfo dinfo)
		{
			SoundDefOf.EnergyShield_AbsorbDamage.PlayOneShot(new TargetInfo(PawnOwner.Position, PawnOwner.Map));
			impactAngleVect = Vector3Utility.HorizontalVectorFromAngle(dinfo.Angle);
			Vector3 loc = PawnOwner.TrueCenter() + impactAngleVect.RotatedBy(180f) * 0.5f;
			float num = Mathf.Min(10f, 2f + dinfo.Amount / 10f);
			FleckMaker.Static(loc, PawnOwner.Map, FleckDefOf.ExplosionFlash, num);
			int num2 = (int)num;
			for (int i = 0; i < num2; i++)
			{
				FleckMaker.ThrowDustPuff(loc, PawnOwner.Map, Rand.Range(0.8f, 1.2f));
			}
			lastAbsorbDamageTick = Find.TickManager.TicksGame;
			KeepDisplaying();
		}

		private void Break()
		{
			if (parent.Spawned)
			{
				float scale = Mathf.Lerp(Props.minDrawSize, Props.maxDrawSize, energy);
				EffecterDefOf.Shield_Break.SpawnAttached(parent, parent.MapHeld, scale);
				FleckMaker.Static(PawnOwner.TrueCenter(), PawnOwner.Map, FleckDefOf.ExplosionFlash, 12f);
				for (int i = 0; i < 6; i++)
				{
					FleckMaker.ThrowDustPuff(PawnOwner.TrueCenter() + Vector3Utility.HorizontalVectorFromAngle(Rand.Range(0, 360)) * Rand.Range(0.3f, 0.6f), PawnOwner.Map, Rand.Range(0.8f, 1.2f));
				}
			}
			energy = 0f;
			ticksToReset = Props.startingTicksToReset;
		}

		private void Reset()
		{
			if (PawnOwner.Spawned)
			{
				SoundDefOf.EnergyShield_Reset.PlayOneShot(new TargetInfo(PawnOwner.Position, PawnOwner.Map));
				FleckMaker.ThrowLightningGlow(PawnOwner.TrueCenter(), PawnOwner.Map, 3f);
			}
			ticksToReset = -1;
			energy = Props.energyOnReset;
		}
		
		public override void CompDrawWornExtras()
		{
			base.CompDrawWornExtras();
			if (IsApparel)
			{
				Draw();
			}
		}

		public override void PostDraw()
		{
			base.PostDraw();
			if (IsBuiltIn)
			{
				Draw();
			}
		}

		private void Draw()
		{
			if (ShieldState == ShieldState.Active && ShouldDisplay)
			{
				float num = Mathf.Lerp(Props.minDrawSize, Props.maxDrawSize, energy);
				Vector3 drawPos = PawnOwner.Drawer.DrawPos;
				drawPos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
				int num2 = Find.TickManager.TicksGame - lastAbsorbDamageTick;
				if (num2 < 8)
				{
					float num3 = (float)(8 - num2) / 8f * 0.05f;
					drawPos += impactAngleVect * num3;
					num -= num3;
				}
				float angle = Rand.Range(0, 360);
				Vector3 s = new Vector3(num, 1f, num);
				Matrix4x4 matrix = default(Matrix4x4);
				matrix.SetTRS(drawPos, Quaternion.AngleAxis(angle, Vector3.up), s);
				Graphics.DrawMesh(MeshPool.plane10, matrix, _bubbleMat, 0);
			}
		}

		public override bool CompAllowVerbCast(Verb verb)
		{
			if (Props.blocksRangedWeapons)
			{
				return !(verb is Verb_LaunchProjectile);
			}
			return true;
		}

    }
    
    [StaticConstructorOnStartup]
    public class Celes_Gizmo_MutiFunctionalShieldStatus : Gizmo
    {
	    public Celes_Comp_MutiFunctionalShield shield;

	    private static readonly Texture2D FullShieldBarTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.2f, 0.2f, 0.24f));

	    private static readonly Texture2D EmptyShieldBarTex = SolidColorMaterials.NewSolidColorTexture(Color.clear);

	    public Celes_Gizmo_MutiFunctionalShieldStatus()
	    {
		    Order = -100f;
	    }

	    public override float GetWidth(float maxWidth)
	    {
		    return 140f;
	    }

	    public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
	    {
		    Rect rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
		    Rect rect2 = rect.ContractedBy(6f);
		    Widgets.DrawWindowBackground(rect);
		    Rect rect3 = rect2;
		    rect3.height = rect.height / 2f;
		    Text.Font = GameFont.Tiny;
		    Widgets.Label(rect3, shield.IsApparel ? shield.parent.LabelCap : "ShieldInbuilt".Translate().Resolve());
		    Rect rect4 = rect2;
		    rect4.yMin = rect2.y + rect2.height / 2f;
		    float fillPercent = shield.Energy / Mathf.Max(1f, shield.parent.GetStatValue(StatDefOf.EnergyShieldEnergyMax));
		    Widgets.FillableBar(rect4, fillPercent, FullShieldBarTex, EmptyShieldBarTex, doBorder: false);
		    Text.Font = GameFont.Small;
		    Text.Anchor = TextAnchor.MiddleCenter;
		    Widgets.Label(rect4, (shield.Energy * 100f).ToString("F0") + " / " + (shield.parent.GetStatValue(StatDefOf.EnergyShieldEnergyMax) * 100f).ToString("F0"));
		    Text.Anchor = TextAnchor.UpperLeft;
		    TooltipHandler.TipRegion(rect2, "ShieldPersonalTip".Translate());
		    return new GizmoResult(GizmoState.Clear);
	    }
    }
}