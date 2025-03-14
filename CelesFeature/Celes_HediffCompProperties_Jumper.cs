using System.Collections.Generic;
using Verse;

namespace CelesFeature
{
        public class Celes_HediffCompProperties_Jumper : HediffCompProperties
        {
            public HediffDef targetHediff;
            public float chanceToJump=1f;
            public bool minToJump=false;
            public bool useRace=false;
            public List<ThingDef> specialRace;
            public HediffDef hediffForSpRace;
            public Celes_HediffCompProperties_Jumper()
            {
                compClass = typeof(Celes_HediffComp_Jumper);
            }
        }
}