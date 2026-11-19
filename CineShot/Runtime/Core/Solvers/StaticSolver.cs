using UnityEngine;

namespace CineShot.Core
{
    public static class StaticSolver
    {
        public static void Solve(in ShotContext ctx, in RigParams p, ref CameraPose pose)
        {
            Vector3 anchor = SolverMath.Anchor(in ctx);
            Quaternion anchorRot = ctx.Follow.Valid ? ctx.Follow.Rotation : Quaternion.identity;
            pose.Position = anchor + anchorRot * p.A;
            SolverMath.AimWithComposition(in ctx, ref pose, p.P0, p.P1);
        }
    }

    public static class DollyTrackSolver
    {
        public static void Solve(in ShotContext ctx, in RigParams p, ref CameraPose pose)
        {
            float u = SolverMath.NormalizedTime(in ctx);
            float eased = u * u * (3f - 2f * u);
            pose.Position = PointOnTrack(in p, eased);
            TargetSample look = ctx.LookAt.Valid ? ctx.LookAt : ctx.Follow;
            if (SolverMath.ShouldPredict(in look, p.P0))
                look.Position = SolverMath.Predict(in look, p.P0);
            var lookCtx = ctx;
            lookCtx.LookAt = look;
            lookCtx.Follow = default(TargetSample);
            SolverMath.AimWithComposition(in lookCtx, ref pose, 0f, 0f);
            pose.Dutch = p.P3;
        }

        public static void TrackFrame(Vector3 a, Vector3 b, out Vector3 along, out Vector3 side, out Vector3 up)
        {
            Vector3 delta = b - a;
            along = delta.sqrMagnitude > 1e-6f ? delta.normalized : Vector3.forward;
            side = Vector3.Cross(Vector3.up, along);
            if (side.sqrMagnitude < 1e-6f)
                side = Vector3.Cross(Vector3.forward, along);
            side.Normalize();
            up = Vector3.Cross(along, side);
            if (up.sqrMagnitude < 1e-6f)
                up = Vector3.up;
            else
                up.Normalize();
        }

        public static Vector3 PointOnTrack(in RigParams p, float eased)
        {
            Vector3 along, side, up;
            TrackFrame(p.A, p.B, out along, out side, out up);
            Vector3 onTrack = Vector3.Lerp(p.A, p.B, Mathf.Clamp01(eased));
            return onTrack + side * p.P1 + up * p.P2;
        }
    }

    public static class OrbitSolver
    {
        public static void Solve(in ShotContext ctx, in RigParams p, ref CameraPose pose)
        {
            Vector3 c = ctx.Follow.Valid ? ctx.Follow.Position : p.B;
            float ang = (p.P0 + p.P1 * ctx.LocalTime) * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
            pose.Position = c + dir * p.P2 + Vector3.up * p.P3;
            SolverMath.AimWithComposition(in ctx, ref pose, 0f, 0f);
            float u = SolverMath.NormalizedTime(in ctx);
            pose.Fov = Mathf.Lerp(pose.Fov, p.A.x > 1f ? p.A.x : pose.Fov, u * 0.5f);
        }
    }

    public static class PushPullSolver
    {
        public static void Solve(in ShotContext ctx, in RigParams p, ref CameraPose pose)
        {
            Vector3 anchor = SolverMath.Anchor(in ctx);
            float u = SolverMath.NormalizedTime(in ctx);
            float dist = Mathf.Lerp(p.P0, p.P1, u * u * (3f - 2f * u));
            Vector3 back = ctx.Follow.Valid
                ? -(ctx.Follow.Rotation * Vector3.forward)
                : Vector3.back;
            if (back.sqrMagnitude < 1e-6f)
                back = Vector3.back;
            pose.Position = anchor + back.normalized * dist + Vector3.up * p.P2;
            SolverMath.AimWithComposition(in ctx, ref pose, 0f, 0f);
            if (p.P3 > 0.5f && p.A.x > 1f)
            {
                float startFov = pose.Fov;
                float endFov = p.A.x;
                pose.Fov = Mathf.Lerp(startFov, endFov, u);
            }
        }
    }
}
