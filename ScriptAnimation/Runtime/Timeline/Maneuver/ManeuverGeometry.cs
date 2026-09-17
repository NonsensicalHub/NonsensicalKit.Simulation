using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>二/三次贝塞尔与段长裁剪等机动几何共用工具。</summary>
    internal static class ManeuverGeometry
    {
        internal const int BezierLengthSteps = 16;

        internal static float TrimDistance(float segmentLength, float earlyDist)
            => Mathf.Min(earlyDist, Mathf.Max(0.01f, segmentLength * 0.49f));

        internal static Vector3 QuadBezierPoint(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * b + t * t * c;
        }

        internal static Vector3 QuadBezierTangent(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            Vector3 d = 2f * (1f - t) * (b - a) + 2f * t * (c - b);
            d.y = 0f;
            return d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward;
        }

        internal static float QuadBezierLength(Vector3 a, Vector3 b, Vector3 c)
        {
            float len = 0f;
            Vector3 prev = a;
            for (int i = 1; i <= BezierLengthSteps; i++)
            {
                float t = i / (float)BezierLengthSteps;
                Vector3 p = QuadBezierPoint(a, b, c, t);
                len += Vector3.Distance(prev, p);
                prev = p;
            }

            return len;
        }

        internal static Vector3 CubicBezierPoint(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float u = 1f - t;
            float uu = u * u;
            float tt = t * t;
            return uu * u * a + 3f * uu * t * b + 3f * u * tt * c + tt * t * d;
        }

        internal static Vector3 CubicBezierTangent(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float u = 1f - t;
            Vector3 dir =
                3f * u * u * (b - a) +
                6f * u * t * (c - b) +
                3f * t * t * (d - c);
            dir.y = 0f;
            return dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.forward;
        }

        internal static float CubicBezierLength(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            float len = 0f;
            Vector3 prev = a;
            for (int i = 1; i <= BezierLengthSteps; i++)
            {
                float t = i / (float)BezierLengthSteps;
                Vector3 p = CubicBezierPoint(a, b, c, d, t);
                len += Vector3.Distance(prev, p);
                prev = p;
            }

            return len;
        }
    }
}
