using UnityEngine;

namespace CineShot.Core
{
    [CreateAssetMenu(menuName = "CineShot/样条资产", fileName = "CineSpline_")]
    public class CineSpline : ScriptableObject
    {
        public Vector3[] knots = new Vector3[]
        {
            new Vector3(-4f, 1.6f, -3f),
            new Vector3(-1f, 1.8f, -4f),
            new Vector3(1f, 1.8f, -4f),
            new Vector3(4f, 1.6f, -3f)
        };
        public float tension = 0.5f;

        Vector3[] m_ArcPos;
        float[] m_ArcDist;
        int m_ArcStamp = int.MinValue;

        public Vector3 Evaluate(float u)
        {
            if (knots == null || knots.Length == 0)
                return Vector3.zero;
            if (knots.Length == 1)
                return knots[0];

            u = Mathf.Clamp01(u);
            EnsureArc();
            float target = u * m_ArcDist[m_ArcDist.Length - 1];
            int hi = 1;
            int last = m_ArcDist.Length - 1;
            while (hi < last && m_ArcDist[hi] < target)
                hi++;
            int lo = hi - 1;
            float span = m_ArcDist[hi] - m_ArcDist[lo];
            float t = span > 1e-6f ? (target - m_ArcDist[lo]) / span : 0f;
            return Vector3.Lerp(m_ArcPos[lo], m_ArcPos[hi], t);
        }

        void EnsureArc()
        {
            int stamp = knots.Length * 397 ^ tension.GetHashCode();
            for (int i = 0; i < knots.Length; i++)
                stamp = stamp * 397 ^ knots[i].GetHashCode();
            if (stamp == m_ArcStamp && m_ArcPos != null)
                return;
            m_ArcStamp = stamp;
            const int steps = 12;
            int count = (knots.Length - 1) * steps + 1;
            if (m_ArcPos == null || m_ArcPos.Length != count)
            {
                m_ArcPos = new Vector3[count];
                m_ArcDist = new float[count];
            }
            m_ArcPos[0] = EvaluateUniform(0f);
            m_ArcDist[0] = 0f;
            float denom = count - 1;
            for (int i = 1; i < count; i++)
            {
                m_ArcPos[i] = EvaluateUniform(i / denom);
                m_ArcDist[i] = m_ArcDist[i - 1] + Vector3.Distance(m_ArcPos[i - 1], m_ArcPos[i]);
            }
        }

        Vector3 EvaluateUniform(float u)
        {
            int n = knots.Length;
            float scaled = Mathf.Clamp01(u) * (n - 1);
            int i = Mathf.Min((int)scaled, n - 2);
            float t = scaled - i;
            Vector3 p0 = knots[Mathf.Max(i - 1, 0)];
            Vector3 p1 = knots[i];
            Vector3 p2 = knots[i + 1];
            Vector3 p3 = knots[Mathf.Min(i + 2, n - 1)];
            return CatmullRom(p0, p1, p2, p3, t, tension);
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t, float tension)
        {
            float s = (1f - tension) * 0.5f;
            Vector3 m1 = (p2 - p0) * s;
            Vector3 m2 = (p3 - p1) * s;
            float t2 = t * t;
            float t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * p1
                 + (t3 - 2f * t2 + t) * m1
                 + (-2f * t3 + 3f * t2) * p2
                 + (t3 - t2) * m2;
        }
    }
}
