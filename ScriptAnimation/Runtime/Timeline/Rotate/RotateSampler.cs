using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 原地旋转：默认维持进入 Clip 时的落点位置；
    /// 「仅旋转」模式下不写位置。朝向 = RotationFromYaw(起始偏航角+ 有符号角 × 进度)。
    /// </summary>
    public static class RotateSampler
    {
        const float DefaultDegreesPerSecond = 90f;

        public static bool HoldsPosition(RotateClipData data)
            => data == null || data.PositionMode == RotatePositionMode.HoldPosition;

        public static float ResolveSpeed(RotateClipData data, PathMoveActor actor)
        {
            _ = actor;
            if (data != null)
                return DurationUtility.SafeSpeed(data.RotateSpeed);

            return DurationUtility.SafeSpeed(DefaultDegreesPerSecond);
        }

        /// <summary>顺时针为正（与 Unity 绕 +Y 一致）。</summary>
        public static float GetSignedAngle(RotateClipData data)
        {
            if (data == null)
                return 0f;

            float mag = Mathf.Max(0f, data.AngleDegrees);
            return data.Direction == RotateDirection.Clockwise ? mag : -mag;
        }

        public static float EstimateDuration(RotateClipData data, PathMoveActor actor)
        {
            if (data == null)
                return -1f;

            float angle = Mathf.Abs(GetSignedAngle(data));
            float speed = ResolveSpeed(data, actor);
            float duration = DurationUtility.TimeForAngle(angle, speed);
            return duration > 1e-6f ? duration : 0.01f;
        }

        /// <summary>按 clip 归一化时间求当前偏航角（度）。</summary>
        public static float EvaluateYaw(RotateClipData data, float normalizedTime)
        {
            if (data == null)
                return 0f;

            float progress = EvaluateProgress(data, normalizedTime);
            return data.StartYawDegrees + GetSignedAngle(data) * progress;
        }

        public static Quaternion EvaluateRotation(
            PathMoveActor actor,
            RotateClipData data,
            float normalizedTime)
        {
            float yaw = EvaluateYaw(data, normalizedTime);
            if (actor != null)
                return actor.RotationFromYawDegrees(yaw, Quaternion.identity);
            return Quaternion.Euler(0f, yaw, 0f);
        }

        public static Quaternion EvaluateEndRotation(PathMoveActor actor, RotateClipData data)
        {
            return EvaluateRotation(actor, data, 1f);
        }

        public static Quaternion EvaluateStartRotation(PathMoveActor actor, RotateClipData data)
        {
            return EvaluateRotation(actor, data, 0f);
        }

        public static void Sample(
            PathMoveActor actor,
            RotateClipData data,
            float normalizedTime)
        {
            if (actor == null || data == null)
                return;

            Transform tr = actor.MoverTransform;
            tr.rotation = EvaluateRotation(actor, data, normalizedTime);
        }

        public static void Sample(
            PathMoveActor actor,
            RotateClipData data,
            Vector3 position,
            float normalizedTime)
        {
            if (actor == null || data == null)
                return;

            Transform tr = actor.MoverTransform;
            if (HoldsPosition(data))
                tr.position = position;
            tr.rotation = EvaluateRotation(actor, data, normalizedTime);
        }

        static float EvaluateProgress(RotateClipData data, float normalizedTime)
        {
            float t = Mathf.Clamp01(normalizedTime);
            AnimationCurve curve = data.RotateCurve != null && data.RotateCurve.length > 0
                ? data.RotateCurve
                : AnimationCurve.Linear(0f, 0f, 1f, 1f);
            return curve.Evaluate(t);
        }
    }
}
