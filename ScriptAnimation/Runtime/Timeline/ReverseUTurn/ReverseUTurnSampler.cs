using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 倒车掉头：入开→倒车旋转 →二次贝塞尔出弯至下一段提前点。
    /// </summary>
    public static class ReverseUTurnSampler
    {
        enum PhaseKind
        {
            Line,
            Bezier,
            ReverseRotate,
        }

        struct Phase
        {
            public PhaseKind kind;
            public Vector3 p0;
            public Vector3 p1;
            public Vector3 control;
            public Quaternion rotStart;
            public Quaternion rotEnd;
            public float moveLength;
            public float rotateAngle;
        }

        static readonly List<Phase> s_phases = new List<Phase>(8);
        static float[] s_dur;

        public struct Plan
        {
            public bool Valid;
            public Vector3 EndPosition;
            public Quaternion EndRotation;
        }

        public static float ResolveBackDistance(ReverseUTurnClipData data, PathMoveActor actor)
        {
            _ = actor;
            if (data != null)
                return Mathf.Max(0.01f, data.BackDistance);

            return 0.8f;
        }

        public static float ResolveEarlyDistance(ReverseUTurnClipData data, PathMoveActor actor)
        {
            _ = actor;
            if (data != null)
                return Mathf.Max(0.01f, data.EarlyTurnDistance);

            return 1f;
        }

        public static float ResolveMoveSpeed(ReverseUTurnClipData data, PathMoveActor actor)
        {
            _ = actor;
            if (data != null)
                return DurationUtility.SafeSpeed(data.MoveSpeed);

            return DurationUtility.SafeSpeed(2f);
        }

        public static float ResolveRotateSpeed(ReverseUTurnClipData data, PathMoveActor actor)
        {
            _ = actor;
            if (data != null)
                return DurationUtility.SafeSpeed(data.RotateSpeed);

            return DurationUtility.SafeSpeed(90f);
        }

        public static bool TryBuildPlan(
            PathMoveActor actor,
            ReverseUTurnClipData data,
            PathNode corner,
            PathNode prev,
            PathNode next,
            Vector3 incomingPos,
            Quaternion incomingRot,
            out Plan plan)
        {
            plan = default;
            if (data == null || actor == null)
                return false;

            Vector3 pathOffset = actor.PathOffset;
            if (!CornerManeuverUtility.TryResolveAxes(
                    actor, corner, prev, next, pathOffset,
                    out Vector3 pivot, out Vector3 dirIn, out Vector3 dirOut,
                    out _, out float lenOut))
                return false;

            float backDist = ResolveBackDistance(data, actor);
            float early = ResolveEarlyDistance(data, actor);
            float trimOut = ManeuverGeometry.TrimDistance(lenOut, early);
            Vector3 exit = pivot + dirOut * trimOut;

            bool reverse = data.ReverseFacing;
            Vector3 faceOut = PathMoveSampler.FacingDirection(dirOut, reverse);
            Quaternion rotStart = actor.FlattenRotation(incomingRot, incomingRot);

            pivot = actor.WithUpHeight(pivot, pivot);
            exit = actor.WithUpHeight(exit, pivot);
            Vector3 cur = actor.WithUpHeight(incomingPos, pivot);

            s_phases.Clear();

            if (Vector3.Distance(cur, pivot) > 1e-4f)
            {
                s_phases.Add(new Phase
                {
                    kind = PhaseKind.Line,
                    p0 = cur,
                    p1 = pivot,
                    rotStart = rotStart,
                    rotEnd = rotStart,
                    moveLength = Vector3.Distance(cur, pivot),
                });
            }

            Vector3 backDir = -actor.Flatten(rotStart * actor.Forward);
            if (backDir.sqrMagnitude < 1e-8f)
                backDir = -dirIn;
            backDir.Normalize();

            Vector3 perp = PickPerpendicular(dirOut, actor.Flatten(rotStart * actor.Forward));
            Quaternion reverseEndRot = actor.LookRotation(perp, rotStart);
            Vector3 reverseEndPos = pivot + backDir * backDist;

            float reverseAngle = Quaternion.Angle(rotStart, reverseEndRot);
            s_phases.Add(new Phase
            {
                kind = PhaseKind.ReverseRotate,
                p0 = pivot,
                p1 = reverseEndPos,
                rotStart = rotStart,
                rotEnd = reverseEndRot,
                moveLength = backDist,
                rotateAngle = reverseAngle,
            });

            Quaternion bezierEndRot = actor.LookRotation(faceOut, reverseEndRot);
            float bezierLen = ManeuverGeometry.QuadBezierLength(reverseEndPos, pivot, exit);
            s_phases.Add(new Phase
            {
                kind = PhaseKind.Bezier,
                p0 = reverseEndPos,
                p1 = exit,
                control = pivot,
                rotStart = reverseEndRot,
                rotEnd = bezierEndRot,
                moveLength = bezierLen,
            });

            plan = new Plan
            {
                Valid = true,
                EndPosition = exit,
                EndRotation = bezierEndRot,
            };
            return true;
        }

        static Vector3 PickPerpendicular(Vector3 dirOut, Vector3 referenceForward)
        {
            Vector3 left = Vector3.Cross(Vector3.up, dirOut);
            if (left.sqrMagnitude < 1e-8f)
                return Vector3.Cross(dirOut, Vector3.right).normalized;

            left.Normalize();
            Vector3 right = -left;
            if (referenceForward.sqrMagnitude < 1e-8f)
                return left;

            referenceForward.y = 0f;
            if (referenceForward.sqrMagnitude < 1e-8f)
                return left;

            referenceForward.Normalize();
            return Vector3.Dot(referenceForward, left) >= Vector3.Dot(referenceForward, right)
                ? left
                : right;
        }

        public static bool TryGetEndPose(
            PathMoveActor actor,
            ReverseUTurnClipData data,
            PathNode corner,
            PathNode prev,
            PathNode next,
            Vector3 incomingPos,
            Quaternion incomingRot,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (!TryBuildPlan(
                    actor, data, corner, prev, next, incomingPos, incomingRot, out Plan plan))
                return false;

            position = plan.EndPosition;
            rotation = plan.EndRotation;
            return true;
        }

        public static float EstimateDuration(
            ReverseUTurnClipData data,
            PathMoveActor actor,
            PathNode corner,
            PathNode prev,
            PathNode next,
            Vector3 incomingPos,
            Quaternion incomingRot)
        {
            if (!TryBuildPlan(
                    actor, data, corner, prev, next, incomingPos, incomingRot, out _))
                return -1f;

            float moveSpeed = ResolveMoveSpeed(data, actor);
            float rotSpeed = ResolveRotateSpeed(data, actor);
            float total = 0f;
            for (int i = 0; i < s_phases.Count; i++)
            {
                Phase p = s_phases[i];
                float moveDur = p.moveLength > 1e-6f
                    ? DurationUtility.TimeForDistance(p.moveLength, moveSpeed)
                    : 0.01f;

                if (p.kind == PhaseKind.ReverseRotate && p.rotateAngle > 1f)
                {
                    float rotDur = DurationUtility.TimeForAngle(p.rotateAngle, rotSpeed);
                    total += Mathf.Max(moveDur, rotDur);
                }
                else
                    total += moveDur;
            }

            return Mathf.Max(0.01f, total);
        }

        public static void Sample(
            PathMoveActor actor,
            ReverseUTurnClipData data,
            PathNode corner,
            PathNode prev,
            PathNode next,
            Vector3 incomingPos,
            Quaternion incomingRot,
            float normalizedTime)
        {
            if (actor == null || data == null)
                return;

            if (!TryBuildPlan(
                    actor, data, corner, prev, next, incomingPos, incomingRot, out _))
                return;

            float moveSpeed = ResolveMoveSpeed(data, actor);
            float rotSpeed = ResolveRotateSpeed(data, actor);
            AnimationCurve curve = data.MoveCurve != null && data.MoveCurve.length > 0
                ? data.MoveCurve
                : AnimationCurve.Linear(0f, 0f, 1f, 1f);

            EnsureDurBuffer(s_phases.Count);
            float total = 0f;
            for (int i = 0; i < s_phases.Count; i++)
            {
                Phase p = s_phases[i];
                float moveDur = p.moveLength > 1e-6f
                    ? Mathf.Max(0.01f, DurationUtility.TimeForDistance(p.moveLength, moveSpeed))
                    : 0.01f;

                if (p.kind == PhaseKind.ReverseRotate && p.rotateAngle > 1f)
                {
                    float rotDur = DurationUtility.TimeForAngle(p.rotateAngle, rotSpeed);
                    s_dur[i] = Mathf.Max(moveDur, rotDur);
                }
                else
                    s_dur[i] = moveDur;

                total += s_dur[i];
            }

            float elapsed = curve.Evaluate(Mathf.Clamp01(normalizedTime)) * total;
            float cursor = 0f;
            Transform tr = actor.MoverTransform;
            bool reverse = data.ReverseFacing;

            for (int i = 0; i < s_phases.Count; i++)
            {
                float dur = s_dur[i];
                if (elapsed < cursor + dur || (dur <= 1e-8f && elapsed <= cursor + 1e-8f))
                {
                    float local = dur > 1e-5f ? (elapsed - cursor) / dur : 1f;
                    local = Mathf.Clamp01(local);
                    Phase p = s_phases[i];

                    switch (p.kind)
                    {
                        case PhaseKind.Line:
                            tr.position = Vector3.Lerp(p.p0, p.p1, local);
                            tr.rotation = p.rotEnd;
                            break;

                        case PhaseKind.ReverseRotate:
                            tr.position = Vector3.Lerp(p.p0, p.p1, local);
                            tr.rotation = Quaternion.Slerp(p.rotStart, p.rotEnd, local);
                            break;

                        default:
                            tr.position = ManeuverGeometry.QuadBezierPoint(p.p0, p.control, p.p1, local);
                            Vector3 tangent = ManeuverGeometry.QuadBezierTangent(p.p0, p.control, p.p1, local);
                            Vector3 faceDir = PathMoveSampler.FacingDirection(tangent, reverse);
                            if (faceDir.sqrMagnitude > 1e-8f)
                                tr.rotation = actor.LookRotation(faceDir, p.rotStart);
                            else
                                tr.rotation = Quaternion.Slerp(p.rotStart, p.rotEnd, local);
                            break;
                    }

                    return;
                }

                cursor += dur;
            }

            Phase last = s_phases[s_phases.Count - 1];
            tr.position = last.p1;
            tr.rotation = last.rotEnd;
        }

        static void EnsureDurBuffer(int count)
        {
            if (s_dur == null || s_dur.Length < count)
                s_dur = new float[count];
        }
    }
}
