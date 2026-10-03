using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace CelesFeature
{
    // ════════════════════════════════════════════════════════════════
    // 批 6 D：配方锁 ext（2026-10-03）——携 lockedTags 的配方默认隐藏，
    // 终端链接（CanBeActive）增幅器携 unlockRecipeTags ⊇ lockedTags 时显示+可挂单。
    // ════════════════════════════════════════════════════════════════
    public class CelesIM_RecipeLockExt : DefModExtension
    {
        public List<string> lockedTags;
    }

    // ════════════════════════════════════════════════════════════════
    // 批 6 D：配方可见性 RecipeWorker 子类（预核 4 定案=零 Harmony）
    // worktable 域三消费点（ITab_Bills.cs:86 主枚举/:43 粘贴板/
    // PlayerItemAccessibilityUtility.cs:202 可用性判定）全部经
    // RecipeDef.AvailableOnNow(Thing)（Verse\RecipeDef.cs:398）→本 virtual
    //（Verse\RecipeWorker.cs:11-14 基类恒 true）——子类覆写单点全覆盖，AvailableReport 同链顺带。
    // 作用域：仅携 CelesIM_RecipeLockExt 且 lockedTags 非空的配方受影响，其余零扰动。
    // 已挂单行为=原版 research 语义（bill 持续存在不追溯，断链仅回隐藏菜单——报备裁决）。
    // ════════════════════════════════════════════════════════════════
    public class CelesIM_RecipeWorker_Locked : RecipeWorker
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            if (!base.AvailableOnNow(thing, part))
            {
                return false;
            }
            CelesIM_RecipeLockExt lockExt = recipe.modExtensions?.OfType<CelesIM_RecipeLockExt>().FirstOrDefault();
            if (lockExt?.lockedTags == null || lockExt.lockedTags.Count == 0)
            {
                return true;   // 无锁=原版行为
            }
            if (thing == null)
            {
                return false;   // 有锁但无建筑上下文=不可判定，保守隐藏
            }
            CompAffectedByFacilities aff = (thing as ThingWithComps)?.GetComp<CompAffectedByFacilities>();
            if (aff == null)
            {
                return false;
            }
            List<Thing> linked = aff.LinkedFacilitiesListForReading;
            for (int i = 0; i < linked.Count; i++)
            {
                CompFacility fac = linked[i]?.TryGetComp<CompFacility>();
                CelesIM_AmplifierExt amp = linked[i]?.def.GetModExtension<CelesIM_AmplifierExt>();
                if (fac == null || !fac.CanBeActive || amp?.unlockRecipeTags == null)
                {
                    continue;
                }
                bool allCovered = true;
                for (int j = 0; j < lockExt.lockedTags.Count; j++)
                {
                    if (!amp.unlockRecipeTags.Contains(lockExt.lockedTags[j]))
                    {
                        allCovered = false;
                        break;
                    }
                }
                if (allCovered)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
