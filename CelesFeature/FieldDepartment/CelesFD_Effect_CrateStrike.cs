using RimWorld;
using Verse;

namespace CelesFeature
{
    // W-2b：a-2/a-3 定点打击垃圾箱（共用一类——两者仅 fallerDef 不同，爆炸/燃烧差异全在 faller 的
    //   impact 扩展与建筑的 CompExplosive 配置；评审 R3：Building 条目模式，无 lootTable/容器化）
    // faller 链：本效果 spawn faller → Skyfaller_OrbitCrate.Impact（落地爆[含 pre 燃料]）→ Building 条目生成
    //   CompExplosive 建筑（血量引信 0.5 + explodeOnKilled——被毁/到期终结爆）
    public class CelesFD_Effect_CrateStrike : CelesFD_SupportEffect
    {
        public ThingDef fallerDef;   // 如 CelesFD_Faller_ExplosiveCrate / _IncendiaryCrate

        public override void Trigger(CelesFD_EffectContext ctx)
        {
            if (fallerDef == null)
            {
                Log.Warning("[CelesFD] Effect_CrateStrike missing fallerDef (support " +
                    (ctx.SupportDef != null ? ctx.SupportDef.defName : "null") + ")");
                return;
            }
            // 内胆必须经 ThingMaker 生成（W-2a 同款——裸 new 缺 def/SpawnSetup，NRE）
            ActiveTransporter info = (ActiveTransporter)ThingMaker.MakeThing(ThingDefOf.ActiveDropPod);
            Skyfaller faller = SkyfallerMaker.SpawnSkyfaller(fallerDef, info, ctx.Cell, ctx.Map);
            ctx.RegisterController(faller);
        }
    }
}
