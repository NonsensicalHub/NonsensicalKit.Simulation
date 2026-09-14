using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 六轴机械臂点位链表：每个点位视为跟随目标的位姿。
    /// 中间状态先对当前段起终点跟随目标做位置朝向插值，再按与独端IK 跟随
    /// 相同的方式跟踪该插值目标（沿直线链式 IK，不用关节角插值当姿态）。
    /// 「末端保持向下」时第 6 轴按各点 J6 目标角插值，并与朝下约束同帧求解
    /// （约束轴为 J5→TCP，依赖当前扭转角）。
    /// 总时长= Σ 各段 max(各关节行程 TCP 直线行程)。
    /// </summary>
    public static class RobotArmSampler
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
        private static readonly List<float> s_poseJ6Angles = new List<float>(8);

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
                float[] endAngles)
                : this(
                    joints, startAngles, endAngles,
                    Vector3.zero, Vector3.zero,
                    Quaternion.identity, Quaternion.identity,
                    default)
            {
            }

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

        /// <summary>多段依次移动计划；Duration 为各段之和。</summary>
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

            public float[] StartAngles =>
                IsValid ? Segments[0].StartAngles : null;

            public float[] EndAngles =>
                IsValid ? Segments[Segments.Length - 1].EndAngles : null;

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

        /// <summary>
        /// 按 Clip + 点位指纹缓存 IK 路径。Timeline 预览 / Inspector 仅在目标或参数变化时重解。
    /// </summary>
        internal sealed class PlanCache
        {
            private bool m_valid;
            private int m_animId;
            private int m_animHash;
            private Vector3[] m_positions;
            private Quaternion[] m_rotations;
            private float[] m_j6Angles;
            private bool m_keepTipDown;
            private bool m_constrainOrientation;
            private float m_speed;
            private float m_accel;
            private float[] m_seedAngles;
            private bool m_hasSeed;
            private bool m_hasStartPoint;
            private int m_waypointCount;
            private MotionPlans[] m_segments;
            private PathMotionPlans m_path;

            public bool TryGet(
                RobotArmAnim anim,
                RobotArmClipData data,
                List<Vector3> positions,
                List<Quaternion> rotations,
                List<float> j6Angles,
                float[] continuousStartAngles,
                float[] ikSeedAngles,
                out PathMotionPlans path)
            {
                path = default;
                if (!m_valid || anim == null || data == null ||
                    positions == null || rotations == null || j6Angles == null)
                    return false;

                ResolveMotionParams(anim, data, out float speed, out float accel);
                bool hasSeed = TrySelectSeed(
                    continuousStartAngles, ikSeedAngles, out float[] seed, out int seedLen);
                bool keepTipDown = anim.KeepTipDown;
                bool constrainOrientation = !keepTipDown;
                if (m_animId != anim.GetInstanceID() ||
                    m_animHash != HashAnim(anim) ||
                    m_hasStartPoint != data.HasStartPoint ||
                    m_waypointCount != data.WaypointCount ||
                    m_keepTipDown != keepTipDown ||
                    m_constrainOrientation != constrainOrientation ||
                    !Approx(m_speed, speed) ||
                    !Approx(m_accel, accel) ||
                    !PosesMatch(m_positions, m_rotations, m_j6Angles, positions, rotations, j6Angles) ||
                    m_hasSeed != hasSeed ||
                    (hasSeed && !AnglesMatch(m_seedAngles, seed, seedLen)))
                    return false;

                if (!IsValidPath(m_path, anim.JointCount))
                    return false;

                path = m_path;
                return true;
            }

            public void Store(
                RobotArmAnim anim,
                RobotArmClipData data,
                List<Vector3> positions,
                List<Quaternion> rotations,
                List<float> j6Angles,
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
                EnsurePoses(ref m_positions, ref m_rotations, ref m_j6Angles, poseCount);
                for (int i = 0; i < poseCount; i++)
                {
                    m_positions[i] = positions[i];
                    m_rotations[i] = rotations[i];
                    m_j6Angles[i] = j6Angles[i];
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
                m_constrainOrientation = !m_keepTipDown;
                m_hasStartPoint = data.HasStartPoint;
                m_waypointCount = data.WaypointCount;
                m_path = new PathMotionPlans(m_segments);
                m_valid = true;
            }

            public void Invalidate() => m_valid = false;

            private static void EnsurePoses(
                ref Vector3[] positions,
                ref Quaternion[] rotations,
                ref float[] j6Angles,
                int count)
            {
                if (positions == null || positions.Length != count)
                    positions = new Vector3[count];
                if (rotations == null || rotations.Length != count)
                    rotations = new Quaternion[count];
                if (j6Angles == null || j6Angles.Length != count)
                    j6Angles = new float[count];
            }

            private static bool PosesMatch(
                Vector3[] cachedPos,
                Quaternion[] cachedRot,
                float[] cachedJ6,
                List<Vector3> positions,
                List<Quaternion> rotations,
                List<float> j6Angles)
            {
                if (cachedPos == null || cachedRot == null || cachedJ6 == null ||
                    cachedPos.Length != positions.Count ||
                    cachedRot.Length != rotations.Count ||
                    cachedJ6.Length != j6Angles.Count)
                    return false;

                for (int i = 0; i < positions.Count; i++)
                {
                    if ((positions[i] - cachedPos[i]).sqrMagnitude > 1e-10f)
                        return false;
                    if (!Approx(cachedRot[i], rotations[i]))
                        return false;
                    if (Mathf.Abs(Mathf.DeltaAngle(cachedJ6[i], j6Angles[i])) > 1e-3f)
                        return false;
                }

                return true;
            }
        }

        public static float EstimateDuration(
            RobotArmAnim anim,
            RobotArmClipData data,
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

        public static bool IsValidMotionPlans(in MotionPlans plans, int jointCount) =>
            IsValidPlans(plans, jointCount);

        public static bool IsValidPathMotionPlans(in PathMotionPlans path, int jointCount) =>
            IsValidPath(path, jointCount);

        /// <summary>Clip 或 RobotArmAnim 开启调试时，禁用 IK 缓存并每帧完整重解。</summary>
        public static bool ShouldForceDebugPlayback(RobotArmAnim anim, RobotArmClipData data) =>
            anim != null && anim.DebugForcePlayback ||
            data != null && data.DebugForcePlayback;

        public static bool TryBuildPathPlans(
            RobotArmClip clip,
            RobotArmAnim anim,
            RobotArmClipData data,
            Transform[] targets,
            TimelineStartFallback fallback,
            out PathMotionPlans path,
            float[] continuousStartAngles = null,
            float[] ikSeedAngles = null,
            bool useCache = true)
        {
            path = default;
            if (anim == null || data == null || anim.JointCount != RobotArmAnim.JointCountFixed)
                return false;

            if (!TryResolvePosePath(
                    anim, data, targets, fallback,
                    s_posePositions, s_poseRotations, s_poseJ6Angles,
                    out Transform firstTarget))
                return false;

            if (continuousStartAngles == null)
                continuousStartAngles = GetContinuousStartAngles(data, fallback, firstTarget);
            if (continuousStartAngles == null && anim.HasHome)
            {
                Ensure(ref s_homeContinuous, RobotArmAnim.JointCountFixed);
                anim.CopyHomeAnglesTo(s_homeContinuous);
                continuousStartAngles = s_homeContinuous;
            }
            else if (continuousStartAngles == null && !anim.HasHome)
            {
                Debug.LogWarning(
                    "[RobotArm] 无前序且未配置 Home，使用全零关节角作 seed。" +
                    "请在 RobotArmAnim 上捕获 Home。",
                    anim);
            }

            if (ikSeedAngles == null)
                ikSeedAngles = GetIkSeedAngles(continuousStartAngles, fallback);

            if (useCache &&
                !ShouldForceDebugPlayback(anim, data) &&
                clip != null &&
                clip.CachedPlans.TryGet(
                    anim, data, s_posePositions, s_poseRotations, s_poseJ6Angles,
                    continuousStartAngles, ikSeedAngles, out path))
                return true;

            path = BuildPathPlans(
                anim, data, s_posePositions, s_poseRotations, s_poseJ6Angles,
                continuousStartAngles, ikSeedAngles);

            if (!IsValidPath(path, anim.JointCount))
                return false;

            if (useCache && !ShouldForceDebugPlayback(anim, data))
            {
                clip?.CachedPlans.Store(
                    anim, data, s_posePositions, s_poseRotations, s_poseJ6Angles,
                    continuousStartAngles, ikSeedAngles, path);
            }

            return true;
        }

        public static PathMotionPlans GetOrBuildPathPlans(
            RobotArmClip clip,
            RobotArmAnim anim,
            RobotArmClipData data,
            Transform[] targets,
            TimelineStartFallback fallback,
            float[] continuousStartAngles = null,
            float[] ikSeedAngles = null)
        {
            TryBuildPathPlans(
                clip, anim, data, targets, fallback,
                out PathMotionPlans path,
                continuousStartAngles, ikSeedAngles,
                useCache: true);
            return path;
        }

        public static bool TrySamplePath(
            RobotArmAnim anim,
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

        public static void SamplePath(RobotArmAnim anim, in PathMotionPlans path, float normalizedTime)
        {
            if (!TrySamplePath(anim, path, normalizedTime))
            {
#if UNITY_EDITOR
                if (anim != null)
                    Debug.LogWarning(
                        $"[RobotArm] SamplePath 跳过：plans 无效或关节数不是六轴（JointCount={anim.JointCount}）",
                        anim);
#endif
            }
        }

        /// <summary>采样并写入关节角；失败时返回 false（不抛异常）。</summary>
        public static bool TrySample(RobotArmAnim anim, in MotionPlans plans, float normalizedTime)
        {
            return TryFollowSegment(
                anim, plans, normalizedTime,
                plans.StartPosition, plans.StartRotation,
                plans.EndPosition, plans.EndRotation);
        }

        public static PathMotionPlans BuildPathPlans(
            RobotArmAnim anim,
            RobotArmClipData data,
            List<Vector3> positions,
            List<Quaternion> rotations,
            List<float> j6Angles,
            float[] continuousStartAngles = null,
            float[] ikSeedAngles = null)
        {
            if (anim == null || data == null ||
                positions == null || rotations == null || j6Angles == null ||
                positions.Count < 2 ||
                rotations.Count != positions.Count ||
                j6Angles.Count != positions.Count ||
                anim.JointCount != RobotArmAnim.JointCountFixed)
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
                    positions[i], rotations[i], j6Angles[i],
                    positions[i + 1], rotations[i + 1], j6Angles[i + 1],
                    continuous, seed);
                continuous = segments[i].EndAngles;
                seed = null;
            }

            return new PathMotionPlans(segments);
        }

        /// <param name="continuousStartAngles">
        /// 前序 Clip 终点关节角。有值时直接作为首段起点角，避免从 Home 重解 IK 造成切换跳变。
    /// </param>
        /// <param name="ikSeedAngles">
        /// 仅在未使用 continuousStartAngles 时生效：作为起点 IK 初值（优先于 Home）。
    /// </param>
        public static MotionPlans BuildPlans(
            RobotArmAnim anim,
            RobotArmClipData data,
            Vector3 startPos,
            Quaternion startRot,
            float startJ6,
            Vector3 endPos,
            Quaternion endRot,
            float endJ6,
            float[] continuousStartAngles = null,
            float[] ikSeedAngles = null)
        {
            if (anim == null || data == null || anim.JointCount != RobotArmAnim.JointCountFixed)
            {
                return new MotionPlans(
                    System.Array.Empty<AxisMotionProfile.Plan>(),
                    System.Array.Empty<float>(),
                    System.Array.Empty<float>());
            }

            int n = RobotArmAnim.JointCountFixed;
            int j6 = RobotArmAnim.J6Index;
            var startAngles = new float[n];
            var endAngles = new float[n];
            bool driveJ6 = anim.KeepTipDown;
            startJ6 = anim.ClampAngle(j6, startJ6);
            endJ6 = anim.ClampAngle(j6, endJ6);
            // 朝下约束轴 = J5→TCP，依赖当前 J6；求解时必须带上该帧目标 J6，
            // 不能先按起点 J6 锁朝下再改写扭转（否则会留下可见倾角）。

            if (continuousStartAngles != null && continuousStartAngles.Length >= n)
            {
                for (int i = 0; i < n; i++)
                    startAngles[i] = anim.ClampAngle(i, continuousStartAngles[i]);
                if (driveJ6)
                    startAngles[j6] = startJ6;

                // 继承起点时仍须保证朝下，避免前序偏斜构型整段传播
                if (anim.KeepTipDown &&
                    !RobotArmIk.IsTipAxisAligned(anim, startAngles, anim.DownWorldAxis))
                {
                    RobotArmIk.ForceTipDown(anim, startAngles, anim.DownWorldAxis);
                    if (driveJ6)
                        startAngles[j6] = startJ6;
                    SolvePose(anim, startPos, startRot, startAngles, startAngles);
                    if (driveJ6)
                        startAngles[j6] = startJ6;
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

                if (driveJ6)
                    seed[j6] = startJ6;
                SolvePose(anim, startPos, startRot, seed, startAngles);
                if (driveJ6)
                    startAngles[j6] = startJ6;
            }

            // 沿笛卡尔直线链式 IK：中途按进度插值 J6，保证朝下几何与扭转一致。
            Ensure(ref s_buildCursor, n);
            CopyAngles(startAngles, s_buildCursor, n);
            if (driveJ6)
                s_buildCursor[j6] = startJ6;
            float tcpDist = Vector3.Distance(startPos, endPos);
            int steps = CartesianIkSteps(tcpDist, minStepsWhenMoving: 3);
            for (int s = 1; s <= steps; s++)
            {
                float u = s / (float)steps;
                Vector3 p = Vector3.Lerp(startPos, endPos, u);
                Quaternion r = Quaternion.Slerp(startRot, endRot, u);
                if (driveJ6)
                    s_buildCursor[j6] = anim.ClampAngle(
                        j6, Mathf.LerpAngle(startJ6, endJ6, u));
                SolvePose(anim, p, r, s_buildCursor, s_buildCursor);
                if (driveJ6)
                    s_buildCursor[j6] = anim.ClampAngle(
                        j6, Mathf.LerpAngle(startJ6, endJ6, u));
            }

            CopyAngles(s_buildCursor, endAngles, n);
            if (driveJ6)
            {
                startAngles[j6] = startJ6;
                endAngles[j6] = endJ6;
                // 终点再按 endJ6 收紧一次，避免末步 J6 与臂角不一致
                SolvePose(anim, endPos, endRot, endAngles, endAngles);
                endAngles[j6] = endJ6;
            }

            ResolveMotionParams(anim, data, out float speed, out float accel);

            // AxisMotionProfile 以「米」为路程；关节路程 = 角度差绝对值（度）
            var plans = new AxisMotionProfile.Plan[n];
            for (int i = 0; i < n; i++)
            {
                float delta = Mathf.Abs(
                    Mathf.DeltaAngle(startAngles[i], endAngles[i]));
                plans[i] = AxisMotionProfile.Build(delta, speed, accel);
            }

            AxisMotionProfile.Plan cartesian = BuildCartesianPlan(anim, data, tcpDist);
            return new MotionPlans(
                plans, startAngles, endAngles,
                startPos, endPos, startRot, endRot, cartesian);
        }

        /// <summary>
        /// 解析完整位姿链：默认姿态（Home）→ 点位链表 → 默认姿态（Home）。
    /// 点位均为途经/作业目标，不再把首点当作路径起点。
    /// 输出至少 2 个位姿才能构成移动。
    /// </summary>
        public static bool TryResolvePosePath(
            RobotArmAnim anim,
            RobotArmClipData data,
            Transform[] targets,
            TimelineStartFallback fallback,
            List<Vector3> positions,
            List<Quaternion> rotations,
            List<float> j6Angles,
            out Transform firstTarget)
        {
            positions.Clear();
            rotations.Clear();
            j6Angles.Clear();
            firstTarget = GetTarget(targets, 0);

            if (anim == null || data == null || data.WaypointCount <= 0)
                return false;

            // fallback 由调用方用于 continuousStartAngles；路径位姿始终以 Home 起止
            _ = fallback;

            if (!TryResolveHomePose(
                    anim, out Vector3 homePos, out Quaternion homeRot, out float homeJ6))
                return false;

            // 始终从默认姿态起步（前序 Clip 通常也回到 Home，continuous 角用于无跳变）
            positions.Add(homePos);
            rotations.Add(homeRot);
            j6Angles.Add(homeJ6);

            float currentJ6 = homeJ6;
            for (int i = 0; i < data.WaypointCount; i++)
            {
                Transform target = GetTarget(targets, i);
                if (!TryResolveWaypointPose(
                        anim, data, i, target,
                        out Vector3 pos, out Quaternion rot))
                    return false;

                currentJ6 = ResolveWaypointJ6(anim, data, i, currentJ6);
                positions.Add(pos);
                rotations.Add(rot);
                j6Angles.Add(currentJ6);
            }

            // 最后返回默认姿态
            positions.Add(homePos);
            rotations.Add(homeRot);
            j6Angles.Add(homeJ6);

            return positions.Count >= 2;
        }

        /// <summary>
        /// 解析点位第六轴角：勾选 UseJ6Angle 用配置值，否则继承前序角。
    /// </summary>
        public static float ResolveWaypointJ6(
            RobotArmAnim anim,
            RobotArmClipData data,
            int index,
            float inheritJ6)
        {
            RobotArmWaypoint wp = data != null ? data.GetWaypoint(index) : null;
            if (wp != null && wp.UseJ6Angle)
                return anim != null
                    ? anim.ClampAngle(RobotArmAnim.J6Index, wp.J6Angle)
                    : wp.J6Angle;
            return anim != null
                ? anim.ClampAngle(RobotArmAnim.J6Index, inheritJ6)
                : inheritJ6;
        }

        public static float ResolveInheritedJ6(
            RobotArmAnim anim,
            TimelineStartFallback fallback)
        {
            if (fallback.HasJointAngles &&
                fallback.JointAngles.Length > RobotArmAnim.J6Index)
            {
                float j6 = fallback.JointAngles[RobotArmAnim.J6Index];
                return anim != null ? anim.ClampAngle(RobotArmAnim.J6Index, j6) : j6;
            }

            if (anim != null && anim.HasHome &&
                anim.HomeAngles != null &&
                anim.HomeAngles.Length > RobotArmAnim.J6Index)
            {
                return anim.ClampAngle(
                    RobotArmAnim.J6Index, anim.HomeAngles[RobotArmAnim.J6Index]);
            }

            return 0f;
        }

        public static bool TryResolveWaypointPose(
            RobotArmAnim anim,
            RobotArmClipData data,
            int index,
            Transform target,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            if (anim == null || data == null)
                return false;

            RobotArmWaypoint wp = data.GetWaypoint(index);
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

        /// <summary>默认姿态（Home）的 TCP 位姿与 J6 角。</summary>
        public static bool TryResolveHomePose(
            RobotArmAnim anim,
            out Vector3 position,
            out Quaternion rotation,
            out float j6Angle)
        {
            position = default;
            rotation = Quaternion.identity;
            j6Angle = 0f;
            if (anim == null || anim.JointCount != RobotArmAnim.JointCountFixed)
                return false;

            int n = RobotArmAnim.JointCountFixed;
            var angles = new float[n];
            if (anim.HasHome)
                anim.CopyHomeAnglesTo(angles);
            else
            {
                for (int i = 0; i < n; i++)
                    angles[i] = 0f;
            }

            RobotArmIk.Forward(anim, angles, out position, out rotation);
            j6Angle = anim.ClampAngle(RobotArmAnim.J6Index, angles[RobotArmAnim.J6Index]);
            return true;
        }

        public static bool TryResolveInheritedStart(
            RobotArmAnim anim,
            TimelineStartFallback fallback,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            if (anim == null)
                return false;

            if (fallback.HasValue)
            {
                position = fallback.Position;
                rotation = fallback.Rotation;
                return true;
            }

            return TryResolveHomePose(anim, out position, out rotation, out _);
        }

        /// <summary>
        /// 兼容旧逻辑：首 Target 或 Data.HasStartPoint。
    /// 路径一径 Home 起止，点位均为途经点。
    /// </summary>
        public static bool HasExplicitStart(RobotArmClipData data, Transform firstTarget) =>
            firstTarget != null || (data != null && data.HasStartPoint);

        /// <summary>
        /// 未指定起点时，取前序 RobotArmClip 的终点 TCP + 终点关节角（链式），保证切换连续。
    /// 由早到晚填充各 Clip 的 IK 缓存，避免每帧递归重解整条前序链。
    /// </summary>
        public static TimelineStartFallback ResolvePreviousClipEnd(
            RobotArmAnim anim,
            TimelineClip timelineClip,
            IExposedPropertyTable resolver)
        {
            if (timelineClip == null || anim == null)
                return TimelineStartFallback.None;

            var prevChain = new List<TimelineClip>(8);
            TimelineClip cursor = timelineClip;
            while (TryFindPreviousRobotArmClip(cursor, out TimelineClip previous))
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
                if (previous.asset is not RobotArmClip prevAsset || prevAsset.Data == null)
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
            RobotArmClipData data,
            TimelineStartFallback fallback,
            Transform firstTarget = null)
        {
            _ = data;
            _ = firstTarget;
            // 路径始终从 Home 起步；前序也回 Home 时用其终点角，避免切换跳变
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

        public static bool TryFindPreviousRobotArmClip(
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
                if (other == null || other == clip || other.asset is not RobotArmClip)
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
                JointAngles != null && JointAngles.Length >= RobotArmAnim.JointCountFixed;

            public TimelineStartFallback(
                Vector3 position,
                Quaternion rotation,
                float[] jointAngles = null)
                : this(position, rotation, jointAngles, copyAngles: true)
            {
            }

            /// <summary>引用已有角度数组（缓存命中热路径，避免每帧复制）。</summary>
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
                if (jointAngles != null && jointAngles.Length >= RobotArmAnim.JointCountFixed)
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
            if (!path.IsValid || jointCount != RobotArmAnim.JointCountFixed)
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
                   jointCount == RobotArmAnim.JointCountFixed &&
                   plans.StartAngles.Length >= jointCount &&
                   plans.EndAngles.Length >= jointCount &&
                   plans.Joints.Length >= jointCount;
        }

        /// <summary>
        /// 当前段跟随目标：优先用刚解析的点位 Transform 位姿，保证中间插值跟的是目标当前位置。
    /// </summary>
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

        /// <summary>
        /// 中间状态 = 跟随目标位姿插值，再 IK 跟踪该目标（与独立跟随同一套 SolveToTarget）。
    /// 近距离用当前关节角增量跟随；scrub / 大跨度则从段起点沿直线链式 IK。
    /// </summary>
        private static bool TryFollowSegment(
            RobotArmAnim anim,
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
            if (n != RobotArmAnim.JointCountFixed ||
                plans.StartAngles.Length < n ||
                plans.EndAngles.Length < n ||
                plans.Joints.Length < n)
                return false;

            float t = Mathf.Clamp01(normalizedTime) * plans.Duration;
            // 位置进度：笛卡尔优先；静止时只看 J1–J5，避免纯 J6 扭转拖着 TCP 进度走
            float u;
            if (!plans.Cartesian.IsStationary)
                u = AxisMotionProfile.ProgressAt(plans.Cartesian, t);
            else
            {
                u = 0f;
                int posJointMax = anim.KeepTipDown
                    ? RobotArmAnim.TipDownJointIndexFixed
                    : n - 1;
                for (int i = 0; i <= posJointMax; i++)
                    u = Mathf.Max(u, AxisMotionProfile.ProgressAt(plans.Joints[i], t));
            }

            Vector3 targetPos = Vector3.Lerp(startPos, endPos, u);
            Quaternion targetRot = Quaternion.Slerp(startRot, endRot, u);

            // J6 用自己的梯形进度；朝下几何依赖当前 J6，须带入本帧目标角求解
            float uJ6 = AxisMotionProfile.ProgressAt(plans.Joints[RobotArmAnim.J6Index], t);
            float targetJ6 = Mathf.LerpAngle(
                plans.StartAngles[RobotArmAnim.J6Index],
                plans.EndAngles[RobotArmAnim.J6Index],
                uJ6);

            Ensure(ref s_sampleAngles, n);
            Ensure(ref s_sampleSeed, n);

            if (!TryIncrementalFollow(
                    anim, targetPos, targetRot, s_sampleAngles, targetJ6))
            {
                FollowAlongLine(
                    anim, startPos, startRot, targetPos, targetRot,
                    plans.StartAngles, s_sampleAngles, targetJ6);
            }

            // 朝下为硬约束：先写入本帧 J6，再锁 J5→TCP（勿在锁后再改扭转）
            if (anim.KeepTipDown)
            {
                s_sampleAngles[RobotArmAnim.J6Index] =
                    anim.ClampAngle(RobotArmAnim.J6Index, targetJ6);
                RobotArmIk.ForceTipDown(anim, s_sampleAngles, anim.DownWorldAxis);
                s_sampleAngles[RobotArmAnim.J6Index] =
                    anim.ClampAngle(RobotArmAnim.J6Index, targetJ6);
            }

            anim.ApplyAngles(s_sampleAngles);
            return true;
        }

        private static bool TryIncrementalFollow(
            RobotArmAnim anim,
            Vector3 targetPos,
            Quaternion targetRot,
            float[] outAngles,
            float targetJ6)
        {
            int n = anim.JointCount;
            int j6 = RobotArmAnim.J6Index;
            Ensure(ref s_sampleSeed, n);
            anim.ReadCurrentAngles(s_sampleSeed);
            // 朝下：用本帧目标 J6 估末端，保证 J5→TCP 约束轴与最终扭转一致
            if (anim.KeepTipDown)
                s_sampleSeed[j6] = anim.ClampAngle(j6, targetJ6);

            RobotArmIk.Forward(anim, s_sampleSeed, out Vector3 tip, out _);
            float maxSqr = IncrementalFollowMaxMeters * IncrementalFollowMaxMeters;
            if ((tip - targetPos).sqrMagnitude > maxSqr)
                return false;

            SolvePose(anim, targetPos, targetRot, s_sampleSeed, outAngles);
            if (anim.KeepTipDown)
                outAngles[j6] = anim.ClampAngle(j6, targetJ6);

            RobotArmIk.Forward(anim, outAngles, out Vector3 reached, out _);
            float acceptSqr = IncrementalFollowAcceptMeters * IncrementalFollowAcceptMeters;
            if ((reached - targetPos).sqrMagnitude > acceptSqr)
                return false;

            // 朝下模式下增量跟随必须保住 J5→TCP 朝下，否则改走直线链式重解
            if (anim.KeepTipDown &&
                !RobotArmIk.IsTipAxisAligned(anim, outAngles, anim.DownWorldAxis))
                return false;

            return true;
        }

        private static void FollowAlongLine(
            RobotArmAnim anim,
            Vector3 startPos,
            Quaternion startRot,
            Vector3 targetPos,
            Quaternion targetRot,
            float[] startAngles,
            float[] outAngles,
            float targetJ6)
        {
            int n = anim.JointCount;
            int j6 = RobotArmAnim.J6Index;
            Ensure(ref s_sampleSeed, n);
            CopyAngles(startAngles, s_sampleSeed, n);
            float startJ6 = anim.KeepTipDown
                ? anim.ClampAngle(j6, startAngles[j6])
                : 0f;
            if (anim.KeepTipDown)
                s_sampleSeed[j6] = startJ6;

            int steps = CartesianIkSteps(Vector3.Distance(startPos, targetPos), minStepsWhenMoving: 1);
            for (int s = 1; s <= steps; s++)
            {
                float su = s / (float)steps;
                Vector3 p = Vector3.Lerp(startPos, targetPos, su);
                Quaternion r = Quaternion.Slerp(startRot, targetRot, su);
                if (anim.KeepTipDown)
                    s_sampleSeed[j6] = anim.ClampAngle(
                        j6, Mathf.LerpAngle(startJ6, targetJ6, su));
                SolvePose(anim, p, r, s_sampleSeed, s_sampleSeed);
                if (anim.KeepTipDown)
                    s_sampleSeed[j6] = anim.ClampAngle(
                        j6, Mathf.LerpAngle(startJ6, targetJ6, su));
            }

            CopyAngles(s_sampleSeed, outAngles, n);
            if (anim.KeepTipDown)
                outAngles[j6] = anim.ClampAngle(j6, targetJ6);
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
            RobotArmAnim arm,
            Vector3 targetPos,
            Quaternion targetRot,
            float[] seed,
            float[] outAngles)
        {
            // 末端朝下用 RobotArmAnim 控制；关闭时对齐点位 Transform 朝向（Clip 路径特有；跟随仅用位置）。
            bool keepTipDown = arm.KeepTipDown;
            bool constrainOrientation = !keepTipDown;

            RobotArmIk.SolveToTarget(
                arm, targetPos, seed, outAngles,
                keepTipDown,
                constrainOrientation,
                targetRot,
                downWorldAxis: arm.DownWorldAxis);
        }

        private static AxisMotionProfile.Plan BuildCartesianPlan(
            RobotArmAnim anim,
            RobotArmClipData data,
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
            RobotArmAnim anim,
            RobotArmClipData data,
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
                continuousStartAngles.Length >= RobotArmAnim.JointCountFixed)
            {
                seed = continuousStartAngles;
                length = continuousStartAngles.Length;
                return true;
            }

            if (ikSeedAngles != null && ikSeedAngles.Length >= RobotArmAnim.JointCountFixed)
            {
                seed = ikSeedAngles;
                length = ikSeedAngles.Length;
                return true;
            }

            seed = null;
            length = 0;
            return false;
        }

        private static int HashAnim(RobotArmAnim arm)
        {
            unchecked
            {
                int n = arm.JointCount;
                int h = n * 397;
                h = (h * 397) ^ 7; // J6 与朝下同帧求解，避免旧缓存残留倾斜
                h = (h * 397) ^ arm.IkIterations;
                h = (h * 397) ^ arm.IkPositionTolerance.GetHashCode();
                h = (h * 397) ^ HashVec(arm.ToolOffsetLocal);
                h = (h * 397) ^ HashVec(arm.ToolApproachLocal);
                h = (h * 397) ^ HashVec(arm.TipDownAxisLocal);
                h = (h * 397) ^ arm.TipDownJointIndex;
                h = (h * 397) ^ (arm.KeepTipDown ? 1 : 0);
                h = (h * 397) ^ HashVec(arm.DownWorldAxis);
                h = (h * 397) ^ (arm.HasHome ? 1 : 0);
                float[] home = arm.HomeAngles;
                for (int i = 0; i < n; i++)
                {
                    if (arm.TryGetJointSettings(i, out RobotArmAnim.JointSettings s) && s != null)
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
