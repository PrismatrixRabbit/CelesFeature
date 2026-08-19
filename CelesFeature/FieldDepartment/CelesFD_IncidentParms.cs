using RimWorld;

namespace CelesFeature
{
    // 信标站系统 Incident 参数扩展（对话动态状态传递）：
    // 原版 IncidentParms 无对话选择字段，子类传递对话 UI 的选中站索引与对话配置的成本值
    public class CelesFD_IncidentParms : IncidentParms
    {
        public int beaconTarget = -1;   // 对话中选中的信标站索引（BeaconAbandon 用；-1 = 未指定）
        public int creditCost;          // 申请成本（对话 comp props 传入，BeaconRequest 用）
    }
}
