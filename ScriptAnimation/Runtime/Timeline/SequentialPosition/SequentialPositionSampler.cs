using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 按绝对时间在组件路点上瞬移采样；总时长为各点停留间隔之和。
    /// </summary>
    public static class SequentialPositionSampler
    {
        public static int GetWaypointCount(SequentialPositionAnim anim)
        {
            return anim?.Waypoints != null ? anim.Waypoints.Length : 0;
        }

        public static float GetInterval(SequentialPositionAnim anim, int index)
        {
            float fallback = anim != null ? Mathf.Max(0f, anim.DefaultInterval) : 0f;
            if (anim?.Intervals == null || index < 0 || index >= anim.Intervals.Length)
                return fallback;
            return Mathf.Max(0f, anim.Intervals[index]);
        }

        /// <summary>
        /// 估算总时长。无路点或间隔全为 0 时返回 0（不注入默认占位时长）。
    /// </summary>
        public static float EstimateDuration(SequentialPositionAnim anim)
        {
            int count = GetWaypointCount(anim);
            if (count <= 0)
                return 0f;

            float total = 0f;
            for (int i = 0; i < count; i++)
                total += GetInterval(anim, i);

            return total;
        }

        public static bool HasValidDuration(SequentialPositionAnim anim)
            => EstimateDuration(anim) > 1e-6f;

        /// <summary>
        /// 根据 Clip 内本地时间求当前应停留的路点下标；无效数据返回 -1。
    /// </summary>
        public static int ResolveIndex(SequentialPositionAnim anim, float localTime)
        {
            int count = GetWaypointCount(anim);
            if (count <= 0)
                return -1;

            float t = Mathf.Max(0f, localTime);
            float acc = 0f;
            for (int i = 0; i < count; i++)
            {
                float hold = GetInterval(anim, i);
                acc += hold;
                if (t < acc || i == count - 1)
                    return i;
            }

            return count - 1;
        }

        public static bool TryResolvePosition(
            SequentialPositionAnim anim,
            float localTime,
            out Vector3 position,
            out int index)
        {
            position = Vector3.zero;
            index = ResolveIndex(anim, localTime);
            if (index < 0 || anim?.Waypoints == null)
                return false;

            Transform waypoint = anim.Waypoints[index];
            if (waypoint == null)
                return false;

            position = waypoint.position;
            return true;
        }

        public static void Sample(SequentialPositionAnim anim, float localTime)
        {
            if (anim == null)
                return;

            if (!TryResolvePosition(anim, localTime, out Vector3 position, out _))
            {
                anim.SamplePose(visible: false, worldPosition: null);
                return;
            }

            anim.SamplePose(visible: true, worldPosition: position);
        }
    }
}
