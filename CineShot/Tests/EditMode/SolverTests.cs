using CineShot.Core;
using NUnit.Framework;
using UnityEngine;

namespace CineShot.Tests
{
    public class SolverTests
    {
        [Test]
        public void Orbit_IsDeterministic_ForSameSeed()
        {
            var ctx = new ShotContext
            {
                LocalTime = 1.25f,
                Duration = 5f,
                LookAt = new TargetSample { Position = Vector3.zero, Valid = true },
                Follow = new TargetSample { Position = Vector3.zero, Valid = true },
                Seed = 12345
            };
            var p = RigParams.DefaultFor(RigKind.Orbit);
            var a = CameraPose.Identity;
            var b = CameraPose.Identity;
            OrbitSolver.Solve(in ctx, in p, ref a);
            OrbitSolver.Solve(in ctx, in p, ref b);
            Assert.AreEqual(a.Position, b.Position);
            Assert.AreEqual(a.Rotation, b.Rotation);
        }

        [Test]
        public void Handheld_SameSeedSameResult()
        {
            var ctx = new ShotContext
            {
                LocalTime = 2f,
                Duration = 4f,
                Seed = 99,
                LookAt = new TargetSample { Position = Vector3.forward * 2f, Valid = true },
                Follow = new TargetSample { Position = Vector3.zero, Valid = true }
            };
            var p = RigParams.DefaultFor(RigKind.Handheld);
            var a = CameraPose.Identity;
            var b = CameraPose.Identity;
            HandheldSolver.Solve(in ctx, in p, ref a);
            HandheldSolver.Solve(in ctx, in p, ref b);
            Assert.AreEqual(a.Position, b.Position);
            Assert.AreNotEqual(Vector3.zero, a.Position);
        }

        [Test]
        public void Handheld_ShakeMovesOffBasePose()
        {
            var ctx = new ShotContext
            {
                LocalTime = 1.3f,
                Duration = 4f,
                Seed = 7,
                LookAt = new TargetSample { Position = Vector3.forward * 3f, Valid = true },
                Follow = new TargetSample { Position = Vector3.zero, Valid = true }
            };
            var p = RigParams.DefaultFor(RigKind.Handheld);
            p.P0 = 0.2f;
            p.P1 = 8f;
            var basePose = CameraPose.Identity;
            Vector3 anchor = SolverMath.Anchor(in ctx);
            basePose.Position = anchor + p.A;
            var shaken = basePose;
            float nx, ny, nz;
            HandheldSolver.ApplyShake(in ctx, in p, ref shaken, out nx, out ny, out nz);
            Assert.Greater((shaken.Position - basePose.Position).sqrMagnitude, 1e-8f);
        }

        [Test]
        public void Handheld_HighFrequencyStillLooksAtTarget()
        {
            var target = new Vector3(0f, 1.6f, 2f);
            var ctx = new ShotContext
            {
                LocalTime = 1.7f,
                Duration = 4f,
                Seed = 3,
                LookAt = new TargetSample { Position = target, Valid = true },
                Follow = new TargetSample { Position = Vector3.zero, Valid = true }
            };
            var p = RigParams.DefaultFor(RigKind.Handheld);
            p.P0 = 0.12f;
            p.P1 = 12f;
            p.P2 = 2f;
            var pose = CameraPose.Identity;
            HandheldSolver.Solve(in ctx, in p, ref pose);
            Vector3 fwd = pose.Rotation * Vector3.forward;
            Vector3 to = (target - pose.Position).normalized;
            Assert.Greater(Vector3.Dot(fwd, to), 0.98f);
        }

        [Test]
        public void Spline_SameLookAndFollowStillAims()
        {
            var target = new Vector3(1f, 1.6f, 0f);
            var sample = new TargetSample { Position = target, Valid = true };
            var ctx = new ShotContext
            {
                LocalTime = 0.5f,
                Duration = 2f,
                LookAt = sample,
                Follow = sample,
                CompositionOffset = new Vector3(0.2f, 0f, 0f)
            };
            var p = RigParams.DefaultFor(RigKind.SplineDolly);
            p.A = new Vector3(-2f, 1.6f, -3f);
            p.B = new Vector3(2f, 1.6f, -3f);
            p.P2 = 0f;
            var pose = CameraPose.Identity;
            SplineDollySolver.Solve(in ctx, in p, ref pose);
            Vector3 fwd = pose.Rotation * Vector3.forward;
            Vector3 to = (target - pose.Position).normalized;
            Assert.Greater(Vector3.Dot(fwd, to), 0.995f);
        }

        [Test]
        public void EasingAsset_Lut_Endpoints()
        {
            var ease = ScriptableObject.CreateInstance<EasingAsset>();
            ease.kind = EasingAsset.EaseKind.EaseInOutCubic;
            ease.Rebuild();
            Assert.AreEqual(0f, ease.Evaluate(0f), 1e-5f);
            Assert.AreEqual(1f, ease.Evaluate(1f), 1e-5f);
            Object.DestroyImmediate(ease);
        }

        [Test]
        public void SolverDispatch_TableLengthMatchesRigKind()
        {
            Assert.NotNull(SolverDispatch.Get(RigKind.Static));
            Assert.NotNull(SolverDispatch.Get(RigKind.SplineDolly));
            Assert.NotNull(SolverDispatch.Get(RigKind.Pan));
        }

        [Test]
        public void ShotBlend_LaterStartBeatsHigherPriority()
        {
            Assert.Greater(ShotBlend.CompareActive(2.0, 0, 1.0, 99), 0);
            Assert.Less(ShotBlend.CompareActive(1.0, 99, 2.0, 0), 0);
        }

