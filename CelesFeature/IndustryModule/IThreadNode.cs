using UnityEngine;

namespace CelesFeature
{
    public interface IThreadNode
    {
        int TotalCapacity { get; }
        int CurrentLoad { get; }
        bool IsOverloaded { get; }
        Color PipColor { get; }
        string GizmoLabel { get; }
    }
}
