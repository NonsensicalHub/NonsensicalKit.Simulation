using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>折线路径查询与采样工具。</summary>
    public static class PathQuery
    {
        public static float GetPolylineLength(IList<Vector3> points)
        {
            if (points == null || points.Count < 2)
                return 0f;

            float len = 0f;
            for (int i = 0; i < points.Count - 1; i++)
                len += Vector3.Distance(points[i], points[i + 1]);
            return len;
        }

        public static void BuildAccum(IList<Vector3> points, List<float> accum, out float totalLen)
        {
            accum.Clear();
            accum.Add(0f);
            totalLen = 0f;
            if (points == null || points.Count == 0)
                return;

            for (int i = 1; i < points.Count; i++)
            {
                totalLen += Vector3.Distance(points[i - 1], points[i]);
                accum.Add(totalLen);
            }
        }

        public static Vector3 SampleByNormalized(
            IList<Vector3> points, IList<float> accum, float totalLen, float normalized)
        {
            if (points == null || points.Count == 0)
                return Vector3.zero;
            if (points.Count == 1 || totalLen < 1e-6f)
                return points[points.Count - 1];

            float target = Mathf.Clamp01(normalized) * totalLen;
            for (int i = 0; i < accum.Count - 1; i++)
            {
                if (target <= accum[i + 1] || i == accum.Count - 2)
                {
                    float segLen = accum[i + 1] - accum[i];
                    float segT = segLen > 1e-6f ? (target - accum[i]) / segLen : 1f;
                    return Vector3.LerpUnclamped(points[i], points[i + 1], Mathf.Clamp01(segT));
                }
            }

            return points[points.Count - 1];
        }

        public static Vector3 TangentAtNormalized(
            IList<Vector3> points, IList<float> accum, float totalLen, float normalized,
            Vector3 up = default)
        {
            if (points == null || points.Count < 2 || totalLen < 1e-6f)
                return Vector3.forward;

            if (up.sqrMagnitude < 1e-8f)
                up = Vector3.up;

            float n = Mathf.Clamp01(normalized);
            // 弧长步进；终点处前向差分会塌成零向量，改用后向差分，避免回退到世界 +Z
            float delta = Mathf.Max(0.001f / totalLen, 1e-5f);
            Vector3 a;
            Vector3 b;
            if (n >= 1f - 1e-6f)
            {
                a = SampleByNormalized(points, accum, totalLen, Mathf.Max(0f, n - delta));
                b = SampleByNormalized(points, accum, totalLen, n);
            }
            else
            {
                a = SampleByNormalized(points, accum, totalLen, n);
                b = SampleByNormalized(points, accum, totalLen, Mathf.Min(1f, n + delta));
            }

            Vector3 dir = Vector3.ProjectOnPlane(b - a, up);
            if (dir.sqrMagnitude > 1e-8f)
                return dir.normalized;

            return SegmentDirectionAtNormalized(points, accum, totalLen, n, up);
        }

        /// <summary>取归一化进度所在折线段的水平方向；差分失效时的稳定回退。</summary>
        static Vector3 SegmentDirectionAtNormalized(
            IList<Vector3> points, IList<float> accum, float totalLen, float normalized, Vector3 up)
        {
            float target = Mathf.Clamp01(normalized) * totalLen;
            for (int i = 0; i < accum.Count - 1; i++)
            {
                if (target <= accum[i + 1] || i == accum.Count - 2)
                {
                    Vector3 seg = Vector3.ProjectOnPlane(points[i + 1] - points[i], up);
                    if (seg.sqrMagnitude > 1e-8f)
                        return seg.normalized;
                    break;
                }
            }

            for (int i = points.Count - 1; i > 0; i--)
            {
                Vector3 seg = Vector3.ProjectOnPlane(points[i] - points[i - 1], up);
                if (seg.sqrMagnitude > 1e-8f)
                    return seg.normalized;
            }

            return Vector3.forward;
        }
    }
}