        [Test]
        public void ShotBlend_LerpSettingsAperture()
        {
            var a = FocusSettings.Default;
            var b = FocusSettings.Default;
            a.aperture = 1.2f;
            b.aperture = 16f;
            Assert.AreEqual(1.2f, ShotBlend.LerpSettings(in a, in b, 0f).aperture, 0.01f);
            Assert.AreEqual(16f, ShotBlend.LerpSettings(in a, in b, 1f).aperture, 0.01f);
            Assert.AreEqual(8.6f, ShotBlend.LerpSettings(in a, in b, 0.5f).aperture, 0.01f);
        }

        [Test]
        public void ShotBlend_SameStartUsesPriority()
        {
            Assert.Greater(ShotBlend.CompareActive(1.0, 5, 1.0, 1), 0);
            Assert.Less(ShotBlend.CompareActive(1.0, 1, 1.0, 5), 0);
            Assert.AreEqual(0, ShotBlend.CompareActive(1.0, 3, 1.0, 3));
        }

        [Test]
        public void Pan_DefaultSweepsGazeAndKeepsCamera()
        {
            var p = RigParams.DefaultFor(RigKind.Pan);
            var startCtx = new ShotContext { Duration = 4f, LocalTime = 0f };
            var endCtx = new ShotContext { Duration = 4f, LocalTime = 4f };
            var start = CameraPose.Identity;
            var end = CameraPose.Identity;
            PanSolver.Solve(in startCtx, in p, ref start);
            PanSolver.Solve(in endCtx, in p, ref end);
            Assert.AreEqual(start.Position, end.Position);
            Assert.Greater(Quaternion.Angle(start.Rotation, end.Rotation), 1f);
        }

        [Test]
        public void Focus_HyperfocalFollowsLensFormula()
        {
            var s = FocusSettings.Default;
            Assert.AreEqual(14.93f, s.HyperfocalDistance, 0.05f);
            s.aperture = 11f;
            Assert.Less(s.HyperfocalDistance, 8f);
        }

        [Test]
        public void HandheldLayer_KeepsTargetInFrame()
        {
            var ctx = new ShotContext
            {
                LocalTime = 2.3f,
                Duration = 6f,
                LookAt = new TargetSample { Position = new Vector3(0f, 1.5f, 0f), Valid = true },
                Seed = 7
            };
            var p = RigParams.DefaultFor(RigKind.Static);
            var pose = CameraPose.Identity;
            StaticSolver.Solve(in ctx, in p, ref pose);
            Vector3 before = pose.Position;
            var layer = RigParams.DefaultFor(RigKind.Handheld);
            HandheldSolver.ApplyLayer(in ctx, in layer, ref pose);
            Assert.Greater(Vector3.Distance(before, pose.Position), 1e-4f);
            Vector3 toTarget = (ctx.LookAt.Position - pose.Position).normalized;
            Assert.Less(Vector3.Angle(pose.Rotation * Vector3.forward, toTarget), 5f);
        }

        [Test]
        public void MatchFrame_HoldsThenBlendsFromMatchPoint()
        {
            var match = new MatchPoint
            {
                valid = true,
                position = new Vector3(10f, 2f, 0f),
                rotation = Quaternion.LookRotation(Vector3.left),
                fov = 30f
            };
            var current = CameraPose.Identity;
            current.Position = Vector3.zero;
            current.Rotation = Quaternion.identity;
            current.Fov = 40f;

            var held = ShotBlend.ApplyMatch(ShotBlendMode.MatchFrame, in match, 0.4f, 1f, 0d, 0.2d, null, in current);
            Assert.AreEqual(match.position, held.Position);
            Assert.AreEqual(30f, held.Fov, 0.01f);

            var mid = ShotBlend.ApplyMatch(ShotBlendMode.MatchFrame, in match, 0.4f, 1f, 0d, 0.7d, null, in current);
            Assert.Greater(Vector3.Distance(match.position, mid.Position), 0.1f);
            Assert.Greater(Vector3.Distance(current.Position, mid.Position), 0.1f);

            var done = ShotBlend.ApplyMatch(ShotBlendMode.MatchFrame, in match, 0.4f, 1f, 0d, 1.0d, null, in current);
            Assert.AreEqual(current.Position, done.Position);
            Assert.AreEqual(40f, done.Fov, 0.01f);
        }

        [Test]
        public void MatchFrame_HoldExceedsWindow_HardCutsToCurrent()
        {
            var match = new MatchPoint
            {
                valid = true,
                position = Vector3.one * 5f,
                rotation = Quaternion.identity,
                fov = 25f
            };
            var current = CameraPose.Identity;
            current.Position = Vector3.forward;
            Assert.IsTrue(ShotBlend.MatchHoldExceedsWindow(1f, 0d, 0.5f));
            var pose = ShotBlend.ApplyMatch(ShotBlendMode.MatchFrame, in match, 1f, 0.5f, 0d, 0.1d, null, in current);
            Assert.AreEqual(current.Position, pose.Position);
        }

        [Test]
        public void ShotOverride_ResolveMatchPoint_UsesMask()
        {
            var shot = ScriptableObject.CreateInstance<ShotAsset>();
            shot.matchPoint = new MatchPoint { valid = true, position = Vector3.right, fov = 35f };
            var over = new ShotOverride
            {
                mask = ShotOverrideMask.MatchPoint,
                matchPoint = new MatchPoint { valid = true, position = Vector3.up, fov = 50f }
            };
            Assert.AreEqual(Vector3.up, over.ResolveMatchPoint(shot).position);
            over.mask = ShotOverrideMask.None;
            Assert.AreEqual(Vector3.right, over.ResolveMatchPoint(shot).position);
            UnityEngine.Object.DestroyImmediate(shot);
        }
    }
}
