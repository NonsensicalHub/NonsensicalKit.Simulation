using UnityEngine;

namespace CineShot.Core
{
    public static class SolverMath
    {
        public static float FrameRateIndependentAlpha(float dt, float tau)
        {
            if (tau <= 1e-5f)
                return 1f;
            return 1f - Mathf.Exp(-dt / tau);
        }

        public static Vector3 Anchor(in ShotContext ctx)
        {
            if (ctx.Follow.Valid)
                return ctx.Follow.Position;
            if (ctx.LookAt.Valid)
                return ctx.LookAt.Position;
            return Vector3.zero;
        }

        public static Vector3 AimPoint(in ShotContext ctx, in Vector3 fallbackFrom)
        {
            if (ctx.LookAt.Valid)
                return ctx.LookAt.Position;
            if (ctx.Follow.Valid)
                return ctx.Follow.Position;
            return fallbackFrom + Vector3.forward;
        }

        public static void LookAtTarget(in ShotContext ctx, ref CameraPose pose, float yawBiasDeg, float pitchBiasDeg)
        {
            Vector3 target = AimPoint(in ctx, pose.Position);
            Vector3 fwd = target - pose.Position;
            if (fwd.sqrMagnitude > 1e-6f)
            {
                Quaternion look = Quaternion.LookRotation(fwd.normalized, Vector3.up);
                pose.Rotation = look * Quaternion.Euler(pitchBiasDeg, yawBiasDeg, 0f);
            }
            else if (pose.Rotation == default(Quaternion))
            {
                pose.Rotation = Quaternion.identity;
            }
        }

        public static void ApplyComposition(ref CameraPose pose, in Vector3 compositionOffset)
        {
            pose.Position += pose.Rotation * compositionOffset;
        }

        /// <summary>构图偏移后重注视，避免机位挪了却不再看目标。</summary>
        public static void AimWithComposition(in ShotContext ctx, ref CameraPose pose, float yawBiasDeg, float pitchBiasDeg)
        {
            LookAtTarget(in ctx, ref pose, yawBiasDeg, pitchBiasDeg);
            if (ctx.CompositionOffset.sqrMagnitude <= 1e-10f)
                return;
            ApplyComposition(ref pose, ctx.CompositionOffset);
            LookAtTarget(in ctx, ref pose, yawBiasDeg, pitchBiasDeg);
        }

        public static void ClampOptics(ref CameraPose pose)
        {
            pose.Fov = Mathf.Clamp(pose.Fov, 1f, 179f);
            pose.NearClip = Mathf.Clamp(pose.NearClip, 0.01f, 1f);
            pose.FarClip = Mathf.Max(pose.FarClip, pose.NearClip + 0.1f);
            pose.FocusDistance = Mathf.Max(0.1f, pose.FocusDistance);
        }

        public static void KeepMinDistance(ref CameraPose pose, in TargetSample target, float minDistance)
        {
            if (!target.Valid || minDistance <= 0f)
                return;
            Vector3 d = pose.Position - target.Position;
            float sqr = d.sqrMagnitude;
            float minSqr = minDistance * minDistance;
            if (sqr < minSqr && sqr > 1e-8f)
                pose.Position = target.Position + d.normalized * minDistance;
        }

        public static float NormalizedTime(in ShotContext ctx)
        {
            return ctx.Duration > 1e-5f ? Mathf.Clamp01(ctx.LocalTime / ctx.Duration) : 0f;
        }

        public static bool ShouldPredict(in TargetSample sample, float lookAhead)
        {
            // 静止/微抖不前馈，避免与真实注视点「抢」瞄准
            return lookAhead > 0.01f && sample.Valid && sample.Velocity.sqrMagnitude > 0.25f;
        }

        public static Vector3 Predict(in TargetSample sample, float lookAhead)
        {
            if (!ShouldPredict(in sample, lookAhead))
                return sample.Valid ? sample.Position : Vector3.zero;
            return sample.Position + sample.Velocity * lookAhead;
        }
    }
}
