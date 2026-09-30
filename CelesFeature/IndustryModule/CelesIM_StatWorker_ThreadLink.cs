using System.Linq;
using RimWorld;
using Verse;

namespace CelesFeature
{
    // ════════════════════════════════════════════════════════════════
    //  我方 stat 显示门禁与定制 worker 族（值义反转+蓝图转发修订 2026-09-27）
    //  · ThreadConsumerOnly：ShouldShowFor=thing 挂 Consumer 才显示（蓝图场景判目标 def 挂
    //    CompProperties_ThreadConsumer——蓝图 InfoCard 同样显示我方规格）；
    //    GetBaseValueFor 蓝图转发（蓝图 def 运行时生成 statBases 空——值从 EntityToBuild 取）
    //  · ThreadDependency：值义 1=依赖（t）/0=可离网（f）——label 直读是/否（零反转）；
    //    蓝图场景从 optionalReq 目标 def 取值
    //  · UnconnectedEfficiency：可离网型（0）才显示本行（依赖型无法工作，效率无意义）
    // ════════════════════════════════════════════════════════════════
    public class CelesIM_StatWorker_ThreadConsumerOnly : StatWorker
    {
        public static CelesIM_CompThreadConsumer ConsumerOf(StatRequest req)
        {
            if (!req.HasThing)
                return null;
            return req.Thing.TryGetComp<CelesIM_CompThreadConsumer>();
        }

        // 蓝图场景的目标 def（EntityToBuild）；非蓝图返回 null
        public static ThingDef BlueprintTargetDef(StatRequest req)
        {
            if (req.Thing is Blueprint bp && bp.EntityToBuild() is ThingDef td)
                return td;
            return null;
        }

        public override bool ShouldShowFor(StatRequest req)
        {
            if (stat.alwaysHide || !stat.CanShowWithLoadedMods())
                return false;
            ThingDef targetDef = BlueprintTargetDef(req);
            if (targetDef != null)   // 蓝图：判目标 def 挂 Consumer（def 级 comps）
                return targetDef.comps != null
                    && targetDef.comps.Any(c => c.compClass == typeof(CelesIM_CompThreadConsumer));
            return ConsumerOf(req) != null;
        }

        public override float GetBaseValueFor(StatRequest request)
        {
            // 蓝图转发：蓝图 def 运行时生成 statBases 基本为空——静态值从目标 def 的 statBases 取
            ThingDef targetDef = BlueprintTargetDef(request);
            if (targetDef != null)
            {
                if (targetDef.statBases != null)
                {
                    for (int i = 0; i < targetDef.statBases.Count; i++)
                    {
                        if (targetDef.statBases[i].stat == stat)
                            return targetDef.statBases[i].value;
                    }
                }
                return stat.defaultBaseValue;
            }
            return base.GetBaseValueFor(request);
        }
    }

    public class CelesIM_StatWorker_ThreadDependency : CelesIM_StatWorker_ThreadConsumerOnly
    {
        public override string GetStatDrawEntryLabel(StatDef stat, float value, ToStringNumberSense numberSense, StatRequest optionalReq, bool finalized = true)
        {
            // 蓝图场景传入 value 来自蓝图 def（default）——从目标 def 重取；1=依赖→「是」直读
            float v = value;
            ThingDef targetDef = BlueprintTargetDef(optionalReq);
            if (targetDef != null)
                v = stat.Worker.GetValueAbstract(targetDef);
            return v >= 0.5f
                ? "CelesIM_Keyed_DepYes".Translate()
                : "CelesIM_Keyed_DepNo".Translate();
        }
    }

    public class CelesIM_StatWorker_UnconnectedEfficiency : CelesIM_StatWorker_ThreadConsumerOnly
    {
        public override bool ShouldShowFor(StatRequest req)
        {
            if (!base.ShouldShowFor(req))
                return false;
            // 可离网型（0=f）才显示——依赖型未连接无法工作，效率行无意义；蓝图从目标 def 取判定
            ThingDef targetDef = BlueprintTargetDef(req);
            if (targetDef != null)
                return targetDef.GetStatValueAbstract(CelesIM_DefOf.Celes_ThreadDependency) < 0.5f;
            return req.Thing.GetStatValue(CelesIM_DefOf.Celes_ThreadDependency) < 0.5f;
        }
    }
}
