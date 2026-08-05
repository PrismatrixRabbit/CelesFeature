using Verse;

namespace CelesFeature
{
    public class CelesFD_DialogueEntry : IExposable
    {
        public bool IsPlayer;
        public string Text;

        public void ExposeData()
        {
            Scribe_Values.Look(ref IsPlayer, "isPlayer", false);
            Scribe_Values.Look(ref Text, "text", "");
        }
    }
}