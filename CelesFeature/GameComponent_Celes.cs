using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace CelesFeature
{
    //用于储存支援系统的冷却时间
    public class GameComponent_Celes : GameComponent
    {
        public GameComponent_Celes(Game game) { }
        public List<TradeOption> TradeOptions 
        {
            get 
            {
                if (!this.tradeOptions.Any() || Find.TickManager.TicksGame - this.lastUpdateTradeTime > 7 * 60000) 
                {
                    this.tradeOptions.Clear();
                    List<TradeOptionDef> goodwill = new List<TradeOptionDef>();
                    List<TradeOptionDef> small = new List<TradeOptionDef>();
                    List<TradeOptionDef> big = new List<TradeOptionDef>();
                    DefDatabase<TradeOptionDef>.AllDefsListForReading.ForEach(d => 
                    {
                        switch (d.tradeType) 
                        {
                            case TradeType.GoodWill:goodwill.Add(d);break;
                            case TradeType.Small: small.Add(d); break;
                            case TradeType.Big: big.Add(d); break;
                        }
                    });
                    Func<TradeOptionDef, TradeOption> getOption = (d) =>
                   {
                       TradeOption result = new TradeOption() {def = d, goodwillRequest = d.goodwillRequest.RandomInRange };
                       if (!d.requests.NullOrEmpty()) 
                       {
                           d.requests.ForEach(r => result.requests.Add(new ThingDefCountClass(r.thingDef,r.countRange.RandomInRange)));
                       }
                       if (!d.rewards.NullOrEmpty())
                       {
                           d.rewards.ForEach(r => result.rewards.Add(new ThingDefCountClass(r.thingDef, r.countRange.RandomInRange)));
                       }
                       return result;
                   };

                    this.tradeOptions.Add(getOption(goodwill.RandomElement()));
                    this.tradeOptions.Add(getOption(small.RandomElement()));
                    this.tradeOptions.Add(getOption(big.RandomElement()));
                    this.lastUpdateTradeTime = Find.TickManager.TicksGame;
                }
                return this.tradeOptions;
            }
        }
        public void AddCooldown(AidOptionDef def) 
        {
            this.cooldown.Add(new AidCooldown() {def = def,cooldown = def.cooldown});
        }
        public bool AidAvailable(AidOptionDef def,out float time) 
        {
            AidCooldown cool = this.cooldown.Find(c => c.def == def);
            time = cool == null ? 0 : cool.cooldown;
            return cool == null;
        }
        public override void GameComponentTick()
        {
            base.GameComponentTick();
            this.cooldown.ForEach(c => c.cooldown -= 1);
            this.cooldown.RemoveAll(c => c.cooldown <= 0);
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref this.lastTradeTime, "lastTradeTime");
            Scribe_Values.Look(ref this.lastUpdateTradeTime, "lastUpdateTradeTime");
            Scribe_Collections.Look(ref this.cooldown, "cooldown",LookMode.Deep);
            Scribe_Collections.Look(ref this.tradeOptions, "tradeOptions", LookMode.Deep);
        }

        public int lastTradeTime;
        public int lastUpdateTradeTime;
        private List<AidCooldown> cooldown = new List<AidCooldown>();
        private List<TradeOption> tradeOptions = new List<TradeOption>(); 
    }
    //支援选项的冷却
    public class TradeOption : IExposable
    {
        public DiaOption GetOption(Map map, GameComponent_Celes comp,Faction f)
        { 
            string requestString = null;
            if (this.goodwillRequest != 0) 
            {
                requestString = ("GoodwillRequest".Translate(this.goodwillRequest));
            }
            this.requests.ForEach(q =>
            {
                if (requestString != null)
                {
                    requestString += " " + q.Label;
                }
                else 
                {
                    requestString = q.Label;
                }
            });
            string rewardString = null;
            this.rewards.ForEach(q =>
            {
                if (rewardString != null)
                {
                    rewardString += " " + q.Label;
                }
                else
                {
                    rewardString = q.Label;
                }
            });
            DiaOption result = new DiaOption(TranslatorFormattedStringExtensions.Translate(this.def.label, requestString, rewardString));
            if ((!this.requests.NullOrEmpty() && !Patch_Faction.CheckRequiredThings(this.requests, map)))
            {
                result.Disable("Celes_LackRequests".Translate());
            }
            if (comp.lastTradeTime != 0 && Find.TickManager.TicksGame - comp.lastTradeTime < 7 * 60000)
            {
                int time = (int)((7 * 60000 - (Find.TickManager.TicksGame - comp.lastTradeTime)));
                result.Disable("Celes_InCooldown".Translate(time.ToStringTicksToDays()));
            }
            result.resolveTree = true;
            result.action = () =>
            {
                if (this.goodwillRequest != 0) 
                {
                    f.TryAffectGoodwillWith(Find.FactionManager.OfPlayer,-this.goodwillRequest);
                }
                if (!this.requests.NullOrEmpty())
                {
                    Patch_Faction.ConsumeRequiredThings(this.requests, map);
                }
                IntVec3 c = DropCellFinder.TradeDropSpot(map);
                ActiveDropPod activeDropPod = (ActiveDropPod)ThingMaker.MakeThing(this.def.dropPod);
                activeDropPod.Contents = new ActiveDropPodInfo()
                {
                    leaveSlag = false
                };
                this.rewards.ForEach(t =>
                {
                    Thing thing = ThingMaker.MakeThing(t.thingDef);
                    thing.stackCount = t.count;
                    activeDropPod.Contents.innerContainer.TryAdd(thing);
                });
                SkyfallerMaker.SpawnSkyfaller(this.def.faller, activeDropPod, c, map);
                comp.lastTradeTime = Find.TickManager.TicksGame;
            };
            return result;
        }
        public void ExposeData()
        {
            Scribe_Defs.Look(ref this.def, "def");
            Scribe_Values.Look(ref this.goodwillRequest, "goodwillRequest");
            Scribe_Collections.Look(ref this.requests, "requests", LookMode.Deep); 
            Scribe_Collections.Look(ref this.rewards, "rewards", LookMode.Deep);
        }

        public int goodwillRequest; 
        public TradeOptionDef def;
        public List<ThingDefCountClass> requests = new List<ThingDefCountClass>();
        public List<ThingDefCountClass> rewards = new List<ThingDefCountClass>();
    }
    public class AidCooldown : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref this.cooldown, "cooldown");
            Scribe_Defs.Look(ref this.def,"def");
        }

        public int cooldown;
        public AidOptionDef def;
    }
}
