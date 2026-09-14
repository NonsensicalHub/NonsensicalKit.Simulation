using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 五轴机械臂点位链表采样。路径：Home → 点位… → Home。
    /// 「末端保持向下」时保持第 5 轴在目标正上方（J5→TCP 朝下），第 4 轴俯仰锁朝下；J5 扭转按点位插值。
    /// </summary>
    public static class RobotArm5Sampler
    {
        private const float CartesianIkStepMeters = 0.1f;
        private const float IncrementalFollowMaxMeters = 0.35f;
        private const float IncrementalFollowAcceptMeters = 0.05f;

        private static float[] s_sampleAngles;
        private static float[] s_sampleSeed;
        private static float[] s_buildCursor;
        private static float[] s_homeContinuous;
        private static readonly List<Vector3> s_posePositions = new List<Vector3>(8);
        private static readonly List<Quaternion> s_poseRotations = new List<Quaternion>(8);
        private static readonly List<float> s_poseJ5Angles = new List<float>(8);

        public readonly struct MotionPlans
        {
            public readonly AxisMotionProfile.Plan[] Joints;
            public readonly AxisMotionProfile.Plan Cartesian;
            public readonly float Duration;
            public readonly float[] StartAngles;
            public readonly float[] EndAngles;
            public readonly Vector3 StartPosition;
            public readonly Vector3 EndPosition;
            public readonly Quaternion StartRotation;
            public readonly Quaternion EndRotation;

            public MotionPlans(
                AxisMotionProfile.Plan[] joints,
                float[] startAngles,
                float[] endAngles,
                Vector3 startPosition,
                Vector3 endPosition,
                Quaternion startRotation,
                Quaternion endRotation,
                AxisMotionProfile.Plan cartesian)
            {
                Joints = joints;
                StartAngles = startAngles;
                EndAngles = endAngles;
                StartPosition = startPosition;
                EndPosition = endPosition;
                StartRotation = startRotation;
                EndRotation = endRotation;
                Cartesian = cartesian;
                float max = 0.01f;
                if (joints != null)
                {
                    for (int i = 0; i < joints.Length; i++)
                        max = Mathf.Max(max, joints[i].Duration);
                }

                if (cartesian.Duration > max)
                    max = cartesian.Duration;
                Duration = max;
            }
        }

        public readonly struct PathMotionPlans
        {
            public readonly MotionPlans[] Segments;
            public readonly float Duration;
            public readonly float[] SegmentStartTimes;

            public bool IsValid =>
                Segments != null &&
                Segments.Length > 0 &&
                SegmentStartTimes != null &&
                SegmentStartTimes.Length == Segments.Length;

            public float[] StartAngles => IsValid ? Segments[0].StartAngles : null;
            public float[] EndAngles => IsValid ? Segments[Segments.Length - 1].EndAngles : null;

            public PathMotionPlans(MotionPlans[] segments)
            {
                Segments = segments;
                if (segments == null || segments.Length == 0)
                {
                    Duration = 0.01f;
                    SegmentStartTimes = System.Array.Empty<float>();
                    return;
                }

                SegmentStartTimes = new float[segments.Length];
                float total = 0f;
                for (int i = 0; i < segments.Length; i++)
                {
                    SegmentStartTimes[i] = total;
                    total += Mathf.Max(0.01f, segments[i].Duration);
                }

                Duration = total;
            }
        }

        internal sealed class PlanCache
        {
            private bool m_valid;
            private int m_animId;
            private int m_animHash;
            private Vector3[] m_positions;
            private Quaternion[] m_rotations;
            private float[] m_j5Angles;
            private bool m_keepTipDown;
            private float m_speed;
            private float m_accel;
            private float[] m_seedAngles;
            private bool m_hasSeed;
            private int m_waypointCount;
            private MotionPlans[] m_segments;
            private PathMotionPlans m_path;

            public bool TryGet(
                RobotArm5Anim anim,
                RobotArm5ClipData data,
                List<Vector3> positions,
                List<Quaternion> rotations,
                List<float> j5Angles,
                float[] continuousStartAngles,
                float[] ikSeedAngles,
                out PathMotionPlans path)
            {
                path = default;
                if (!m_valid || anim == null || data == null ||
                    positions == null || rotations == null || j5Angles == null)
                    return false;

                ResolveMotionParams(anim, data, out float speed, out float accel);
                bool hasSeed = TrySelectSeed(
                    continuousStartAngles, ikSeedAngles, out float[] seed, out int seedLen);
                bool keepTipDown = anim.KeepTipDown;
                if (m_animId != anim.GetInstanceID() ||
                    m_animHash != HashAnim(anim) ||
                    m_waypointCount != data.WaypointCount ||
                    m_keepTipDown != keepTipDown ||
                    !Approx(m_speed, speed) ||
                    !Approx(m_accel, accel) ||
                    !PosesMatch(m_positions, m_rotations, m_j5Angles, positions, rotations, j5Angles) ||
                    m_hasSeed != hasSeed ||
                    (hasSeed && !AnglesMatch(m_seedAngles, seed, seedLen)))
                    return false;

                if (!IsValidPath(m_path, anim.JointCount))
                    return false;

                path = m_path;
                return true;
            }

            public void Store(
                RobotArm5Anim anim,
                RobotArm5ClipData data,
                List<Vector3> positions,
                List<Quaternion> rotations,
                List<float> j5Angles,
                float[] continuousStartAngles,
                float[] ikSeedAngles,
                in PathMotionPlans path)
            {
                if (anim == null || data == null || !IsValidPath(path, anim.JointCount))
                {
                    m_valid = false;
                    return;
                }

                int poseCount = positions.Count;
                EnsurePoses(ref m_positions, ref m_rotations, ref m_j5Angles, poseCount);
                for (int i = 0; i < poseCount; i++)
                {
                    m_positions[i] = positions[i];
                    m_rotations[i] = rotations[i];
                    m_j5Angles[i] = j5Angles[i];
                }

                int segN = path.Segments.Length;
                if (m_segments == null || m_segments.Length != segN)
                    m_segments = new MotionPlans[segN];

                for (int s = 0; s < segN; s++)
                {
                    MotionPlans src = path.Segments[s];
                    int n = src.StartAngles.Length;
                    var startAngles = new float[n];
                    var endAngles = new float[n];
                    var joints = new AxisMotionProfile.Plan[n];
                    CopyAngles(src.StartAngles, startAngles, n);
                    CopyAngles(src.EndAngles, endAngles, n);
                    if (src.Joints != null)
                    {
                        int planN = Mathf.Min(n, src.Joints.Length);
                        for (int i = 0; i < planN; i++)
                            joints[i] = src.Joints[i];
                    }

                    m_segments[s] = new MotionPlans(
                        joints, startAngles, endAngles,
                        src.StartPosition, src.EndPosition,
                        src.StartRotation, src.EndRotation,
                        src.Cartesian);
                }

                m_hasSeed = TrySelectSeed(
                    continuousStartAngles, ikSeedAngles, out float[] seed, out int seedLen);
                if (m_hasSeed)
                {
                    Ensure(ref m_seedAngles, seedLen);
                    CopyAngles(seed, m_seedAngles, seedLen);
                }

                ResolveMotionParams(anim, data, out m_speed, out m_accel);
                m_animId = anim.GetInstanceID();
                m_animHash = HashAnim(anim);
                m_keepTipDown = anim.KeepTipDown;
                m_waypointCount = data.WaypointCount;
                m_path = new PathMotionPlans(m_segments);
                m_valid = true;
            }

            public void Invalidate() => m_valid = false;

            private static void EnsurePoses(
                ref Vector3[] positions,
                ref Quaternion[] rotations,
                ref float[] j5Angles,
                int count)
            {
                if (positions == null || positions.Length != count)
                    positions = new Vector3[count];
                if (rotations == null || rotations.Length != count)
                    rotations = new Quaternion[count];
                if (j5Angles == null || j5Angles.Length != count)
                    j5Angles = new float[count];
            }

            private static bool PosesMatch(
                Vector3[] cachedPos,
                Quaternion[] cachedRot,
                float[] cachedJ5,
                List<Vector3> positions,
                List<Quaternion> rotations,
                List<float> j5Angles)
            {
                if (cachedPos == null || cachedRot == null || cachedJ5 == null ||
                    cachedPos.Length != positions.Count ||
                    cachedRot.Length != rotations.Count ||
                    cachedJ5.Length != j5Angles.Count)
                    return false;

                for (int i = 0; i < positions.Count; i++)
                {
                    if ((positions[i] - cachedPos[i]).sqrMagnitude > 1e-10f)
                        return false;
                    if (!Approx(cachedRot[i], rotations[i]))
                        return false;
                    if (Mathf.Abs(Mathf.DeltaAngle(cachedJ5[i], j5Angles[i])) > 1e-3f)
                        return false;
                }

                return true;
            }
        }

        public static float EstimateDuration(
            RobotArm5Anim anim,
            RobotArm5ClipData data,
            Transform[] targets,
            TimelineStartFallback fallback = default,
            float[] continuousStartAngles = null,
            float[] ikSeedAngles = null)
        {
            if (!TryBuildPathPlans(
                    null, anim, data, targets, fallback,
                    out PathMotionPlans path,
                    continuousStartAngles, ikSeedAngles,
                    useCache: false))
                return -1f;
            return path.Duration;
        }

        public static bool ShouldForceDebugPlayback(RobotArm5Anim anim, RobotArm5ClipData data) =>
            anim != null && anim.DebugForcePlayback ||
            data != null && data.DebugForcePlayback;

        public static bool TryBuildPathPlans(
            RobotArm5Clip clip,
            RobotArm5Anim anim,
            RobotArm5ClipData data,
            Transform[] targets,
            TimelineStartFallback fallback,
            out PathMotionPlans path,
            float[] continuousStartAngles = null,
            float[] ikSeedAngles = null,
            bool useCache = true)
        {
            path = default;
            if (anim == null || data == null || anim.JointCount != RobotArm5Anim.JointCountFixed)
                return false;

            if (!TryResolvePosePath(
                    anim, data, targets, fallback,
                    s_posePositions, s_poseRotations, s_poseJ5Angles,
                    out Transform firstTarget))
                return false;

            if (continuousStartAngles == null)
                continuousStartAngles = GetContinuousStartAngles(data, fallback, firstTarget);
            if (continuousStartAngles == null && anim.HasHome)
            {
                Ensure(ref s_homeContinuous, RobotArm5Anim.JointCountFixed);
                anim.CopyHomeAnglesTo(s_homeContinuous);
                continuousStartAngles = s_homeContinuous;
            }
            else if (continuousStartAngles == null && !anim.HasHome)
            {
                Debug.LogWarning(
                    "[RobotArm5] 无前序且未配置 Home，使用全零关节角作 seed。" +
                    "请在 RobotArm5Anim 上捕获 Home。",
                    anim);
            }

            if (ikSeedAngles == null)
                ikSeedAngles = GetIkSeedAngles(continuousStartAngles, fallback);

            if (useCache &&
                !ShouldForceDebugPlayback(anim, data) &&
                clip != null &&
                clip.CachedPlans.TryGet(
                    anim, data, s_posePositions, s_poseRotations, s_poseJ5Angles,
                    continuousStartAngles, ikSeedAngles, out path))
                return true;

            path = BuildPathPlans(
                anim, data, s_posePositions, s_poseRotations, s_poseJ5Angles,
                continuousStartAngles, ikSeedAngles);

            if (!IsValidPath(path, anim.JointCount))
                return false;

            if (useCache && !ShouldForceDebugPlayback(anim, data))
            {
                clip?.CachedPlans.Store(
                    anim, data, s_posePositions, s_poseRotations, s_poseJ5Angles,
                    continuousStartAngles, ikSeedAngles, path);
            }

            return true;
        }

        public static bool TrySamplePath(
            RobotArm5Anim anim,
            in PathMotionPlans path,
            float normalizedTime)
        {
            if (anim == null || !path.IsValid)
                return false;

            float t = Mathf.Clamp01(normalizedTime) * path.Duration;
            int segIndex = path.Segments.Length - 1;
            float localN = 1f;
            for (int i = 0; i < path.Segments.Length; i++)
            {
                float segStart = path.SegmentStartTimes[i];
                float segDur = Mathf.Max(0.01f, path.Segments[i].Duration);
                float segEnd = segStart + segDur;
                if (t < segEnd || i == path.Segments.Length - 1)
                {
                    segIndex = i;
                    localN = Mathf.Clamp01((t - segStart) / segDur);
                    break;
                }
            }

            ResolveSegmentFollowTargets(
                path, segIndex,
                out Vector3 startPos, out Quaternion startRot,
                out Vector3 endPos, out Quaternion endRot);
            return TryFollowSegment(
                anim, path.Segments[segIndex], localN,
                startPos, startRot, endPos, endRot);
        }

        public static PathMotionPlans BuildPathPlans(
            RobotArm5Anim anim,
            RobotArm5ClipData data,
            List<Vector3> positions,
            List<Quaternion> rotations,
            List<float> j5Angles,
            float[] continuousStartAngles = null,
            float[] ikSeedAngles = null)
        {
            if (anim == null || data == null ||
                positions == null || rotations == null || j5Angles == null ||
                positions.Count < 2 ||
                rotations.Count != positions.Count ||
                j5Angles.Count != positions.Count ||
                anim.JointCount != RobotArm5Anim.JointCountFixed)
            {
                return new PathMotionPlans(System.Array.Empty<MotionPlans>());
            }

            int poseCount = positions.Count;
            var segments = new MotionPlans[poseCount - 1];
            float[] continuous = continuousStartAngles;
            float[] seed = ikSeedAngles;

            for (int i = 0; i < poseCount - 1; i++)
            {
                segments[i] = BuildPlans(
                    anim, data,
                    positions[i], rotations[i], j5Angles[i],
                    positions[i + 1], rotations[i + 1], j5Angles[i + 1],
                    continuous, seed);
                continuous = segments[i].EndAngles;
                seed = null;
            }

            return new PathMotionPlans(segments);
        }

        public static MotionPlans BuildPlans(
            RobotArm5Anim anim,
            RobotArm5ClipData data,
            Vector3 startPos,
            Quaternion startRot,
            float startJ5,
            Vector3 endPos,
            Quaternion endRot,
            float endJ5,
            float[] continuousStartAngles = null,
            float[] ikSeedAngles = null)
        {
            if (anim == null || data == null || anim.JointCount != RobotArm5Anim.JointCountFixed)
            {
                return new MotionPlans(
                    System.Array.Empty<AxisMotionProfile.Plan>(),
                    System.Array.Empty<float>(),
                    System.Array.Empty<float>(),
                    Vector3.zero, Vector3.zero,
                    Quaternion.identity, Quaternion.identity, default);
            }

            int n = RobotArm5Anim.JointCountFixed;
            int j5 = RobotArm5Anim.J5Index;
            var startAngles = new float[n];
            var endAngles = new float[n];
            bool driveJ5 = anim.KeepTipDown;
            startJ5 = anim.ClampAngle(j5, startJ5);
            endJ5 = anim.ClampAngle(j5, endJ5);

            if (continuousStartAngles != null && continuousStartAngles.Length >= n)
            {
                for (int i = 0; i < n; i++)
                    startAngles[i] = anim.ClampAngle(i, continuousStartAngles[i]);
                if (driveJ5)
                    startAngles[j5] = startJ5;

                if (anim.KeepTipDown &&
                    !RobotArm5Ik.IsTipAxisAligned(anim, startAngles, anim.DownWorldAxis))
                {
                    RobotArm5Ik.ForceTipDown(anim, startAngles, anim.DownWorldAxis);
                    if (driveJ5)
                        startAngles[j5] = startJ5;
                    SolvePose(anim, startPos, startRot, startAngles, startAngles);
                    if (driveJ5)
                        startAngles[j5] = startJ5;
                }
            }
            else
            {
                var seed = new float[n];
                if (ikSeedAngles != null && ikSeedAngles.Length >= n)
                {
                    for (int i = 0; i < n; i++)
                        seed[i] = anim.ClampAngle(i, ikSeedAngles[i]);
                }
                else
                {
                    anim.CopyHomeAnglesTo(seed);
                }

                if (driveJ5)
                    seed[j5] = startJ5;
                SolvePose(anim, startPos, startRot, seed, startAngles);
                if (driveJ5)
                    startAngles[j5] = startJ5;
            }

            Ensure(ref s_buildCursor, n);
            CopyAngles(startAngles, s_buildCursor, n);
            if (driveJ5)
                s_buildCursor[j5] = startJ5;
            float tcpDist = Vector3.Distance(startPos, endPos);
            int steps = CartesianIkSteps(tcpDist, minStepsWhenMoving: 3);
            for (int s = 1; s <= steps; s++)
            {
                float u = s / (float)steps;
                Vector3 p = Vector3.Lerp(startPos, endPos, u);
                Quaternion r = Quaternion.Slerp(startRot, endRot, u);
                if (driveJ5)
                    s_buildCursor[j5] = anim.ClampAngle(
                        j5, Mathf.LerpAngle(startJ5, endJ5, u));
                SolvePose(anim, p, r, s_buildCursor, s_buildCursor);
                if (driveJ5)
                    s_buildCursor[j5] = anim.ClampAngle(
                        j5, Mathf.LerpAngle(startJ5, endJ5, u));
            }

            CopyAngles(s_buildCursor, endAngles, n);
            if (driveJ5)
            {
                startAngles[j5] = startJ5;
                endAngles[j5] = endJ5;
                SolvePose(anim, endPos, endRot, endAngles, endAngles);
                endAngles[j5] = endJ5;
            }

            ResolveMotionParams(anim, data, out float speed, out float accel);
            var plans = new AxisMotionProfile.Plan[n];
            for (int i = 0; i < n; i++)
            {
                float delta = Mathf.Abs(Mathf.DeltaAngle(startAngles[i], endAngles[i]));
                plans[i] = AxisMotionProfile.Build(delta, speed, accel);
            }

            AxisMotionProfile.Plan cartesian = BuildCartesianPlan(anim, data, tcpDist);
            return new MotionPlans(
                plans, startAngles, endAngles,
                startPos, endPos, startRot, endRot, cartesian);
        }

        public static bool TryResolvePosePath(
            RobotArm5Anim anim,
            RobotArm5ClipData data,
            Transform[] targets,
            TimelineStartFallback fallback,
            List<Vector3> positions,
            List<Quaternion> rotations,
            List<float> j5Angles,
            out Transform firstTarget)
        {
            positions.Clear();
            rotations.Clear();
            j5Angles.Clear();
            firstTarget = GetTarget(targets, 0);

            if (anim == null || data == null || data.WaypointCount <= 0)
                return false;

            _ = fallback;

            if (!TryResolveHomePose(
                    anim, out Vector3 homePos, out Quaternion homeRot, out float homeJ5))
                return false;

            positions.Add(homePos);
            rotations.Add(homeRot);
            j5Angles.Add(homeJ5);

            float currentJ5 = homeJ5;
            for (int i = 0; i < data.WaypointCount; i++)
            {
                Transform target = GetTarget(targets, i);
                if (!TryResolveWaypointPose(
                        anim, data, i, target,
                        out Vector3 pos, out Quaternion rot))
                    return false;

                currentJ5 = ResolveWaypointJ5(anim, data, i, currentJ5);
                positions.Add(pos);
                rotations.Add(rot);
                j5Angles.Add(currentJ5);
            }

            positions.Add(homePos);
            rotations.Add(homeRot);
            j5Angles.Add(homeJ5);

            return positions.Count >= 2;
        }

        public static float ResolveWaypointJ5(
            RobotArm5Anim anim,
            RobotArm5ClipData data,
            int index,
            float inheritJ5)
        {
            RobotArm5Waypoint wp = data != null ? data.GetWaypoint(index) : null;
            if (wp != null && wp.UseJ5Angle)
                return anim != null
                    ? anim.ClampAngle(RobotArm5Anim.J5Index, wp.J5Angle)
                    : wp.J5Angle;
            return anim != null
                ? anim.ClampAngle(RobotArm5Anim.J5Index, inheritJ5)
                : inheritJ5;
        }

        public static bool TryResolveWaypointPose(
            RobotArm5Anim anim,
            RobotArm5ClipData data,
            int index,
            Transform target,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            if (anim == null || data == null)
                return false;

            RobotArm5Waypoint wp = data.GetWaypoint(index);
            Vector3 offset = wp != null ? wp.Offset : Vector3.zero;

            if (target != null)
            {
                position = target.position + offset;
                rotation = target.rotation;
                return true;
            }

            bool usePoint = wp != null &&
                            (wp.HasPoint || index == 0 && data.HasStartPoint);
            if (usePoint)
            {
                position = wp.Point + offset;
                rotation = Quaternion.Euler(wp.Euler);
                return true;
            }

            return false;
        }

        public static bool TryResolveHomePose(
            RobotArm5Anim anim,
            out Vector3 position,
            out Quaternion rotation,
            out float j5Angle)
        {
            position = default;
            rotation = Quaternion.identity;
            j5Angle = 0f;
            if (anim == null || anim.JointCount != RobotArm5Anim.JointCountFixed)
                return false;

            int n = RobotArm5Anim.JointCountFixed;
            var angles = new float[n];
            if (anim.HasHome)
                anim.CopyHomeAnglesTo(angles);
            else
            {
                for (int i = 0; i < n; i++)
                    angles[i] = 0f;
            }

            RobotArm5Ik.Forward(anim, angles, out position, out rotation);
            j5Angle = anim.ClampAngle(RobotArm5Anim.J5Index, angles[RobotArm5Anim.J5Index]);
            return true;
        }

        public static TimelineStartFallback ResolvePreviousClipEnd(
            RobotArm5Anim anim,
            TimelineClip timelineClip,
            IExposedPropertyTable resolver)
        {
            if (timelineClip == null || anim == null)
                return TimelineStartFallback.None;

            var prevChain = new List<TimelineClip>(8);
            TimelineClip cursor = timelineClip;
            while (TryFindPreviousRobotArm5Clip(cursor, out TimelineClip previous))
            {
                prevChain.Add(previous);
                cursor = previous;
            }

            if (prevChain.Count == 0)
                return TimelineStartFallback.None;

            prevChain.Reverse();

            TimelineStartFallback fallback = TimelineStartFallback.None;
            for (int i = 0; i < prevChain.Count; i++)
            {
                TimelineClip previous = prevChain[i];
                if (previous.asset is not RobotArm5Clip prevAsset || prevAsset.Data == null)
                    return TimelineStartFallback.None;

                Transform[] targets = prevAsset.ResolveWaypointTargets(resolver);
                if (!TryBuildPathPlans(
                        prevAsset, anim, prevAsset.Data, targets, fallback,
                        out PathMotionPlans path))
                    return TimelineStartFallback.None;

                MotionPlans lastSeg = path.Segments[path.Segments.Length - 1];
                fallback = TimelineStartFallback.FromCached(
                    lastSeg.EndPosition, lastSeg.EndRotation, path.EndAngles);
            }

            return fallback;
        }

        public static float[] GetContinuousStartAngles(
            RobotArm5ClipData data,
            TimelineStartFallback fallback,
            Transform firstTarget = null)
        {
            _ = data;
            _ = firstTarget;
            if (fallback.HasJointAngles)
                return fallback.JointAngles;
            return null;
        }

        public static float[] GetIkSeedAngles(
            float[] continuousStartAngles,
            TimelineStartFallback fallback)
        {
            if (continuousStartAngles != null)
                return null;
            return fallback.HasJointAngles ? fallback.JointAngles : null;
        }

        public static bool TryFindPreviousRobotArm5Clip(
            TimelineClip clip,
            out TimelineClip previous)
        {
            previous = null;
            TrackAsset track = clip?.GetParentTrack();
            if (track == null)
                return false;

            double bestStart = double.NegativeInfinity;
            foreach (TimelineClip other in track.GetClips())
            {
                if (other == null || other == clip || other.asset is not RobotArm5Clip)
                    continue;
                if (other.start >= clip.start)
                    continue;
                if (other.start > bestStart)
                {
                    bestStart = other.start;
                    previous = other;
                }
            }

            return previous != null;
        }

        public static Transform GetTarget(Transform[] targets, int index)
        {
            if (targets == null || index < 0 || index >= targets.Length)
                return null;
            return targets[index];
        }

        public readonly struct TimelineStartFallback
        {
            public readonly bool HasValue;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly float[] JointAngles;

            public bool HasJointAngles =>
                JointAngles != null && JointAngles.Length >= RobotArm5Anim.JointCountFixed;

            public TimelineStartFallback(
                Vector3 position,
                Quaternion rotation,
                float[] jointAngles = null)
                : this(position, rotation, jointAngles, copyAngles: true)
            {
            }

            public static TimelineStartFallback FromCached(
                Vector3 position,
                Quaternion rotation,
                float[] jointAngles)
            {
                return new TimelineStartFallback(position, rotation, jointAngles, copyAngles: false);
            }

            private TimelineStartFallback(
                Vector3 position,
                Quaternion rotation,
                float[] jointAngles,
                bool copyAngles)
            {
                HasValue = true;
                Position = position;
                Rotation = rotation;
                if (jointAngles != null && jointAngles.Length >= RobotArm5Anim.JointCountFixed)
                {
                    if (copyAngles)
                    {
                        JointAngles = new float[jointAngles.Length];
                        for (int i = 0; i < jointAngles.Length; i++)
                            JointAngles[i] = jointAngles[i];
                    }
                    else
                    {
                        JointAngles = jointAngles;
                    }
                }
                else
                {
                    JointAngles = null;
                }
            }

            public static TimelineStartFallback None => default;
        }

        private static bool IsValidPath(in PathMotionPlans path, int jointCount)
        {
            if (!path.IsValid || jointCount != RobotArm5Anim.JointCountFixed)
                return false;
            for (int i = 0; i < path.Segments.Length; i++)
            {
                if (!IsValidPlans(path.Segments[i], jointCount))
                    return false;
            }

            return true;
        }

        private static bool IsValidPlans(in MotionPlans plans, int jointCount)
        {
            return plans.StartAngles != null &&
                   plans.EndAngles != null &&
                   plans.Joints != null &&
                   jointCount == RobotArm5Anim.JointCountFixed &&
                   plans.StartAngles.Length >= jointCount &&
                   plans.EndAngles.Length >= jointCount &&
                   plans.Joints.Length >= jointCount;
        }

        private static void ResolveSegmentFollowTargets(
            in PathMotionPlans path,
            int segIndex,
            out Vector3 startPos,
            out Quaternion startRot,
            out Vector3 endPos,
            out Quaternion endRot)
        {
            MotionPlans seg = path.Segments[segIndex];
            startPos = seg.StartPosition;
            startRot = seg.StartRotation;
            endPos = seg.EndPosition;
            endRot = seg.EndRotation;

            int poseCount = path.Segments.Length + 1;
            if (s_posePositions.Count != poseCount || s_poseRotations.Count != poseCount)
                return;
            if (segIndex < 0 || segIndex + 1 >= poseCount)
                return;

            startPos = s_posePositions[segIndex];
            startRot = s_poseRotations[segIndex];
            endPos = s_posePositions[segIndex + 1];
            endRot = s_poseRotations[segIndex + 1];
        }

        private static bool TryFollowSegment(
            RobotArm5Anim anim,
            in MotionPlans plans,
            float normalizedTime,
            Vector3 startPos,
            Quaternion startRot,
            Vector3 endPos,
            Quaternion endRot)
        {
            if (anim == null || plans.Joints == null || plans.StartAngles == null || plans.EndAngles == null)
                return false;

            int n = anim.JointCount;
            if (n != RobotArm5Anim.JointCountFixed ||
                plans.StartAngles.Length < n ||
                plans.EndAngles.Length < n ||
                plans.Joints.Length < n)
                return false;

            float t = Mathf.Clamp01(normalizedTime) * plans.Duration;
            float u;
            if (!plans.Cartesian.IsStationary)
                u = AxisMotionProfile.ProgressAt(plans.Cartesian, t);
            else
            {
                u = 0f;
                int posJointMax = anim.KeepTipDown
                    ? RobotArm5Anim.WristPitchJointIndexFixed
                    : n - 1;
                for (int i = 0; i <= posJointMax; i++)
                    u = Mathf.Max(u, AxisMotionProfile.ProgressAt(plans.Joints[i], t));
            }

            Vector3 targetPos = Vector3.Lerp(startPos, endPos, u);
            Quaternion targetRot = Quaternion.Slerp(startRot, endRot, u);

            float uJ5 = AxisMotionProfile.ProgressAt(plans.Joints[RobotArm5Anim.J5Index], t);
            float targetJ5 = Mathf.LerpAngle(
                plans.StartAngles[RobotArm5Anim.J5Index],
                plans.EndAngles[RobotArm5Anim.J5Index],
                uJ5);

            Ensure(ref s_sampleAngles, n);
            Ensure(ref s_sampleSeed, n);

            if (!TryIncrementalFollow(
                    anim, targetPos, targetRot, s_sampleAngles, targetJ5))
            {
                FollowAlongLine(
                    anim, startPos, startRot, targetPos, targetRot,
                    plans.StartAngles, s_sampleAngles, targetJ5);
            }

            if (anim.KeepTipDown)
            {
                s_sampleAngles[RobotArm5Anim.J5Index] =
                    anim.ClampAngle(RobotArm5Anim.J5Index, targetJ5);
                RobotArm5Ik.ForceTipDown(anim, s_sampleAngles, anim.DownWorldAxis);
                s_sampleAngles[RobotArm5Anim.J5Index] =
                    anim.ClampAngle(RobotArm5Anim.J5Index, targetJ5);
            }

            anim.ApplyAngles(s_sampleAngles);
            return true;
        }

        private static bool TryIncrementalFollow(
            RobotArm5Anim anim,
            Vector3 targetPos,
            Quaternion targetRot,
            float[] outAngles,
            float targetJ5)
        {
            int n = anim.JointCount;
            int j5 = RobotArm5Anim.J5Index;
            Ensure(ref s_sampleSeed, n);
            anim.ReadCurrentAngles(s_sampleSeed);
            if (anim.KeepTipDown)
                s_sampleSeed[j5] = anim.ClampAngle(j5, targetJ5);

            RobotArm5Ik.Forward(anim, s_sampleSeed, out Vector3 tip, out _);
            float maxSqr = IncrementalFollowMaxMeters * IncrementalFollowMaxMeters;
            if ((tip - targetPos).sqrMagnitude > maxSqr)
                return false;

            SolvePose(anim, targetPos, targetRot, s_sampleSeed, outAngles);
            if (anim.KeepTipDown)
                outAngles[j5] = anim.ClampAngle(j5, targetJ5);

            RobotArm5Ik.Forward(anim, outAngles, out Vector3 reached, out _);
            float acceptSqr = IncrementalFollowAcceptMeters * IncrementalFollowAcceptMeters;
            if ((reached - targetPos).sqrMagnitude > acceptSqr)
                return false;

            if (anim.KeepTipDown &&
                !RobotArm5Ik.IsTipAxisAligned(anim, outAngles, anim.DownWorldAxis))
                return false;

            return true;
        }

        private static void FollowAlongLine(
            RobotArm5Anim anim,
            Vector3 startPos,
            Quaternion startRot,
            Vector3 targetPos,
            Quaternion targetRot,
            float[] startAngles,
            float[] outAngles,
            float targetJ5)
        {
            int n = anim.JointCount;
            int j5 = RobotArm5Anim.J5Index;
            Ensure(ref s_sampleSeed, n);
            CopyAngles(startAngles, s_sampleSeed, n);
            float startJ5 = anim.KeepTipDown
                ? anim.ClampAngle(j5, startAngles[j5])
                : 0f;
            if (anim.KeepTipDown)
                s_sampleSeed[j5] = startJ5;

            int steps = CartesianIkSteps(Vector3.Distance(startPos, targetPos), minStepsWhenMoving: 1);
            for (int s = 1; s <= steps; s++)
            {
                float su = s / (float)steps;
                Vector3 p = Vector3.Lerp(startPos, targetPos, su);
                Quaternion r = Quaternion.Slerp(startRot, targetRot, su);
                if (anim.KeepTipDown)
                    s_sampleSeed[j5] = anim.ClampAngle(
                        j5, Mathf.LerpAngle(startJ5, targetJ5, su));
                SolvePose(anim, p, r, s_sampleSeed, s_sampleSeed);
                if (anim.KeepTipDown)
                    s_sampleSeed[j5] = anim.ClampAngle(
                        j5, Mathf.LerpAngle(startJ5, targetJ5, su));
            }

            CopyAngles(s_sampleSeed, outAngles, n);
            if (anim.KeepTipDown)
                outAngles[j5] = anim.ClampAngle(j5, targetJ5);
        }

        private static int CartesianIkSteps(float distance, int minStepsWhenMoving)
        {
            if (distance < 0.02f)
                return 1;
            return Mathf.Clamp(
                Mathf.CeilToInt(distance / CartesianIkStepMeters),
                Mathf.Max(1, minStepsWhenMoving),
                20);
        }

        private static void SolvePose(
            RobotArm5Anim arm,
            Vector3 targetPos,
            Quaternion targetRot,
            float[] seed,
            float[] outAngles)
        {
            bool keepTipDown = arm.KeepTipDown;
            bool constrainOrientation = !keepTipDown;

            RobotArm5Ik.SolveToTarget(
                arm, targetPos, seed, outAngles,
                keepTipDown,
                constrainOrientation,
                targetRot,
                downWorldAxis: arm.DownWorldAxis);
        }

        private static AxisMotionProfile.Plan BuildCartesianPlan(
            RobotArm5Anim anim,
            RobotArm5ClipData data,
            float tcpDistance)
        {
            ResolveMotionParams(anim, data, out float degSpeed, out float degAccel);
            float reach = 0.15f;
            int n = anim.JointCount;
            float sum = 0f;
            for (int i = 0; i < n; i++)
                sum += anim.GetLinkLength(i);
            if (sum > 1e-4f)
                reach = Mathf.Max(0.15f, sum * 0.4f);

            float linearSpeed = degSpeed * Mathf.Deg2Rad * reach;
            float linearAccel = degAccel * Mathf.Deg2Rad * reach;
            return AxisMotionProfile.Build(tcpDistance, linearSpeed, linearAccel);
        }

        private static void ResolveMotionParams(
            RobotArm5Anim anim,
            RobotArm5ClipData data,
            out float speed,
            out float accel)
        {
            _ = anim;
            if (data != null)
            {
                speed = DurationUtility.SafeSpeed(data.JointSpeed);
                accel = Mathf.Max(0.01f, data.JointAcceleration);
            }
            else
            {
                speed = DurationUtility.SafeSpeed(60f);
                accel = 90f;
            }
        }

        private static bool TrySelectSeed(
            float[] continuousStartAngles,
            float[] ikSeedAngles,
            out float[] seed,
            out int length)
        {
            if (continuousStartAngles != null &&
                continuousStartAngles.Length >= RobotArm5Anim.JointCountFixed)
            {
                seed = continuousStartAngles;
                length = continuousStartAngles.Length;
                return true;
            }

            if (ikSeedAngles != null && ikSeedAngles.Length >= RobotArm5Anim.JointCountFixed)
            {
                seed = ikSeedAngles;
                length = ikSeedAngles.Length;
                return true;
            }

            seed = null;
            length = 0;
            return false;
        }

        private static int HashAnim(RobotArm5Anim arm)
        {
            unchecked
            {
                int n = arm.JointCount;
                int h = n * 397;
                h = (h * 397) ^ 5;
                h = (h * 397) ^ arm.IkIterations;
                h = (h * 397) ^ arm.IkPositionTolerance.GetHashCode();
                h = (h * 397) ^ HashVec(arm.ToolOffsetLocal);
                h = (h * 397) ^ HashVec(arm.TipDownAxisLocal);
                h = (h * 397) ^ arm.TipDownJointIndex;
                h = (h * 397) ^ (arm.KeepTipDown ? 1 : 0);
                h = (h * 397) ^ HashVec(arm.DownWorldAxis);
                h = (h * 397) ^ (arm.HasHome ? 1 : 0);
                float[] home = arm.HomeAngles;
                for (int i = 0; i < n; i++)
                {
                    if (arm.TryGetJointSettings(i, out RobotArm5Anim.JointSettings s) && s != null)
                    {
                        h = (h * 397) ^ s.LinkLength.GetHashCode();
                        h = (h * 397) ^ HashVec(s.AxisLocal);
                        h = (h * 397) ^ HashVec(s.LinkDirectionLocal);
                        h = (h * 397) ^ s.MinAngle.GetHashCode();
                        h = (h * 397) ^ s.MaxAngle.GetHashCode();
                    }

                    if (home != null && i < home.Length)
                        h = (h * 397) ^ home[i].GetHashCode();
                }

                return h;
            }
        }

        private static int HashVec(Vector3 v)
        {
            unchecked
            {
                int h = v.x.GetHashCode();
                h = (h * 397) ^ v.y.GetHashCode();
                h = (h * 397) ^ v.z.GetHashCode();
                return h;
            }
        }

        private static bool Approx(float a, float b) => Mathf.Abs(a - b) <= 1e-5f;

        private static bool Approx(Quaternion a, Quaternion b) =>
            Mathf.Abs(Quaternion.Dot(a, b)) > 0.999999f;

        private static bool AnglesMatch(float[] a, float[] b, int count)
        {
            if (a == null || b == null || a.Length < count || b.Length < count)
                return false;
            for (int i = 0; i < count; i++)
            {
                if (Mathf.Abs(Mathf.DeltaAngle(a[i], b[i])) > 1e-3f)
                    return false;
            }

            return true;
        }

        private static void CopyAngles(float[] src, float[] dst, int count)
        {
            for (int i = 0; i < count; i++)
                dst[i] = src[i];
        }

        private static void Ensure(ref float[] buffer, int n)
        {
            if (buffer == null || buffer.Length < n)
                buffer = new float[n];
        }
    }
}
