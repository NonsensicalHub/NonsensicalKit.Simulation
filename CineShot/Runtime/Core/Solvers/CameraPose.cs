using UnityEngine;

namespace CineShot.Core
{
    public struct CameraPose
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public float Fov;
        public float NearClip;
        public float FarClip;
        public float FocusDistance;
        public float Dutch;

        public static CameraPose Lerp(CameraPose a, CameraPose b, float t)
        {
            var pose = b;
            pose.Position = Vector3.Lerp(a.Position, b.Position, t);
            pose.Rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t);
            pose.Fov = Mathf.Lerp(a.Fov, b.Fov, t);
            pose.Dutch = Mathf.Lerp(a.Dutch, b.Dutch, t);
            pose.NearClip = Mathf.Lerp(a.NearClip, b.NearClip, t);
            pose.FarClip = Mathf.Lerp(a.FarClip, b.FarClip, t);
            pose.FocusDistance = Mathf.Lerp(a.FocusDistance, b.FocusDistance, t);
            return pose;
        }

        public static readonly CameraPose Identity = new CameraPose
        {
            Rotation = Quaternion.identity,
            Fov = 40f,
            NearClip = 0.1f,
            FarClip = 1000f,
            FocusDistance = 10f
        };
    }

    public struct TargetSample
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public Quaternion Rotation;
        public bool Valid;
    }

    public struct ShotContext
    {
        public float LocalTime;
        public float Duration;
        public TargetSample LookAt;
        public TargetSample Follow;
        public Vector3 CompositionOffset;
        public int Seed;
        public CineSpline Spline;
        public CineSpline CameraSpline;
        public EasingAsset Speed;
    }

    public delegate void SolveDelegate(in ShotContext ctx, in RigParams p, ref CameraPose pose);
}
