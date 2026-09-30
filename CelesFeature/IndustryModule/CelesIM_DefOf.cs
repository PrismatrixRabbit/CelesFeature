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

        // 批 4 终版：依赖线程网络（canWorkOffline 升格；值义反转修订 2026-09-27：1=依赖未连接不可工作/0=可离网）
        public static StatDef Celes_ThreadDependency;

        // 批 5 前置修订：尘构终端预热时长（单源化 2026-09-27——haveWarmUpCount/warmUpTicks 废弃，
        // 计算与显示唯一源；default 0=无预热）
        public static StatDef Celes_WarmUpDuration;

        // 批 5 视觉修订批：锻造流光 mote（单南向起步——建筑 Graphic_Single 不旋转；转 Multi 时补 east/west/north 同族+per-rotation 字典）
        public static ThingDef Celes_Mote_ForgeGlow_South;
    }
}
