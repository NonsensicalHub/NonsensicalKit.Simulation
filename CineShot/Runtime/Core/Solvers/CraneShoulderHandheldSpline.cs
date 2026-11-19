using UnityEngine;

namespace CineShot.Core
{
    public static class CraneSolver
    {
        public static void Solve(in ShotContext ctx, in RigParams p, ref CameraPose pose)
        {
            float u = SolverMath.NormalizedTime(in ctx);
            Vector3 origin = ctx.Follow.Valid ? ctx.Follow.Position : Vector3.zero;
            pose.Position = Point(in p, u, origin);
            if (ctx.LookAt.Valid || ctx.Follow.Valid)
                SolverMath.AimWithComposition(in ctx, ref pose, 0f, 0f);
            else
            {
                SolverMath.ApplyComposition(ref pose, ctx.CompositionOffset);
                Vector3 ahead = Point(in p, Mathf.Clamp01(u + 0.04f), origin);
                Vector3 aim = ahead - pose.Position;
                if (aim.sqrMagnitude < 1e-8f)
                    aim = p.B - p.A;
                if (aim.sqrMagnitude > 1e-8f)
                    pose.Rotation = Quaternion.LookRotation(aim.normalized, Vector3.up);
            }
        }

        public static Vector3 Bulge(in RigParams p)
        {
            return new Vector3(p.P0, p.P1, p.P2);
        }

        public static Vector3 Point(in RigParams p, float u, Vector3 origin)
        {
            float eased = u * u * (3f - 2f * u);
            eased = Mathf.Clamp01(eased);
            return origin + Vector3.Lerp(p.A, p.B, eased) + Bulge(in p) * Mathf.Sin(eased * Mathf.PI);
        }
    }

    public static class ShoulderSolver
    {
        public static void Solve(in ShotContext ctx, in RigParams p, ref CameraPose pose)
        {
            Vector3 followPos = ctx.Follow.Valid ? ctx.Follow.Position : SolverMath.Anchor(in ctx);
            Quaternion followRot = ctx.Follow.Valid ? ctx.Follow.Rotation : Quaternion.identity;
            float side = p.I0 >= 0 ? 1f : -1f;
            Vector3 shoulder = followPos
                               + followRot * (Vector3.right * (side * p.P0))
                               + Vector3.up * p.P1
                               + followRot * (Vector3.back * 0.35f);
            pose.Position = shoulder;
            pose.Dutch = p.P3;
            pose.NearClip = Mathf.Min(pose.NearClip, 0.05f);
            SolverMath.AimWithComposition(in ctx, ref pose, p.P2, 0f);
        }
    }

    public static class HandheldSolver
    {
        public static void Solve(in ShotContext ctx, in RigParams p, ref CameraPose pose)
        {
            // 不用 StaticSolver：其 P0/P1 是偏航/俯仰，手持里是振幅/频率
            Vector3 anchor = SolverMath.Anchor(in ctx);
            Quaternion anchorRot = ctx.Follow.Valid ? ctx.Follow.Rotation : Quaternion.identity;
            pose.Position = anchor + anchorRot * p.A;
            float nx, ny, nz;
            ApplyShake(in ctx, in p, ref pose, out nx, out ny, out nz);
            float tip = p.P2 * 0.35f;
            SolverMath.AimWithComposition(in ctx, ref pose, nx * tip, ny * tip);
            pose.Dutch = nx * p.P3;
        }

        /// <summary>手持层：叠在已解好的机位上，抖完重新盯住目标，滚转叠加到原有 Dutch。</summary>
        public static void ApplyLayer(in ShotContext ctx, in RigParams p, ref CameraPose pose)
        {
            float nx, ny, nz;
            ApplyShake(in ctx, in p, ref pose, out nx, out ny, out nz);
            float tip = p.P2 * 0.35f;
            SolverMath.LookAtTarget(in ctx, ref pose, nx * tip, ny * tip);
            pose.Dutch += nx * p.P3;
        }

        public static void ApplyShake(in ShotContext ctx, in RigParams p, ref CameraPose pose, out float nx, out float ny, out float nz)
        {
            nx = ny = nz = 0f;
            float amp = Mathf.Max(0f, p.P0);
            if (amp <= 1e-6f && Mathf.Abs(p.P2) <= 1e-6f && Mathf.Abs(p.P3) <= 1e-6f)
                return;

            // P1=频率(Hz 量级)；正弦主波保证持续在动，Perlin 只加点不规则
            float hz = Mathf.Max(0.05f, p.P1);
            float seed = (ctx.Seed & 1023) * 0.173f;
            float t = ctx.LocalTime;
            nx = Wave(t, hz, seed, 1.00f, 2.37f, 5.11f);
            ny = Wave(t, hz, seed + 1.9f, 1.13f, 2.71f, 4.67f);
            nz = Wave(t, hz, seed + 3.7f, 0.89f, 2.09f, 6.13f);
            if (amp <= 1e-6f)
                return;

            Vector3 aim = SolverMath.AimPoint(in ctx, pose.Position) - pose.Position;
            Quaternion basis = aim.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(aim.normalized, Vector3.up)
                : (pose.Rotation.Equals(default(Quaternion)) ? Quaternion.identity : pose.Rotation);
            pose.Position += basis * new Vector3(nx, ny, nz * 0.4f) * amp;
        }

        static float Wave(float time, float hz, float phase, float a, float b, float c)
        {
            float w = hz * (Mathf.PI * 2f);
            float n = Mathf.Sin(time * w * a + phase) * 0.55f
                      + Mathf.Sin(time * w * b + phase * 1.7f) * 0.30f
                      + Mathf.Sin(time * w * c + phase * 0.4f) * 0.15f;
            float grit = (Mathf.PerlinNoise(time * hz * 0.35f + phase, phase * 0.21f) - 0.5f) * 0.35f;
            return Mathf.Clamp(n + grit, -1.25f, 1.25f);
        }
    }

    public static class SplineDollySolver
    {
        public static void Solve(in ShotContext ctx, in RigParams p, ref CameraPose pose)
        {
            float u = SolverMath.NormalizedTime(in ctx);
            if (ctx.Spline != null)
                pose.Position = ctx.Spline.Evaluate(u);
            else
                pose.Position = Vector3.Lerp(p.A, p.B, u) + Vector3.up * p.P3;

            if (ctx.LookAt.Valid || ctx.Follow.Valid)
            {
                // 只认注视；跟随仅作无注视时的回退，避免同绑一物时偏移互相抢瞄
                TargetSample look = ctx.LookAt.Valid ? ctx.LookAt : ctx.Follow;
                if (SolverMath.ShouldPredict(in look, p.P2))
                    look.Position = SolverMath.Predict(in look, p.P2);
                var lookCtx = ctx;
                lookCtx.LookAt = look;
                lookCtx.Follow = default(TargetSample);
                SolverMath.AimWithComposition(in lookCtx, ref pose, 0f, 0f);
            }
            else
            {
                SolverMath.ApplyComposition(ref pose, ctx.CompositionOffset);
                float aheadU = Mathf.Clamp01(u + 0.02f);
                Vector3 ahead = ctx.Spline != null
                    ? ctx.Spline.Evaluate(aheadU)
                    : Vector3.Lerp(p.A, p.B, aheadU) + Vector3.up * p.P3;
                Vector3 aim = ahead - pose.Position;
                if (aim.sqrMagnitude > 1e-8f)
                    pose.Rotation = Quaternion.LookRotation(aim.normalized, Vector3.up);
            }
        }
    }
}
