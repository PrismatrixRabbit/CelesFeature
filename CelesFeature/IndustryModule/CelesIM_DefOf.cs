using RimWorld;
using Verse;

namespace CelesFeature
{
    // CelesIM Def 引用集中（对齐 CelesFD_DefOf 惯例：加载期校验 + 单一引用源）
    [DefOf]
    public static class CelesIM_DefOf
    {
        public static JobDef CelesIM_FixHubFault;

        // 批 4：尘构机效率 stat（单一真相源——GetWorkSpeedFactor 经 GetStatValue 读此 stat 的 parts 乘法链）
        public static StatDef Celes_ThreadLinkEfficiency;

        // 批 4 终版：未连接效率乘数（原 MinEfficiencyFactor 改名接管——offlineFactor 的 stat 化；
        // default 0.1=依赖型预热起点，可离网型 statBases 配值）
        public static StatDef Celes_UnconnectedEfficiency;

        // 批 4 终版：依赖线程网络（canWorkOffline 升格——1=可离网 / 0=依赖未连接不可工作；bill 拦截 patch 消费）
        public static StatDef Celes_ThreadDependency;
    }
}
