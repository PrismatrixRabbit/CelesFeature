using RimWorld;
using Verse;

namespace CelesFeature
{
	public class Celes_StatPart_HearingPsychicSensitivityFactor : StatPart
	{
		public override void TransformValue(StatRequest req, ref float val)
		{
			if (TryGetPsychicOffset(req.Thing, out var factor))
			{
				if (factor >= 1.25f && factor <= 1.55f)
				{
					val *= 2f;
				}
				
				else if (factor >= 1.1f && factor <= 1.25f)
				{
					val *= 1.75f;
				}
				
				else if (factor >= 1f && factor <=1.1f)
				{
					val *= 1.5f;
					
				}
				
				else if (factor <= 1f && factor >= 0.6f)
				{
					val *= 1f;
				}
				
				else if (factor >= 0.4f && factor <= 0.6f)
				{
					val *= 0.75f;
				}

				else if (factor >= 0.2f && factor <= 0.4f)
				{
					val *= 0.25f;
				}

				else if (factor < 0.2f)
				{
					val *= 0f;
				}
			}
		}

		public override string ExplanationPart(StatRequest req)
		{
			float a = 1f;
			if (TryGetPsychicOffset(req.Thing, out var factor))
			{
				if (factor >= 1.25f && factor <= 1.55f)
				{
					a = 2f;
				}

				else if (factor >= 1.1f && factor <= 1.25f)
				{
					a = 1.75f;
				}

				else if (factor >= 1f && factor <= 1.1f)
				{
					a = 1.5f;

				}

				else if (factor <= 1f && factor >= 0.6f)
				{
					a = 1f;
				}

				else if (factor >= 0.4f && factor <= 0.6f)
				{
					a = 0.75f;
				}

				else if (factor >= 0.2f && factor <= 0.4f)
				{
					a = 0.25f;
				}

				else if (factor < 0.2f)
				{
					a = 0f;
				}
			}

			if (req.HasThing && TryGetPsychicOffset(req.Thing,out factor))
			{
				return "StatsReport_HearingPsychicSensitivityFactor".Translate() + (": x" + a*100 + "%");
			}

			return null;
		}

		private bool TryGetPsychicOffset(Thing t, out float factor)
		{
			if (t != null && t is Pawn pawn)
			{
				float num = PawnCapacityUtility.CalculateTagEfficiency(pawn.health.hediffSet, BodyPartTagDefOf.HearingSource);
				factor = num;
				return true;
			}
			factor = 0f;
			return false;
		}
	}
}