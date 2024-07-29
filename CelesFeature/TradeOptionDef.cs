using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace CelesFeature
{
    public class TradeOptionDef : Def
    {
        public TradeType tradeType = TradeType.Small;
        public IntRange goodwillRequest;
        public ThingDef dropPod;
        public ThingDef faller;
        public List<ThingDefCountRangeClass> requests = new List<ThingDefCountRangeClass>();
        public List<ThingDefCountRangeClass> rewards = new List<ThingDefCountRangeClass>();
    }

    public enum TradeType
    {
        GoodWill,
        Small,
        Big
    }
}
