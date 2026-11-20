using UnityEngine;

namespace CineShot.Output
{
    public struct LensDTO
    {
        public float FieldOfView;
        public float NearClipPlane;
        public float FarClipPlane;
        public float Dutch;
    }

    /// <summary>屏蔽 CM2 / CM3 虚拟相机 API 差异。</summary>
    public interface ILensWriter
    {
        bool IsValid { get; }
        int Priority { get; set; }
        void SetPose(Vector3 position, Quaternion rotation);
        void SetLens(in LensDTO lens);
        void SetEnabled(bool enabled);
    }
}
