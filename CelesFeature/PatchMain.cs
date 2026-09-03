using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace CelesFeature
{
    // FD-G01 修复（2026-09-02）：PatchAll → 逐类 Patch + 失败隔离——
    // 原 PatchAll 异常传播：任一注解类解析失败（目标方法变更 / TargetMethod null）→ 中断后续，
    // 已应用的保留、未应用的全失效且无从定位；现单类失败仅红字该条，其余照常（CreateClassProcessor 即
    // PatchAll 内部同款处理器，Harmony 2.x 公开 API）
    [StaticConstructorOnStartup]
    public static class PatchMain
    {
        static PatchMain()
        {
            Harmony harmony = new Harmony("CelesFeature");
            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (!type.IsDefined(typeof(HarmonyPatch), inherit: false)) continue;
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception e)
                {
                    Log.Error($"[CelesFeature] Harmony patch failed on {type.FullName}: {e}");
                }
            }
        }
    }
}
