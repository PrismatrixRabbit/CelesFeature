using System.Collections.Generic;
using RimWorld;
using Verse;

namespace CelesFeature
{
    // W-5：即刻投放（合并旧 Effect_CrateDrop/Effect_CrateStrike——两者逐行同构，仅 fallerDef 来源不同）
    // 内容物由 fallerDef 的 CelesFD_OrbitDropFallerExtension 解析（Skyfaller_OrbitCrate.Impact 只读扩展，
    //   效果字段从不参与内容解析——旧 CrateDrop.crateDef 为冗余间接层，W-5 消除）：
    //   fallerDef 有 crateDef 扩展 → 容器/建筑链；无 → 纯效果舱链
    public class CelesFD_Effect_Drop : CelesFD_SupportEffect
    {
        public ThingDef fallerDef;

        public override void Trigger(CelesFD_EffectContext ctx)
        {
            if (fallerDef == null)
            {
                Log.Warning("[CelesFD] Effect_Drop missing fallerDef (support " +
                    (ctx.SupportDef != null ? ctx.SupportDef.defName : "null") + ")");
                return;
            }
            foreach (IntVec3 cell in ctx.Cells)
            {
                // 内胆必须经 ThingMaker 生成（IncidentWorker_OrbitDrop.SpawnBuildingDrop 同款——
                //   裸 new ActiveTransporter 缺 def/SpawnSetup，Skyfaller Tick 访问 inner 时 NRE）
                ActiveTransporter info = (ActiveTransporter)ThingMaker.MakeThing(ThingDefOf.ActiveDropPod);
                Skyfaller faller = SkyfallerMaker.SpawnSkyfaller(fallerDef, info, cell, ctx.Map);
                ctx.RegisterController(faller);
            }
        }
    }
}
