using UnityEngine;

namespace CelesFeature
{
    public interface CelesIM_IThreadNode
    {
        int TotalCapacity { get; }
        int CurrentLoad { get; }
        int NoSignalLoad { get; }   // 批 2：无信号段（绑定保留·占位非活跃）——Gizmo 嵌套格数据源；活跃段 = CurrentLoad − NoSignalLoad
        bool IsOverloaded { get; }
        Color PipColor { get; }
        string GizmoLabel { get; }
    }
}
