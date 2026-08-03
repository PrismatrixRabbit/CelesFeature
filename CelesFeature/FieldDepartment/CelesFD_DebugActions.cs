using System.Collections.Generic;
using LudeonTK;
using Verse;

namespace CelesFeature
{
    public static class CelesFD_DebugActions
    {
        [DebugAction("CelesFD", "Modify global values")]
        private static void ModifyGlobalValues()
        {
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            if (gc == null) return;

            List<DebugMenuOption> options = new List<DebugMenuOption>();
            AddIntOptions(options, "Fame", gc.Fame, v => gc.Fame = v);
            AddIntOptions(options, "Credit", gc.Credit, v => gc.Credit = v);
            AddIntOptions(options, "QuantumKey", gc.QuantumKey, v => gc.QuantumKey = v);
            AddIntOptions(options, "UnlockLevelValue", gc.UnlockLevelValue, v => gc.UnlockLevelValue = v);
            AddIntOptions(options, "EffectiveLevelValue", gc.EffectiveLevelValue, v => gc.EffectiveLevelValue = v);
            AddIntOptions(options, "TradeVolume", gc.TradeVolume, v => gc.TradeVolume = v);
            AddIntOptions(options, "KeyToCredit", gc.KeyToCredit, v => gc.KeyToCredit = v);

            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        private static void AddIntOptions(List<DebugMenuOption> options, string label, int current, System.Action<int> setter)
        {
            options.Add(new DebugMenuOption(label + " +100 (" + current + ")", DebugMenuOptionMode.Action,
                delegate { setter(current + 100); Log.Message(label + " -> " + (current + 100)); }));
            options.Add(new DebugMenuOption(label + " -100 (" + current + ")", DebugMenuOptionMode.Action,
                delegate { setter(current - 100); Log.Message(label + " -> " + (current - 100)); }));
            options.Add(new DebugMenuOption(label + " = 0", DebugMenuOptionMode.Action,
                delegate { setter(0); Log.Message(label + " -> 0"); }));
        }
    }
}