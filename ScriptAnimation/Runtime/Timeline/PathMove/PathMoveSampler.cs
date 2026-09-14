using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// PathMove：纯函数式路径解析与采样。
    /// 路径位置由 StartNode→EndNode 决定；开场朝向由前序 Clip 结束朝向（incoming）决定。
    /// 节点可配置「经过时停留」（顶升移栽），仅当前后路径夹角超过
    /// <see cref="PauseTurnAngleThreshold"/> 时计入，时长并入 Clip 总时长。
    /// </summary>
    public static class PathMoveSampler
    {
        /// <summary>前后路径夹角超过此值才使用节点停留时长；直线经过不计等待。</summary>
        public const float PauseTurnAngleThreshold = 35f;

        static float[] s_segMoveDur;
        static float[] s_segRotDur;

        static void EnsureSegBuffers(int count)
        {
            if (s_segMoveDur == null || s_segMoveDur.Length < count)
                s_segMoveDur = new float[count];
            if (s_segRotDur == null || s_segRotDur.Length < count)
                s_segRotDur = new float[count];
        }

        public static bool TryResolveWorldPoints(
            PathNetwork network,
            PathNode startNode,
            PathNode endNode,
            PathMoveClipData data,
            List<Vector3> worldPoints,
            Vector3 pathOffset = default,
            List<float> pauseDurations = null)
        {
            worldPoints.Clear();
            pauseDurations?.Clear();
            if (data == null || network == null)
                return false;

            PathNode start = startNode;
            PathNode end = endNode;
            if (start == null || end == null)
                return false;

            if (!network.TryBuildWorldPoints(start, end, worldPoints, pauseDurations))
                return false;

            if (worldPoints.Count == 0)
                return false;

            if (worldPoints.Count == 1)
            {
                worldPoints.Add(worldPoints[0]);
                if (pauseDurations != null && pauseDurations.Count == 1)
                    pauseDurations.Add(0f);
            }

            if (data.DestinationOffset.sqrMagnitude > 1e-12f)
                worldPoints[worldPoints.Count - 1] += data.DestinationOffset;

            if (pathOffset.sqrMagnitude > 1e-12f)
            {
                for (int i = 0; i < worldPoints.Count; i++)
                    worldPoints[i] += pathOffset;
            }

            FilterPausesByTurnAngle(worldPoints, pauseDurations);
            return true;
        }

        /// <summary>
        /// 只保留换向点的停留：中间点前后段夹角超过阈值才保留 <see cref="PathNode.PauseDuration"/>。
        /// 起点、终点没有完整前后路径，不计等待。
        /// </summary>
        static void FilterPausesByTurnAngle(IList<Vector3> worldPoints, List<float> pauseDurations)
        {
            if (pauseDurations == null || worldPoints == null)
                return;

            int n = Mathf.Min(worldPoints.Count, pauseDurations.Count);
            for (int i = 0; i < n; i++)
            {
                if (pauseDurations[i] <= 0f)
                    continue;

                if (i <= 0 || i >= worldPoints.Count - 1)
                {
                    pauseDurations[i] = 0f;
                    continue;
                }

                Vector3 incoming = worldPoints[i] - worldPoints[i - 1];
                Vector3 outgoing = worldPoints[i + 1] - worldPoints[i];
                if (PathTurnAngle(incoming, outgoing) <= PauseTurnAngleThreshold)
                    pauseDurations[i] = 0f;
            }
        }

        static float PathTurnAngle(Vector3 incoming, Vector3 outgoing)
        {
            Vector3 inFlat = incoming;
            inFlat.y = 0f;
            Vector3 outFlat = outgoing;
            outFlat.y = 0f;
            if (inFlat.sqrMagnitude > 1e-8f && outFlat.sqrMagnitude > 1e-8f)
                return Vector3.Angle(inFlat, outFlat);

            if (incoming.sqrMagnitude > 1e-8f && outgoing.sqrMagnitude > 1e-8f)
                return Vector3.Angle(incoming, outgoing);

            return 0f;
        }

        public static float SumPauseDurations(IList<float> pauseDurations)
        {
            if (pauseDurations == null || pauseDurations.Count == 0)
                return 0f;

            float sum = 0f;
            for (int i = 0; i < pauseDurations.Count; i++)
                sum += Mathf.Max(0f, pauseDurations[i]);
            return sum;
        }

        static float PauseAt(IList<float> pauseDurations, int index)
        {
            if (pauseDurations == null || index < 0 || index >= pauseDurations.Count)
                return 0f;
            return Mathf.Max(0f, pauseDurations[index]);
        }

        public static Quaternion GetPathStartRotation(
            IList<Vector3> worldPoints, PathMoveActor actor = null, bool reverseFacing = false)
        {
            if (worldPoints == null || worldPoints.Count < 2)
                return Quaternion.identity;

            Vector3 dir = FacingDirection(worldPoints[1] - worldPoints[0], reverseFacing);
            if (actor != null)
                return actor.LookRotation(dir, Quaternion.identity);

            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-8f)
                return Quaternion.identity;
            return Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        public static bool TryGetPathEndPose(
            IList<Vector3> worldPoints, out Vector3 position, out Quaternion rotation,
            PathMoveActor actor = null, bool reverseFacing = false,
            PathMoveClipData data = null, Quaternion incomingRotation = default,
            IList<float> pauseDurations = null)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (worldPoints == null || worldPoints.Count < 2)
                return false;

            if (actor != null && data != null)
            {
                PathMoveMode mode = PathMoveModeUtility.ResolveMode(data, actor);
                position = worldPoints[worldPoints.Count - 1];

                if (mode == PathMoveMode.MoveOnly)
                {
                    rotation = actor.FlattenRotation(incomingRotation, incomingRotation);
                    return true;
                }

                if (mode == PathMoveMode.FaceWhileMove)
                {
                    var accum = new List<float>(worldPoints.Count);
                    PathQuery.BuildAccum(worldPoints, accum, out float totalLen);
                    rotation = EvaluateFaceWhileMoveRotation(
                        actor, data, worldPoints, accum, totalLen, 1f, incomingRotation,
                        pauseDurations);
                    return true;
                }

                Vector3 faceDir = FacingDirection(
                    worldPoints[worldPoints.Count - 1] - worldPoints[worldPoints.Count - 2],
                    reverseFacing);
                rotation = actor.LookRotation(
                    faceDir, GetPathStartRotation(worldPoints, actor, reverseFacing));
                return true;
            }

            position = worldPoints[worldPoints.Count - 1];
            Vector3 dir = FacingDirection(
                worldPoints[worldPoints.Count - 1] - worldPoints[worldPoints.Count - 2],
                reverseFacing);
            if (dir.sqrMagnitude < 1e-8f)
                rotation = GetPathStartRotation(worldPoints, reverseFacing: reverseFacing);
            else
                rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            return true;
        }

        public static Vector3 FacingDirection(Vector3 moveDirection, bool reverseFacing)
            => reverseFacing ? -moveDirection : moveDirection;

        static bool IsReverseFacing(PathMoveClipData data)
            => data != null && data.ReverseFacing;

        public static float EstimateDuration(
            PathMoveClipData data,
            PathMoveActor actor,
            IList<Vector3> worldPoints,
            Quaternion incomingRotation,
            IList<float> pauseDurations = null)
        {
            if (data == null || actor == null || worldPoints == null || worldPoints.Count < 2)
                return -1f;

            PathMoveMode mode = PathMoveModeUtility.ResolveMode(data, actor);
            float speed = DurationUtility.SafeSpeed(data.MoveSpeed);

            float pathLength = PathQuery.GetPolylineLength(worldPoints);
            float moveTime = pathLength < 1e-6f
                ? 0.01f
                : DurationUtility.TimeForDistance(pathLength, speed);

            float pauseTime = SumPauseDurations(pauseDurations);

            if (mode == PathMoveMode.MoveOnly || mode == PathMoveMode.FaceWhileMove)
                return Mathf.Max(0.01f, moveTime + pauseTime);

            float rotSpeed = PathMoveModeUtility.ResolveRotateSpeed(data, actor);
            bool reverse = IsReverseFacing(data);

            Quaternion facing = actor.FlattenRotation(
                incomingRotation, GetPathStartRotation(worldPoints, actor, reverse));
            int segments = worldPoints.Count - 1;
            float rotateTime = 0f;
            float moveTimeExact = 0f;
            for (int i = 0; i < segments; i++)
            {
                float segLen = Vector3.Distance(worldPoints[i], worldPoints[i + 1]);
                moveTimeExact += Mathf.Max(0.01f, DurationUtility.TimeForDistance(Mathf.Max(segLen, 0f), speed));

                Vector3 dir = FacingDirection(worldPoints[i + 1] - worldPoints[i], reverse);
                Vector3 flat = actor.Flatten(dir);
                if (flat.sqrMagnitude <= 1e-4f)
                    continue;

                Quaternion endRot = actor.LookRotation(dir, facing);
                float angle = Quaternion.Angle(facing, endRot);
                if (angle > 1f)
                    rotateTime += DurationUtility.TimeForAngle(angle, rotSpeed);
                facing = endRot;
            }

            return Mathf.Max(0.01f, moveTimeExact + rotateTime + pauseTime);
        }

        public static void Sample(
            PathMoveActor actor,
            PathMoveClipData data,
            IList<Vector3> worldPoints,
            IList<float> accum,
            float totalLen,
            float normalizedTime,
            Quaternion incomingRotation,
            IList<float> pauseDurations = null)
        {
            if (actor == null || data == null || worldPoints == null || worldPoints.Count == 0)
                return;

            PathMoveMode mode = PathMoveModeUtility.ResolveMode(data, actor);
            float t = Mathf.Clamp01(normalizedTime);
            AnimationCurve curve = data.MoveCurve != null && data.MoveCurve.length > 0
                ? data.MoveCurve
                : AnimationCurve.Linear(0f, 0f, 1f, 1f);

            switch (mode)
            {
                case PathMoveMode.MoveOnly:
                    SampleMoveOnly(
                        actor, data, worldPoints, accum, totalLen, t, curve, incomingRotation,
                        pauseDurations);
                    break;

                case PathMoveMode.FaceWhileMove:
                    SampleFaceWhileMove(
                        actor, data, worldPoints, accum, totalLen, t, curve, incomingRotation,
                        pauseDurations);
                    break;

                default:
                    SampleRotateThenMove(
                        actor, data, worldPoints, accum, totalLen, t, curve, incomingRotation,
                        pauseDurations);
                    break;
            }
        }

        static void SampleMoveOnly(
            PathMoveActor actor,
            PathMoveClipData data,
            IList<Vector3> worldPoints,
            IList<float> accum,
            float totalLen,
            float normalizedTime,
            AnimationCurve curve,
            Quaternion incomingRotation,
            IList<float> pauseDurations)
        {
            Transform tr = actor.MoverTransform;
            Quaternion rot = actor.FlattenRotation(incomingRotation, incomingRotation);
            float pauseSum = SumPauseDurations(pauseDurations);

            if (pauseSum <= 1e-6f || worldPoints.Count < 2 || totalLen < 1e-6f)
            {
                float progress = curve.Evaluate(normalizedTime);
                tr.position = PathQuery.SampleByNormalized(worldPoints, accum, totalLen, progress);
                tr.rotation = rot;
                return;
            }

            float speed = DurationUtility.SafeSpeed(data.MoveSpeed);
            float moveDuration = Mathf.Max(0.01f, DurationUtility.TimeForDistance(totalLen, speed));
            float total = moveDuration + pauseSum;
            float elapsed = Mathf.Clamp01(normalizedTime) * total;

            if (!TrySampleAlongPolylineWithPauses(
                    worldPoints, accum, totalLen, pauseDurations, speed, moveDuration, elapsed,
                    curve, out Vector3 pos, out _))
                pos = worldPoints[worldPoints.Count - 1];

            tr.position = pos;
            tr.rotation = rot;
        }

        static void SampleFaceWhileMove(
            PathMoveActor actor,
            PathMoveClipData data,
            IList<Vector3> worldPoints,
            IList<float> accum,
            float totalLen,
            float normalizedTime,
            AnimationCurve curve,
            Quaternion incomingRotation,
            IList<float> pauseDurations)
        {
            Transform tr = actor.MoverTransform;
            float pauseSum = SumPauseDurations(pauseDurations);
            if (pauseSum <= 1e-6f || worldPoints.Count < 2 || totalLen < 1e-6f)
            {
                float progress = curve.Evaluate(normalizedTime);
                tr.position = PathQuery.SampleByNormalized(worldPoints, accum, totalLen, progress);
                tr.rotation = EvaluateFaceWhileMoveRotation(
                    actor, data, worldPoints, accum, totalLen, normalizedTime, incomingRotation,
                    pauseDurations);
                return;
            }

            float speed = DurationUtility.SafeSpeed(data.MoveSpeed);
            float moveDuration = Mathf.Max(0.01f, DurationUtility.TimeForDistance(totalLen, speed));
            float total = moveDuration + pauseSum;
            float elapsed = Mathf.Clamp01(normalizedTime) * total;

            if (!TrySampleAlongPolylineWithPauses(
                    worldPoints, accum, totalLen, pauseDurations, speed, moveDuration, elapsed,
                    curve, out Vector3 pos, out float moveNormalized))
            {
                pos = worldPoints[worldPoints.Count - 1];
                moveNormalized = 1f;
            }

            tr.position = pos;
            tr.rotation = EvaluateFaceWhileMoveRotation(
                actor, data, worldPoints, accum, totalLen, moveNormalized, incomingRotation,
                pauseDurations: null);
        }

        static bool TrySampleAlongPolylineWithPauses(
            IList<Vector3> worldPoints,
            IList<float> accum,
            float totalLen,
            IList<float> pauseDurations,
            float speed,
            float moveDuration,
            float elapsed,
            AnimationCurve curve,
            out Vector3 position,
            out float moveNormalized)
        {
            position = worldPoints[0];
            moveNormalized = 0f;
            int segments = worldPoints.Count - 1;
            float cursor = 0f;
            float moveElapsed = 0f;

            float startPause = PauseAt(pauseDurations, 0);
            if (elapsed < cursor + startPause)
            {
                position = worldPoints[0];
                moveNormalized = 0f;
                return true;
            }

            cursor += startPause;

            for (int seg = 0; seg < segments; seg++)
            {
                float segLen = accum != null && accum.Count > seg + 1
                    ? accum[seg + 1] - accum[seg]
                    : Vector3.Distance(worldPoints[seg], worldPoints[seg + 1]);
                float segDur = Mathf.Max(0.01f, DurationUtility.TimeForDistance(Mathf.Max(segLen, 0f), speed));

                if (elapsed < cursor + segDur)
                {
                    float local = segDur > 1e-5f ? (elapsed - cursor) / segDur : 1f;
                    float u = curve != null ? Mathf.Clamp01(curve.Evaluate(Mathf.Clamp01(local))) : local;
                    position = Vector3.Lerp(worldPoints[seg], worldPoints[seg + 1], u);
                    moveElapsed += local * segDur;
                    moveNormalized = moveDuration > 1e-5f
                        ? Mathf.Clamp01(moveElapsed / moveDuration)
                        : 1f;
                    return true;
                }

                cursor += segDur;
                moveElapsed += segDur;

                float arrivePause = PauseAt(pauseDurations, seg + 1);
                if (elapsed < cursor + arrivePause)
                {
                    position = worldPoints[seg + 1];
                    moveNormalized = moveDuration > 1e-5f
                        ? Mathf.Clamp01(moveElapsed / moveDuration)
                        : 1f;
                    return true;
                }

                cursor += arrivePause;
            }

            position = worldPoints[worldPoints.Count - 1];
            moveNormalized = 1f;
            return true;
        }

        public static Quaternion EvaluateFaceWhileMoveRotation(
            PathMoveActor actor,
            PathMoveClipData data,
            IList<Vector3> worldPoints,
            IList<float> accum,
            float totalLen,
            float normalizedTime,
            Quaternion incomingRotation,
            IList<float> pauseDurations = null)
        {
            Quaternion startFacing = actor.FlattenRotation(
                incomingRotation, GetPathStartRotation(worldPoints, actor, IsReverseFacing(data)));

            if (totalLen < 1e-6f || worldPoints == null || worldPoints.Count < 2)
                return startFacing;

            AnimationCurve curve = data != null && data.MoveCurve != null && data.MoveCurve.length > 0
                ? data.MoveCurve
                : AnimationCurve.Linear(0f, 0f, 1f, 1f);

            float speed = data != null
                ? DurationUtility.SafeSpeed(data.MoveSpeed)
                : DurationUtility.SafeSpeed(2f);
            float moveDuration = Mathf.Max(0.01f, DurationUtility.TimeForDistance(totalLen, speed));
            float degPerSec = PathMoveModeUtility.ResolveRotateSpeed(data, actor);
            bool reverse = IsReverseFacing(data);

            float pauseSum = SumPauseDurations(pauseDurations);
            float endNt;
            if (pauseSum > 1e-6f)
            {
                float total = moveDuration + pauseSum;
                float elapsed = Mathf.Clamp01(normalizedTime) * total;
                TrySampleAlongPolylineWithPauses(
                    worldPoints, accum, totalLen, pauseDurations, speed, moveDuration, elapsed,
                    curve, out _, out endNt);
            }
            else
                endNt = Mathf.Clamp01(normalizedTime);

            int steps = Mathf.Clamp(Mathf.CeilToInt(moveDuration * 60f), 8, 240);
            Quaternion facing = startFacing;
            float prevNt = 0f;
            for (int i = 1; i <= steps; i++)
            {
                float nt = endNt * (i / (float)steps);
                float dt = (nt - prevNt) * moveDuration;
                prevNt = nt;

                float p = curve.Evaluate(nt);
                Vector3 dir = FacingDirection(
                    PathQuery.TangentAtNormalized(worldPoints, accum, totalLen, p, Vector3.up),
                    reverse);
                if (dir.sqrMagnitude <= 1e-8f)
                    continue;

                Quaternion look = actor.LookRotation(dir, facing);
                facing = Quaternion.RotateTowards(facing, look, degPerSec * dt);
            }

            return facing;
        }

        static void SampleRotateThenMove(
            PathMoveActor actor,
            PathMoveClipData data,
            IList<Vector3> worldPoints,
            IList<float> accum,
            float totalLen,
            float normalizedTime,
            AnimationCurve curve,
            Quaternion incomingRotation,
            IList<float> pauseDurations)
        {
            _ = totalLen;
            float speed = DurationUtility.SafeSpeed(data.MoveSpeed);
            float rotSpeed = PathMoveModeUtility.ResolveRotateSpeed(data, actor);
            bool reverse = IsReverseFacing(data);

            Quaternion startFacing = actor.FlattenRotation(
                incomingRotation, GetPathStartRotation(worldPoints, actor, reverse));

            int segmentCount = worldPoints.Count - 1;
            EnsureSegBuffers(segmentCount);
            float[] segMoveDur = s_segMoveDur;
            float[] segRotDur = s_segRotDur;
            float sumMove = 0f;
            float sumRot = 0f;
            Quaternion facing = startFacing;

            for (int i = 0; i < segmentCount; i++)
            {
                float segLen = accum[i + 1] - accum[i];
                segMoveDur[i] = Mathf.Max(0.01f, DurationUtility.TimeForDistance(Mathf.Max(segLen, 0f), speed));
                sumMove += segMoveDur[i];

                Vector3 dir = FacingDirection(worldPoints[i + 1] - worldPoints[i], reverse);
                Vector3 flat = actor.Flatten(dir);
                if (flat.sqrMagnitude <= 1e-4f)
                {
                    segRotDur[i] = 0f;
                    continue;
                }

                Quaternion endRot = actor.LookRotation(dir, facing);
                float angle = Quaternion.Angle(facing, endRot);
                segRotDur[i] = angle > 1f ? DurationUtility.TimeForAngle(angle, rotSpeed) : 0f;
                sumRot += segRotDur[i];
                facing = endRot;
            }

            float pauseTime = SumPauseDurations(pauseDurations);
            float total = Mathf.Max(0.01f, sumMove + sumRot + pauseTime);
            float elapsed = normalizedTime * total;

            float cursor = 0f;
            Transform tr = actor.MoverTransform;
            Quaternion rot = startFacing;

            float startPause = PauseAt(pauseDurations, 0);
            if (elapsed < cursor + startPause)
            {
                tr.position = worldPoints[0];
                tr.rotation = rot;
                return;
            }

            cursor += startPause;

            for (int seg = 0; seg < segmentCount; seg++)
            {
                Vector3 from = worldPoints[seg];
                Vector3 to = worldPoints[seg + 1];
                Vector3 dir = FacingDirection(to - from, reverse);
                Vector3 flat = actor.Flatten(dir);

                float rotDur = segRotDur[seg];
                Quaternion endRot = flat.sqrMagnitude > 1e-4f
                    ? actor.LookRotation(dir, rot)
                    : rot;

                if (elapsed < cursor + rotDur)
                {
                    float rt = rotDur > 1e-5f ? (elapsed - cursor) / rotDur : 1f;
                    tr.position = from;
                    tr.rotation = Quaternion.Slerp(rot, endRot, Mathf.Clamp01(rt));
                    return;
                }

                cursor += rotDur;
                rot = endRot;

                float moveDur = segMoveDur[seg];
                if (elapsed < cursor + moveDur)
                {
                    float local = (elapsed - cursor) / moveDur;
                    float u = Mathf.Clamp01(curve.Evaluate(Mathf.Clamp01(local)));
                    tr.position = Vector3.Lerp(from, to, u);
                    tr.rotation = endRot;
                    return;
                }

                cursor += moveDur;

                float arrivePause = PauseAt(pauseDurations, seg + 1);
                if (elapsed < cursor + arrivePause)
                {
                    tr.position = to;
                    tr.rotation = endRot;
                    return;
                }

                cursor += arrivePause;
            }

            tr.position = worldPoints[worldPoints.Count - 1];
            if (segmentCount > 0)
            {
                Vector3 lastDir = FacingDirection(
                    worldPoints[segmentCount] - worldPoints[segmentCount - 1], reverse);
                Vector3 lastFlat = actor.Flatten(lastDir);
                if (lastFlat.sqrMagnitude > 1e-4f)
                    tr.rotation = actor.LookRotation(lastDir, rot);
                else
                    tr.rotation = rot;
            }
            else
                tr.rotation = rot;
        }
    }
}
