using System.Collections.Generic;
using System.Text;
using Verse;

namespace CelesFeature
{
    public class CelesFD_GameComponent : GameComponent
    {
        public int Fame;                 // 外勤部声望
        public int Credit;               // 外勤部信用额
        public int QuantumKey;           // 外勤部密钥
        public int UnlockLevelValue;     // 外勤部开放等级值
        public int EffectiveLevelValue;  // 实际生效开放等级值
        public int TradeVolume;          // 外勤部交易额
        public int KeyToCredit;          // 1 密钥 = ? 信用额（汇率，待定稿）
        public const int KeyToCreditDefault = 100; // 占位值
        
        // 新增字段（类成员区）：
        public List<string> DialogueHistory = new List<string>();
        
        public CelesFD_GameComponent(Game game) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref Fame, "CFD_Fame", 0);
            Scribe_Values.Look(ref Credit, "CFD_Credit", 0);
            Scribe_Values.Look(ref QuantumKey, "CFD_QuantumKey", 0);
            Scribe_Values.Look(ref UnlockLevelValue, "CFD_UnlockLevelValue", 0);
            Scribe_Values.Look(ref EffectiveLevelValue, "CFD_EffectiveLevelValue", 0);
            Scribe_Values.Look(ref TradeVolume, "CFD_TradeVolume", 0);
            Scribe_Values.Look(ref KeyToCredit, "CFD_KeyToCredit", KeyToCreditDefault);
            Scribe_Collections.Look(ref DialogueHistory, "CFD_DialogueHistory", LookMode.Value);
            if (DialogueHistory == null) DialogueHistory = new List<string>();
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            KeyToCredit = KeyToCreditDefault;
            LogData("New game initialized");
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            LogData("Game loaded");
        }

        public override void AppendDebugString(StringBuilder sb)
        {
            base.AppendDebugString(sb);
            sb.AppendLine("CelesFD:");
            sb.AppendLine("  Fame=" + Fame);
            sb.AppendLine("  Credit=" + Credit);
            sb.AppendLine("  QuantumKey=" + QuantumKey);
            sb.AppendLine("  UnlockLevelValue=" + UnlockLevelValue);
            sb.AppendLine("  EffectiveLevelValue=" + EffectiveLevelValue);
            sb.AppendLine("  TradeVolume=" + TradeVolume);
            sb.AppendLine("  KeyToCredit=" + KeyToCredit);
        }

        private void LogData(string label)
        {
            Log.Message("[CelesFD] " + label
                + " Fame=" + Fame + " Credit=" + Credit + " Key=" + QuantumKey
                + " Unlock=" + UnlockLevelValue + " Eff=" + EffectiveLevelValue
                + " Trade=" + TradeVolume + " KeyToCredit=" + KeyToCredit);
        }

        public static CelesFD_GameComponent Instance
        {
            get
            {
                if (Current.Game == null) return null;
                return Current.Game.GetComponent<CelesFD_GameComponent>();
            }
        }
        
        public void AddDialogue(string line)
        {
            DialogueHistory.Add(line);
            if (DialogueHistory.Count > 300)
                DialogueHistory.RemoveRange(0, DialogueHistory.Count - 300);
        }
    }
}