using UnityEngine;

namespace CineShot.Core
{
    /// <summary>默认机位停、注视摇；I0≠0 时可选机位曲线。</summary>
    public static class PanSolver
    {
        public static bool Advanced(in RigParams p)
        {
            return p.I0 != 0;
        }

        public static float Progress(in ShotContext ctx)
        {
            float u = SolverMath.NormalizedTime(in ctx);
            if (ctx.Speed != null)
                return Mathf.Clamp01(ctx.Speed.Evaluate(u));
            return u * u * (3f - 2f * u);
        }

        public static bool HasPath(CineSpline spline)
        {
            return spline != null && spline.knots != null && spline.knots.Length > 0;
        }

        public static Vector3 LookPoint(in ShotContext ctx, in RigParams p, float s)
        {
            if (HasPath(ctx.Spline))
                return ctx.Spline.Evaluate(s);
            Vector3 origin = ctx.LookAt.Valid
                ? ctx.LookAt.Position
                : (ctx.Follow.Valid ? ctx.Follow.Position : Vector3.zero);
            float k = Mathf.Clamp01(s);
            return origin + Vector3.Lerp(p.B, p.C, k) + new Vector3(p.P0, p.P1, p.P2) * Mathf.Sin(k * Mathf.PI);
        }

        public static Vector3 CameraPoint(in ShotContext ctx, in RigParams p, float s)
        {
            if (Advanced(in p) && HasPath(ctx.CameraSpline))
                return ctx.CameraSpline.Evaluate(s);
            Vector3 origin = ctx.Follow.Valid ? ctx.Follow.Position : Vector3.zero;
            return origin + p.A;
        }

        public static void Solve(in ShotContext ctx, in RigParams p, ref CameraPose pose)
        {
            float s = Progress(in ctx);
            pose.Position = CameraPoint(in ctx, in p, s);
            Vector3 look = LookPoint(in ctx, in p, s);
            if (ctx.CompositionOffset.sqrMagnitude > 1e-10f)
            {
                Vector3 provisional = look - pose.Position;
                if (provisional.sqrMagnitude > 1e-8f)
                    pose.Rotation = Quaternion.LookRotation(provisional.normalized, Vector3.up);
                SolverMath.ApplyComposition(ref pose, ctx.CompositionOffset);
            }
            Vector3 aim = look - pose.Position;
            if (aim.sqrMagnitude > 1e-8f)
                pose.Rotation = Quaternion.LookRotation(aim.normalized, Vector3.up);
        }
    }
}
