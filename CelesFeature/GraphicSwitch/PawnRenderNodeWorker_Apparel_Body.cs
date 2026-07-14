using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace CelesFeature
{
    public class PawnRenderNodeWorker_Apparel_Body : Verse.PawnRenderNodeWorker_Apparel_Body
    {
        private static bool _CachedDraftState = false;
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            if (parms.pawn.Drafted != _CachedDraftState)
            {
                _CachedDraftState = parms.pawn.Drafted;
                node.requestRecache = true;
            }
            return true;
        }
    }
}
