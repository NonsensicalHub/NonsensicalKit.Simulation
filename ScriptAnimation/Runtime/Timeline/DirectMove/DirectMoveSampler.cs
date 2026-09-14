using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// DirectMove：A→B 直线插值，不寻路、不改朝向。
    /// 端点可为任意 Transform（通常来自 <see cref="ScriptAnimPoint"/>），或 Data 中的世界坐标。
    /// </summary>
    public static class DirectMoveSampler
    {
        public static bool TryResolveEndpoints(
            Transform startTarget,
            Transform endTarget,
            DirectMoveClipData data,
            out Vector3 start,
            out Vector3 end,
            Vector3 pathOffset = default)
        {
            start = Vector3.zero;
            end = Vector3.zero;
            if (data == null)
                return false;

            if (!TryResolvePoint(
                    startTarget, data.UseStartWorldPosition, data.StartWorldPosition, out start))
                return false;
            if (!TryResolvePoint(
                    endTarget, data.UseEndWorldPosition, data.EndWorldPosition, out end))
                return false;

            if (data.DestinationOffset.sqrMagnitude > 1e-12f)
                end += data.DestinationOffset;

            if (pathOffset.sqrMagnitude > 1e-12f)
            {
                start += pathOffset;
                end += pathOffset;
            }

            return true;
        }

        public static float EstimateDuration(
            DirectMoveClipData data,
            PathMoveActor actor,
            Vector3 start,
            Vector3 end)
        {
            if (data == null || actor == null)
                return -1f;

            float speed = DurationUtility.SafeSpeed(data.MoveSpeed);

            float distance = Vector3.Distance(start, end);
            return distance < 1e-6f
                ? 0.01f
                : DurationUtility.TimeForDistance(distance, speed);
        }

        public static void Sample(
            PathMoveActor actor,
            DirectMoveClipData data,
            Vector3 start,
            Vector3 end,
            float normalizedTime,
            Quaternion incomingRotation)
        {
            if (actor == null || data == null)
                return;

            Transform tr = actor.MoverTransform;
            float t = Mathf.Clamp01(normalizedTime);
            AnimationCurve curve = data.MoveCurve != null && data.MoveCurve.length > 0
                ? data.MoveCurve
                : AnimationCurve.Linear(0f, 0f, 1f, 1f);

            float progress = curve.Evaluate(t);
            tr.position = Vector3.LerpUnclamped(start, end, progress);
            // 不旋转：全程保持进入 Clip 时的朝向
            tr.rotation = incomingRotation;
        }

        private static bool TryResolvePoint(
            Transform target,
            bool useWorld,
            Vector3 worldPosition,
            out Vector3 position)
        {
            if (useWorld)
            {
                position = worldPosition;
                return true;
            }

            if (target == null)
            {
                position = Vector3.zero;
                return false;
            }

            position = target.position;
            return true;
        }
    }
}
