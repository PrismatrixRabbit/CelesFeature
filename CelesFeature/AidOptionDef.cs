using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace CelesFeature
{
    public class AidOptionDef : Def
    {
        public bool isDrop;
        public int cooldown = 100;
        public List<ThingDefCountClass> requests = new List<ThingDefCountClass>();
        public List<AidEffect> effects = new List<AidEffect>();
    }

    public abstract class AidEffect 
    {
        public abstract void Work(Map map);
    }
    public class AidEffect_Drop : AidEffect
    {
        public override void Work(Map map)
        {
            IntVec3 c = DropCellFinder.TradeDropSpot(map);
            ActiveDropPod activeDropPod = (ActiveDropPod)ThingMaker.MakeThing(this.dropPod);
            activeDropPod.Contents = new ActiveDropPodInfo() 
            {
                leaveSlag = false
            };
            this.things.RandomElement().ForEach(t =>
            {
                Thing thing = ThingMaker.MakeThing(t.thingDef);
                thing.stackCount = t.countRange.RandomInRange;
                activeDropPod.Contents.innerContainer.TryAdd(thing);
            });
            SkyfallerMaker.SpawnSkyfaller(faller, activeDropPod, c, map);
        }

        public List<List<ThingDefCountRangeClass>> things = new List<List<ThingDefCountRangeClass>>();
        public ThingDef dropPod;
        public ThingDef faller;
    }
    public class AidEffect_Quest : AidEffect
    {
        public override void Work(Map map)
        {
            Slate slate = new Slate();
            slate.Set<Map>("map", map, false);
            slate.Set<int>("laborersCount", this.aidParameter.pawnCount, false);
            slate.Set<Faction>("permitFaction",Find.FactionManager.FirstFactionOfDef(this.faction), false);
            slate.Set<PawnKindDef>("laborersPawnKind", this.aidParameter.pawnKindDef, false);
            slate.Set<float>("laborersDurationDays", this.aidParameter.aidDurationDays, false);
            slate.Set<IntVec3>("landingCell", DropCellFinder.GetBestShuttleLandingSpot(map, Find.FactionManager.FirstFactionOfDef(this.faction)), false);
            QuestUtility.GenerateQuestAndMakeAvailable(this.def, slate);
        }

        public RoyalAid aidParameter;
        public FactionDef faction;
        public QuestScriptDef def;
    }

}
