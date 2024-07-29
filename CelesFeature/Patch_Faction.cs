using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace CelesFeature
{
    //对生成派系对话的函数进行补丁，来添加支援内容
    [HarmonyPatch(typeof(FactionDialogMaker), "FactionDialogFor")]
    public class Patch_Faction
    {
        [HarmonyPostfix]
        public static void postfix(ref DiaNode __result, Pawn negotiator, Faction faction)
        {
            if (faction.def.GetModExtension<ModExtension_Aid>() is ModExtension_Aid extension && faction.GoodwillWith(negotiator.Faction) is int goodwill && extension.greetWords.ToList().Find(g =>
            {
                return goodwill <= g.Key.TrueMax && goodwill >= g.Key.TrueMin;
            }) is KeyValuePair<IntRange,string> greet)
            {
                DiaNode mainNode = GetMainNode(__result, negotiator, goodwill, greet,faction);
                DiaOption aid = new DiaOption("Celes_Main".Translate()) { link = mainNode };
                aid.Disable(null);
                DiaOption disconnect = __result.options.Last();
                __result.options.Remove(disconnect);
                __result.options.Add(aid);
                __result.options.Add(disconnect);
            }
        }
        private static DiaNode GetMainNode(DiaNode __result, Pawn negotiator, int goodwill, KeyValuePair<IntRange, string> greet,Faction f)
        {
            DiaNode mainNode = new DiaNode(greet.Value.Translate(goodwill, GetSilverCount(negotiator.Map)));  
            GameComponent_Celes comp = Current.Game.GetComponent<GameComponent_Celes>();
            DiaNode aid = new DiaNode("Celes_AidGreet".Translate());
            DiaNode drop = new DiaNode("Celes_DropGreet".Translate());
            DefDatabase<AidOptionDef>.AllDefsListForReading.ForEach(o =>
            {
                DiaOption option = new DiaOption(o.label);
                if ((!o.requests.NullOrEmpty() && !CheckRequiredThings(o.requests, negotiator.Map)))
                {
                    option.Disable("Celes_LackRequests".Translate());
                }
                if (!comp.AidAvailable(o, out float time))
                {
                    option.Disable("Celes_InCooldown".Translate(((int)time).ToStringTicksToDays()));
                }
                option.resolveTree = true;
                option.action = () =>
                {
                    ConsumeRequiredThings(o.requests, negotiator.Map);
                    o.effects.ForEach(e => e.Work(negotiator.Map));
                    comp.AddCooldown(o);
                };
                if (!o.isDrop)
                {
                    aid.options.Add(option);
                }
                else 
                {
                    drop.options.Add(option);
                }
            });
            aid.options.Add(new DiaOption("GoBack".Translate()) { link = mainNode });
            drop.options.Add(new DiaOption("GoBack".Translate()) { link = mainNode });
            mainNode.options.Add(new DiaOption("Celes_Aid".Translate()) { link = aid });
            mainNode.options.Add(new DiaOption("Celes_Drop".Translate()) { link = drop });
            DiaNode tradeNode = new DiaNode("Celes_TradeGreet".Translate());
            mainNode.options.Add(new DiaOption("Celes_Trade".Translate()) {link = tradeNode});
            comp.TradeOptions.ForEach(o => tradeNode.options.Add(o.GetOption(negotiator.Map,comp,f)));
            tradeNode.options.Add(new DiaOption("GoBack".Translate()) { link = mainNode });
            mainNode.options.Add(new DiaOption("GoBack".Translate()) { link = __result });
            return mainNode;
        }

        public static int GetSilverCount(Map map) 
        {
            return map.listerThings.ThingsOfDef(ThingDefOf.Silver).FindAll(s => s.Spawned && s.Position.GetZone(map) is Zone_Stockpile).Sum(s => s.stackCount);
        }
        public static bool CheckRequiredThings(List<ThingDefCountClass> requiredThings,Map map)
        {
            Dictionary<ThingDef, int> counts = new Dictionary<ThingDef, int>();
            requiredThings.ForEach(d => counts.Add(d.thingDef,d.count));
            foreach (Thing t in map.listerThings.GetAllThings(t => t.Spawned && t.Position.GetZone(map) is Zone_Stockpile))
            {
                if (counts.ContainsKey(t.def))
                {
                    counts[t.def] -= t.stackCount;
                }
            }
            return !counts.ToList().Exists(c => c.Value >= 1);
        }
        public static void ConsumeRequiredThings(List<ThingDefCountClass> requiredThings, Map map)
        {
            Dictionary<ThingDef, int> counts = new Dictionary<ThingDef, int>();
            requiredThings.ForEach(d => counts.Add(d.thingDef, d.count));
            foreach (Thing t in map.listerThings.GetAllThings(t => t.Spawned && t.Position.GetZone(map) is Zone_Stockpile))
            {
                if (counts.ContainsKey(t.def) && counts[t.def] >= 1)
                {
                    int count = t.stackCount;
                    t.SplitOff(counts[t.def]).Destroy();
                    counts[t.def] -= t.stackCount;
                }
            }
        }
    }
}
