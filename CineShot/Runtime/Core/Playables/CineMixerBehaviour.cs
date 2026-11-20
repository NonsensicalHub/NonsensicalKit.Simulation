using UnityEngine;
using UnityEngine.Playables;

namespace CineShot.Core
{
    public class CineMixerBehaviour : PlayableBehaviour
    {
        struct SolvedClip
        {
            public CineShotClip Clip;
            public ShotAsset Shot;
            public CameraPose Pose;
            public double Local;
            public double Start;
            public double Duration;
            public FocusMode FocusMode;
            public float FocusHint;
            public FocusSettings FocusSettings;
            public Transform FocusTarget;
            public TargetSample LookAt;
            public TargetSample Follow;
            public HandheldLayer Handheld;
            public int Priority;
        }

        readonly SolvedClip[] m_Solved = new SolvedClip[16];
        CameraPose m_PrevPose;
        bool m_HasPrev;
        CineShotClip m_LatchedClip;
        CameraPose m_LatchPose;
        bool m_HasLatch;
        double m_HeldOverlap;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var rig = playerData as CineShotRig;
            if (rig == null)
                return;

            CinePerfProbe.BeginSolve();
            float dt = info.deltaTime;
            double directorTime = playable.GetTime();
            var resolver = playable.GetGraph().GetResolver();
            int n = playable.GetInputCount();
            int count = 0;

            for (int j = 0; j < n && count < m_Solved.Length; j++)
            {
                if (playable.GetInputWeight(j) <= 0f)
                    continue;
                var input = (ScriptPlayable<CineShotBehaviour>)playable.GetInput(j);
                var beh = input.GetBehaviour();
                if (beh == null || beh.Clip == null || beh.Clip.shot == null)
                    continue;

                var clip = beh.Clip;
                var shot = clip.shot;
                double local = input.GetTime();
                double dur = input.GetDuration();

                RigParams rigParams;
                Vector3 composition;
                float fov, near, far, focusDistance;
                FocusMode mode;
                FocusSettings focusSettings;
                clip.overrides.ApplyTo(shot, out rigParams, out composition, out fov, out near, out far,
                    out mode, out focusDistance, out focusSettings);

                Transform lookTr = clip.lookAt.Resolve(resolver);
                Transform followTr = clip.follow.Resolve(resolver);
                var look = rig.SampleLookAt(lookTr, clip.overrides.ResolveLookOffset(shot), dt);
                var follow = rig.SampleFollow(followTr, clip.overrides.ResolveFollowOffset(shot), dt);
                var ctx = clip.BuildContext((float)local, (float)dur, in look, in follow, composition);
                var pose = CameraPose.Identity;
                pose.Fov = fov;
                pose.NearClip = near;
                pose.FarClip = far;
                pose.FocusDistance = focusDistance;
                SolverDispatch.Solve(shot.rigKind, in ctx, in rigParams, ref pose);

                m_Solved[count++] = new SolvedClip
                {
                    Clip = clip,
                    Shot = shot,
                    Pose = pose,
                    Local = local,
                    Start = directorTime - local,
                    Duration = dur,
                    FocusMode = mode,
                    FocusHint = focusDistance,
                    FocusSettings = focusSettings,
                    FocusTarget = lookTr != null ? lookTr : followTr,
                    LookAt = look,
                    Follow = follow,
                    Handheld = clip.overrides.ResolveHandheld(shot),
                    Priority = clip.overrides.ResolvePriority(shot)
                };
            }

            if (count == 0)
            {
                rig.SetActiveShot(null);
                CinePerfProbe.EndSolve();
                return;
            }

            int current = 0;
            for (int i = 1; i < count; i++)
            {
                if (ShotBlend.CompareActive(m_Solved[i].Start, m_Solved[i].Priority,
                        m_Solved[current].Start, m_Solved[current].Priority) > 0)
                    current = i;
            }
            int previous = -1;
            for (int i = 0; i < count; i++)
            {
                if (i == current)
                    continue;
                if (previous < 0
                    || ShotBlend.CompareActive(m_Solved[i].Start, m_Solved[i].Priority,
                        m_Solved[previous].Start, m_Solved[previous].Priority) > 0)
                    previous = i;
            }

            SolvedClip active = m_Solved[current];
            if (active.Clip != m_LatchedClip)
            {
                // 无重叠的混合从上一帧最终位姿出发
                m_LatchPose = m_PrevPose;
                m_HasLatch = m_HasPrev;
                m_LatchedClip = active.Clip;
                m_HeldOverlap = 0d;
            }

            double overlap = 0d;
            if (previous >= 0)
            {
                overlap = (m_Solved[previous].Start + m_Solved[previous].Duration) - active.Start;
                if (overlap > 0.01d)
                    m_HeldOverlap = overlap;
            }
            else
                overlap = m_HeldOverlap;

            bool hasFrom = previous >= 0 || m_HasLatch;
            CameraPose from = previous >= 0 ? m_Solved[previous].Pose : m_LatchPose;
            EasingAsset curve = ShotBlend.BlendCurve(active.Clip, previous >= 0 ? m_Solved[previous].Clip : null);
            CameraPose blended = ShotBlend.Apply(active.Clip.blendMode, active.Clip.blendDuration, overlap, active.Local, hasFrom, in from, in active.Pose, curve);
            MatchPoint matchPoint = active.Clip.overrides.ResolveMatchPoint(active.Shot);
            blended = ShotBlend.ApplyMatch(
                active.Clip.blendMode,
                in matchPoint,
                active.Clip.matchHoldDuration,
                active.Clip.blendDuration,
                overlap,
                active.Local,
                curve,
                in blended);

            var aimCtx = new ShotContext
            {
                LocalTime = (float)directorTime,
                Seed = active.Shot.Seed,
                LookAt = active.LookAt,
                Follow = active.Follow
            };
            if (active.Handheld.enabled)
                HandheldSolver.ApplyLayer(in aimCtx, in active.Handheld.parameters, ref blended);

            FocusSettings blendedFocus = active.FocusSettings;
            float focusHint = active.FocusHint;
            FocusMode focusMode = active.FocusMode;
            Transform focusTarget = active.FocusTarget;
            if (active.Clip.blendMode == ShotBlendMode.Blend && previous >= 0)
            {
                var prev = m_Solved[previous];
                float u = ShotBlend.Factor(ShotBlend.Window(overlap, active.Clip.blendDuration), active.Local, curve);
                blendedFocus = ShotBlend.LerpSettings(in prev.FocusSettings, in active.FocusSettings, u);
                if (prev.FocusMode == FocusMode.FixedDistance && focusMode == FocusMode.FixedDistance)
                    focusHint = Mathf.Lerp(prev.FocusHint, active.FocusHint, u);
                else if (u < 0.5f)
                {
                    focusMode = prev.FocusMode;
                    focusHint = prev.FocusHint;
                    focusTarget = prev.FocusTarget;
                }
            }

            Vector3 aimPoint = SolverMath.AimPoint(in aimCtx, blended.Position);
            bool hasAim = active.LookAt.Valid || active.Follow.Valid;
            rig.SetActiveShot(active.Shot);
            rig.SubmitPose(in blended, dt, focusHint, focusMode, focusTarget, in blendedFocus, aimPoint, hasAim);
            m_PrevPose = blended;
            m_HasPrev = true;
            CinePerfProbe.EndSolve();
        }
    }
}
