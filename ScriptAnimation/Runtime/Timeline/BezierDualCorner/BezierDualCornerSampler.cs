using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 双拐点贝塞尔弯：沿 Prev→CornerA 入弯，三次贝塞尔经 CornerA/CornerB，再直线到达 Next。
    /// </summary>
    public static class BezierDualCornerSampler
    {
        enum PhaseKind
        {
            Line,
            CubicBezier,
        }

        struct Phase
        {
            public PhaseKind kind;
            public Vector3 p0;
            public Vector3 p1;
            public Vector3 controlA;
            public Vector3 controlB;
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

        public static float ResolveEarlyDistance(BezierDualCornerClipData data, PathMoveActor actor)
        {
            _ = actor;
            if (data != null)
                return Mathf.Max(0.01f, data.EarlyTurnDistance);

            return 1f;
        }

        public static float ResolveMoveSpeed(BezierDualCornerClipData data, PathMoveActor actor)
        {
            _ = actor;
            if (data != null)
                return DurationUtility.SafeSpeed(data.MoveSpeed);

            return DurationUtility.SafeSpeed(2f);
        }

        public static bool TryBuildPlan(
            PathMoveActor actor,
            BezierDualCornerClipData data,
            PathNode cornerA,
            PathNode cornerB,
            PathNode prev,
            PathNode next,
            Vector3 incomingPos,
            Quaternion incomingRot,
            out Plan plan)
        {
            plan = default;
            if (data == null || actor == null)
                return false;
            if (cornerA == null || cornerB == null || prev == null || next == null)
                return false;

            Vector3 pathOffset = actor.PathOffset;
            Vector3 prevPos = prev.transform.position + pathOffset;
            Vector3 pivotA = cornerA.transform.position + pathOffset;
            Vector3 pivotB = cornerB.transform.position + pathOffset;
            Vector3 nextPos = next.transform.position + pathOffset;

            prevPos = actor.WithUpHeight(prevPos, pivotA);
            pivotA = actor.WithUpHeight(pivotA, pivotA);
            pivotB = actor.WithUpHeight(pivotB, pivotA);
            nextPos = actor.WithUpHeight(nextPos, pivotA);

            Vector3 inVec = actor.Flatten(pivotA - prevPos);
            Vector3 midVec = actor.Flatten(pivotB - pivotA);
            Vector3 outVec = actor.Flatten(nextPos - pivotB);
            if (inVec.sqrMagnitude < 1e-8f || midVec.sqrMagnitude < 1e-8f || outVec.sqrMagnitude < 1e-8f)
                return false;

            float lenIn = inVec.magnitude;
            float lenOut = outVec.magnitude;
            Vector3 dirIn = inVec / lenIn;
            Vector3 dirOut = outVec / lenOut;

            float early = ResolveEarlyDistance(data, actor);
            float trimIn = ManeuverGeometry.TrimDistance(lenIn, early);
            float trimOut = ManeuverGeometry.TrimDistance(lenOut, early);
            Vector3 entry = pivotA - dirIn * trimIn;
            Vector3 exit = pivotB + dirOut * trimOut;
            Vector3 nextTarget = pivotB + dirOut * lenOut;

            bool reverse = data.ReverseFacing;
            Vector3 faceOut = PathMoveSampler.FacingDirection(dirOut, reverse);
            Quaternion rotStart = actor.FlattenRotation(incomingRot, incomingRot);
            Quaternion rotEnd = actor.LookRotation(faceOut, rotStart);

            s_phases.Clear();
            Vector3 cur = actor.WithUpHeight(incomingPos, pivotA);
            entry = actor.WithUpHeight(entry, pivotA);
            exit = actor.WithUpHeight(exit, pivotA);
            nextTarget = actor.WithUpHeight(nextTarget, pivotA);

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

            float bezierLen = ManeuverGeometry.CubicBezierLength(entry, pivotA, pivotB, exit);
            s_phases.Add(new Phase
            {
                kind = PhaseKind.CubicBezier,
                p0 = entry,
                p1 = exit,
                controlA = pivotA,
                controlB = pivotB,
                rotStart = rotStart,
                rotEnd = rotEnd,
                length = bezierLen,
            });

            if (Vector3.Distance(exit, nextTarget) > 1e-4f)
            {
                s_phases.Add(new Phase
                {
                    kind = PhaseKind.Line,
                    p0 = exit,
                    p1 = nextTarget,
                    rotStart = rotEnd,
                    rotEnd = rotEnd,
                    length = Vector3.Distance(exit, nextTarget),
                });
            }

            plan = new Plan
            {
                Valid = s_phases.Count > 0,
                EndPosition = nextTarget,
                EndRotation = rotEnd,
            };
            return plan.Valid;
        }

        public static bool TryGetEndPose(
            PathMoveActor actor,
            BezierDualCornerClipData data,
            PathNode cornerA,
            PathNode cornerB,
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
                    actor, data, cornerA, cornerB, prev, next, incomingPos, incomingRot, out Plan plan))
                return false;

            position = plan.EndPosition;
            rotation = plan.EndRotation;
            return true;
        }

        public static float EstimateDuration(
            BezierDualCornerClipData data,
            PathMoveActor actor,
            PathNode cornerA,
            PathNode cornerB,
            PathNode prev,
            PathNode next,
            Vector3 incomingPos,
            Quaternion incomingRot)
        {
            if (!TryBuildPlan(
                    actor, data, cornerA, cornerB, prev, next, incomingPos, incomingRot, out _))
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
            BezierDualCornerClipData data,
            PathNode cornerA,
            PathNode cornerB,
            PathNode prev,
            PathNode next,
            Vector3 incomingPos,
            Quaternion incomingRot,
            float normalizedTime)
        {
            if (actor == null || data == null)
                return;

            if (!TryBuildPlan(
                    actor, data, cornerA, cornerB, prev, next, incomingPos, incomingRot, out _))
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
                        tr.position = ManeuverGeometry.CubicBezierPoint(
                            p.p0, p.controlA, p.controlB, p.p1, local);
                        Vector3 tangent = ManeuverGeometry.CubicBezierTangent(
                            p.p0, p.controlA, p.controlB, p.p1, local);
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
