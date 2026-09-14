using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 贝塞尔直角弯：沿 Prev→Corner 入弯、二次贝塞尔进 Corner、出弯至 Corner→Next 上的提前点。
    /// </summary>
    public static class BezierCornerSampler
    {
        enum PhaseKind
        {
            Line,
            Bezier,
        }

        struct Phase
        {
            public PhaseKind kind;
            public Vector3 p0;
            public Vector3 p1;
            public Vector3 control;
            public Quaternion rotStart;
            public Quaternion rotEnd;
            public float length;
        }

        static readonly List<Phase> s_phases = new List<Phase>(8);
        static float[] s_dur;

        public struct Plan
        {
            public bool Valid;
            public Vector3 EndPosition;
            public Quaternion EndRotation;
        }

        public static float ResolveEarlyDistance(BezierCornerClipData data, PathMoveActor actor)
        {
            _ = actor;
            if (data != null)
                return Mathf.Max(0.01f, data.EarlyTurnDistance);

            return 1f;
        }

        public static float ResolveMoveSpeed(BezierCornerClipData data, PathMoveActor actor)
        {
            _ = actor;
            if (data != null)
                return DurationUtility.SafeSpeed(data.MoveSpeed);

            return DurationUtility.SafeSpeed(2f);
        }

        public static bool TryBuildPlan(
            PathMoveActor actor,
            BezierCornerClipData data,
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
                    out float lenIn, out float lenOut))
                return false;

            float early = ResolveEarlyDistance(data, actor);
            float trimIn = ManeuverGeometry.TrimDistance(lenIn, early);
            float trimOut = ManeuverGeometry.TrimDistance(lenOut, early);
            Vector3 entry = pivot - dirIn * trimIn;
            Vector3 exit = pivot + dirOut * trimOut;

            bool reverse = data.ReverseFacing;
            Vector3 faceOut = PathMoveSampler.FacingDirection(dirOut, reverse);
            Quaternion rotStart = actor.FlattenRotation(incomingRot, incomingRot);
            Quaternion rotEnd = actor.LookRotation(faceOut, rotStart);

            s_phases.Clear();
            Vector3 cur = actor.WithUpHeight(incomingPos, pivot);
            entry = actor.WithUpHeight(entry, pivot);
            exit = actor.WithUpHeight(exit, pivot);
            pivot = actor.WithUpHeight(pivot, pivot);

            if (Vector3.Distance(cur, entry) > 1e-4f)
            {
                s_phases.Add(new Phase
                {
                    kind = PhaseKind.Line,
                    p0 = cur,
                    p1 = entry,
                    rotStart = rotStart,
                    rotEnd = rotStart,
                    length = Vector3.Distance(cur, entry),
                });
            }

            float bezierLen = ManeuverGeometry.QuadBezierLength(entry, pivot, exit);
            s_phases.Add(new Phase
            {
                kind = PhaseKind.Bezier,
                p0 = entry,
                p1 = exit,
                control = pivot,
                rotStart = rotStart,
                rotEnd = rotEnd,
                length = bezierLen,
            });

            plan = new Plan
            {
                Valid = s_phases.Count > 0,
                EndPosition = exit,
                EndRotation = rotEnd,
            };
            return plan.Valid;
        }

        public static bool TryGetEndPose(
            PathMoveActor actor,
            BezierCornerClipData data,
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
            BezierCornerClipData data,
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

            float speed = ResolveMoveSpeed(data, actor);
            float total = 0f;
            for (int i = 0; i < s_phases.Count; i++)
            {
                float len = s_phases[i].length;
                total += len > 1e-6f
                    ? DurationUtility.TimeForDistance(len, speed)
                    : 0.01f;
            }

            return Mathf.Max(0.01f, total);
        }

        public static void Sample(
            PathMoveActor actor,
            BezierCornerClipData data,
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

            float speed = ResolveMoveSpeed(data, actor);
            AnimationCurve curve = data.MoveCurve != null && data.MoveCurve.length > 0
                ? data.MoveCurve
                : AnimationCurve.Linear(0f, 0f, 1f, 1f);

            EnsureDurBuffer(s_phases.Count);
            float total = 0f;
            for (int i = 0; i < s_phases.Count; i++)
            {
                float len = s_phases[i].length;
                s_dur[i] = len > 1e-6f
                    ? Mathf.Max(0.01f, DurationUtility.TimeForDistance(len, speed))
                    : 0.01f;
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
                    if (p.kind == PhaseKind.Line)
                    {
                        tr.position = Vector3.Lerp(p.p0, p.p1, local);
                        tr.rotation = p.rotEnd;
                    }
                    else
                    {
                        tr.position = ManeuverGeometry.QuadBezierPoint(p.p0, p.control, p.p1, local);
                        Vector3 tangent = ManeuverGeometry.QuadBezierTangent(p.p0, p.control, p.p1, local);
                        Vector3 faceDir = PathMoveSampler.FacingDirection(tangent, reverse);
                        if (faceDir.sqrMagnitude > 1e-8f)
                            tr.rotation = actor.LookRotation(faceDir, p.rotStart);
                        else
                            tr.rotation = Quaternion.Slerp(p.rotStart, p.rotEnd, local);
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
