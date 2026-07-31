using System.Collections.Generic;
using RimWorld;
using Verse;

namespace CelesFeature
{
    //健康状态移除替换 usable thing
    
    public class Celes_CompProperties_RemoveMechlinkRestriction : CompProperties_UseEffect
    {
        public HediffDef hediffToRemove;
        public HediffDef hediffToAdd;
        public List<HediffDef> requisiteHediffs;
        public BodyPartDef bodyPart;
        public LetterDef letterDef;
        public string letterLabel;
        public string letterText;
        public Celes_CompProperties_RemoveMechlinkRestriction()
        {
            compClass = typeof(Celes_CompUseEffect_RemoveMechlinkRestriction);
        }
        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef))
                yield return e;
            if (requisiteHediffs.NullOrEmpty())
                yield return "[Celes] requisiteHediffs 为空";
            if (hediffToRemove != null && requisiteHediffs != null
                                       && !requisiteHediffs.Contains(hediffToRemove))
                yield return $"[Celes] requisiteHediffs 必须包含 hediffToRemove "
                             + $"({hediffToRemove.defName})，否则可能删除未验证拥有的 Hediff";
            if (hediffToAdd == null)
                yield return "[Celes] hediffToAdd 未指定，效果将仅删除不添加";
        }
    }
    
    public class Celes_CompUseEffect_RemoveMechlinkRestriction : CompUseEffect
    {
        public Celes_CompProperties_RemoveMechlinkRestriction Props =>
            (Celes_CompProperties_RemoveMechlinkRestriction)props;
        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            if (!Props.requisiteHediffs.NullOrEmpty())
            {
                for (int i = 0; i < Props.requisiteHediffs.Count; i++)
                {
                    if (!p.health.hediffSet.HasHediff(Props.requisiteHediffs[i]))
                    {
                        return "Celes_Keyed_MissingRequiredHediff"
                            .Translate(Props.requisiteHediffs[i].label);
                    }
                }
            }
            if (Props.bodyPart != null)
            {
                BodyPartRecord bodyPartRecord = p.RaceProps.body
                    .GetPartsWithDef(Props.bodyPart).FirstOrFallback();
                if (bodyPartRecord == null)
                {
                    return "Celes_Keyed_MissingBodyPart".Translate(Props.bodyPart.LabelShort);
                }
            }
            return true;
        }
        public override void DoEffect(Pawn user)
        {
            base.DoEffect(user);
            if (Props.hediffToRemove != null)
            {
                Hediff toRemove = user.health.hediffSet
                    .GetFirstHediffOfDef(Props.hediffToRemove);
                if (toRemove != null)
                {
                    user.health.RemoveHediff(toRemove);
                }
            }
            if (Props.hediffToAdd != null)
            {
                BodyPartRecord bodyPartRecord = user.RaceProps.body
                    .GetPartsWithDef(Props.bodyPart).FirstOrFallback();
                if (bodyPartRecord != null)
                {
                    user.health.AddHediff(Props.hediffToAdd, bodyPartRecord);
                    if (user.Spawned && Props.letterDef != null)
                    {
                        Find.LetterStack.ReceiveLetter(
                            Props.letterLabel.Formatted(user.Named("PAWN")),
                            Props.letterText.Formatted(user.Named("PAWN")),
                            Props.letterDef,
                            user
                        );
                    }
                }
            }
        }
    }
}