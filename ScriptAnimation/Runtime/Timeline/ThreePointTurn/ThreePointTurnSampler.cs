using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 窄道三点转向：前进 → 对侧倒车圆弧转 90° → 再前进回到开场点。
    /// 右转示例：沿开场车头前进，倒车落到左侧并转到朝右，再前进回到原点对准右侧货物。
    /// </summary>
    public static class ThreePointTurnSampler
    {
        const float TurnAngleDegrees = 90f;
        const float DefaultDegreesPerSecond = 90f;

        public struct Plan
        {
            public bool Valid;
            public Vector3 P0;
            public Vector3 P1;
            public Vector3 P2;
            public Vector3 P3;
            public Vector3 Icr;
            public Vector3 StartForward;
            public Quaternion RotStart;
            public Quaternion RotEnd;
            public float Distance;
            public float ArcLength;
            public float SignedYaw;
        }

        public static float ResolveDistance(ThreePointTurnClipData data, PathMoveActor actor)
        {
            _ = actor;
            if (data != null)
                return Mathf.Max(0.01f, data.ManeuverDistance);

            return 0.8f;
        }

        public static float ResolveMoveSpeed(ThreePointTurnClipData data, PathMoveActor actor)
        {
            _ = actor;
            if (data != null)
                return DurationUtility.SafeSpeed(data.MoveSpeed);

            return DurationUtility.SafeSpeed(2f);
        }

        public static float ResolveRotateSpeed(ThreePointTurnClipData data, PathMoveActor actor)
        {
            _ = actor;
            if (data != null)
                return DurationUtility.SafeSpeed(data.RotateSpeed);

            return DurationUtility.SafeSpeed(DefaultDegreesPerSecond);
        }

        public static bool TryBuildPlan(
            PathMoveActor actor,
            ThreePointTurnClipData data,
            Vector3 incomingPos,
            Quaternion incomingRot,
            out Plan plan)
        {
            plan = default;
            if (data == null)
                return false;

            float d = ResolveDistance(data, actor);
            Quaternion startRot = actor != null
                ? actor.FlattenRotation(incomingRot, incomingRot)
                : incomingRot;

            Vector3 fwd = actor != null
                ? actor.Flatten(startRot * actor.Forward)
                : Flatten(startRot * Vector3.forward);
            if (fwd.sqrMagnitude < 1e-8f)
                fwd = Vector3.forward;
            else
                fwd.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            if (right.sqrMagnitude < 1e-8f)
                right = Vector3.right;
            else
                right.Normalize();

            bool turnRight = data.Direction != ThreePointTurnDirection.Left;
            float signedYaw = turnRight ? TurnAngleDegrees : -TurnAngleDegrees;
            Vector3 reverseSide = turnRight ? -right : right;
            Vector3 targetFwd = turnRight ? right : -right;

            Vector3 p0 = incomingPos;
            Vector3 p1 = WithHeight(actor, p0 + fwd * d, p0);
            Vector3 icr = WithHeight(actor, p1 + reverseSide * d, p0);
            Vector3 offset0 = p1 - icr;
            offset0.y = 0f;
            Vector3 p2 = WithHeight(
                actor,
                icr + Quaternion.AngleAxis(signedYaw, Vector3.up) * offset0,
                p0);
            Vector3 p3 = WithHeight(actor, p2 + targetFwd * d, p0);

            Quaternion rotEnd = actor != null
                ? actor.LookRotation(targetFwd, startRot)
                : Quaternion.LookRotation(targetFwd, Vector3.up);

            plan = new Plan
            {
                Valid = true,
                P0 = p0,
                P1 = p1,
                P2 = p2,
                P3 = p3,
                Icr = icr,
                StartForward = fwd,
                RotStart = startRot,
                RotEnd = rotEnd,
                Distance = d,
                ArcLength = 0.5f * Mathf.PI * d,
                SignedYaw = signedYaw,
            };
            return true;
        }

        public static bool TryGetEndPose(
            PathMoveActor actor,
            ThreePointTurnClipData data,
            Vector3 incomingPos,
            Quaternion incomingRot,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (!TryBuildPlan(actor, data, incomingPos, incomingRot, out Plan plan))
                return false;

            position = plan.P3;
            rotation = plan.RotEnd;
            return true;
        }

        public static float EstimateDuration(ThreePointTurnClipData data, PathMoveActor actor)
        {
            if (data == null)
                return -1f;

            float d = ResolveDistance(data, actor);
            GetPhaseDurations(data, actor, d, 0.5f * Mathf.PI * d, out float d1, out float d2, out float d3);
            float total = d1 + d2 + d3;
            return total > 1e-6f ? total : 0.01f;
        }

        public static void Sample(
            PathMoveActor actor,
            ThreePointTurnClipData data,
            Plan plan,
            float normalizedTime)
        {
            if (actor == null || data == null || !plan.Valid)
                return;

            GetPhaseDurations(
                data, actor, plan.Distance, plan.ArcLength,
                out float d1, out float d2, out float d3);
            float total = d1 + d2 + d3;
            if (total < 1e-6f)
                total = 0.01f;

            AnimationCurve curve = data.MoveCurve != null && data.MoveCurve.length > 0
                ? data.MoveCurve
                : AnimationCurve.Linear(0f, 0f, 1f, 1f);

            float elapsed = curve.Evaluate(Mathf.Clamp01(normalizedTime)) * total;
            Transform tr = actor.MoverTransform;

            if (elapsed <= d1)
            {
                float u = d1 > 1e-5f ? Mathf.Clamp01(elapsed / d1) : 1f;
                tr.position = Vector3.Lerp(plan.P0, plan.P1, u);
                tr.rotation = plan.RotStart;
                return;
            }

            elapsed -= d1;
            if (elapsed <= d2)
            {
                float u = d2 > 1e-5f ? Mathf.Clamp01(elapsed / d2) : 1f;
                SampleReverseArc(actor, plan, u, tr);
                return;
            }

            elapsed -= d2;
            float v = d3 > 1e-5f ? Mathf.Clamp01(elapsed / d3) : 1f;
            tr.position = Vector3.Lerp(plan.P2, plan.P3, v);
            tr.rotation = plan.RotEnd;
        }

        static void SampleReverseArc(PathMoveActor actor, Plan plan, float u, Transform tr)
        {
            u = Mathf.Clamp01(u);
            Vector3 offset0 = plan.P1 - plan.Icr;
            offset0.y = 0f;
            Quaternion yaw = Quaternion.AngleAxis(plan.SignedYaw * u, Vector3.up);
            Vector3 pos = plan.Icr + yaw * offset0;
            tr.position = WithHeight(actor, pos, plan.P0);

            Vector3 heading = yaw * plan.StartForward;
            tr.rotation = actor.LookRotation(heading, plan.RotStart);
        }

        static void GetPhaseDurations(
            ThreePointTurnClipData data,
            PathMoveActor actor,
            float distance,
            float arcLength,
            out float forwardDur,
            out float reverseDur,
            out float exitDur)
        {
            float moveSpeed = ResolveMoveSpeed(data, actor);
            float rotSpeed = ResolveRotateSpeed(data, actor);
            forwardDur = DurationUtility.TimeForDistance(distance, moveSpeed);
            exitDur = forwardDur;
            float arcMove = DurationUtility.TimeForDistance(arcLength, moveSpeed);
            float arcRot = DurationUtility.TimeForAngle(TurnAngleDegrees, rotSpeed);
            reverseDur = Mathf.Max(arcMove, arcRot);
        }

        static Vector3 Flatten(Vector3 direction)
        {
            direction.y = 0f;
            return direction;
        }

        static Vector3 WithHeight(PathMoveActor actor, Vector3 pos, Vector3 reference)
        {
            if (actor != null)
                return actor.WithUpHeight(pos, reference);
            pos.y = reference.y;
            return pos;
        }
    }
}
