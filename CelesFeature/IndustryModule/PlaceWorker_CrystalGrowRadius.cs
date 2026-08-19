using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // 放置 ghost：显示 growRadius 生效范围圈（类原版太阳灯）
    // [事实] 渲染入口先例：PlaceWorker_ShowThreadConnection 在 AllowsPlacing 内画圈（PlaceWorker_ShowThreadConnection.cs:12-41）
    // [事实] GenDraw.DrawRadiusRing(IntVec3, float) 白色圈（模块带宽系统同款，1.6 反编译 GenDraw 实证）
    public class PlaceWorker_CrystalGrowRadius : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot,
            Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            if (checkingDef is ThingDef thingDef)
            {
                // [事实] PlaceWorker 无 Comp 实例，半径从 def 静态读取（PlaceWorker_ShowThreadConnection 同款 def.GetCompProperties）
                Celes_CompProperties_CrystalGrowth props = thingDef.GetCompProperties<Celes_CompProperties_CrystalGrowth>();
                if (props != null)
                    GenDraw.DrawRadiusRing(loc, props.growRadius);
            }
            return true;
        }
    }
}
