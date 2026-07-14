using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace CelesFeature
{
    public class CompProperties_DraftSwitch : CompProperties
    {
        /// <summary>
        /// 这个comp只用写一个，推荐写在服装套装的核心服装上
        /// </summary>
        public CompProperties_DraftSwitch()
        {
            this.compClass = typeof(CompDraftSwitch);
        }

        public ThingDef switchApperal;
        public ThingDef defaultStuff;
        public List<ApparelLayerDef> removeLayers;
    }
}
