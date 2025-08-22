using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    public class Alert_Celes_TransformerPowerOff : Alert
    {
        private List<Thing> transformers = new List<Thing>();
        private List<Thing> Transformers
        {
            get
            {
                transformers.Clear();
                foreach (Building_Celes_Transformer item in Celes_Utility.AllPlayerTransformers)
                {
                    if (item.DangerPowerOff())
                    {
                        transformers.Add(item);
                    }
                }

                return transformers;
            }
        }

        public Alert_Celes_TransformerPowerOff()
        {
            this.defaultLabel = "Celes_Alert_TransformerPowerOff".Translate();
            this.defaultPriority = AlertPriority.High;
        }
        protected override Color BGColor
        {
            get
            {
                float num = Pulser.PulseBrightness(0.5f, Pulser.PulseBrightness(0.5f, 0.6f));
                return new Color(num, num, num) * Color.red;
            }
        }
        public override string GetLabel()
        {
            return "Celes_Alert_TransformerPowerOff".Translate();
        }
        public override TaggedString GetExplanation()
        {
            return "Celes_Alert_TransformerPowerOff_Desc".Translate();
        }
        public override AlertReport GetReport()
        {
            return AlertReport.CulpritsAre(Transformers);
        }
    }
}
