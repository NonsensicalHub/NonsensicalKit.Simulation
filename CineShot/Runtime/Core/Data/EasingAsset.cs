using UnityEngine;

namespace CineShot.Core
{
    [CreateAssetMenu(menuName = "CineShot/缓动曲线", fileName = "Ease_")]
    public class EasingAsset : ScriptableObject
    {
        public enum EaseKind
        {
            [InspectorName("线性")] Linear = 0,
            [InspectorName("平滑步进")] SmoothStep = 1,
            [InspectorName("三次缓入缓出")] EaseInOutCubic = 2,
            [InspectorName("自定义曲线")] Custom = 3
        }

        public EaseKind kind = EaseKind.EaseInOutCubic;
        public AnimationCurve custom = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        const int k_LutSize = 64;
        readonly float[] m_Lut = new float[k_LutSize + 1];
        bool m_Ready;

        void OnEnable()
        {
            Rebuild();
        }

        public void Rebuild()
        {
            for (int i = 0; i <= k_LutSize; i++)
            {
                float x = i / (float)k_LutSize;
                m_Lut[i] = kind == EaseKind.Custom
                    ? (custom != null ? custom.Evaluate(x) : x)
                    : Preset(kind, x);
            }
            m_Ready = true;
        }

        public float Evaluate(float x)
        {
            if (!m_Ready)
                Rebuild();
            if (x <= 0f) return m_Lut[0];
            if (x >= 1f) return m_Lut[k_LutSize];
            float f = x * k_LutSize;
            int i = (int)f;
            return m_Lut[i] + (m_Lut[i + 1] - m_Lut[i]) * (f - i);
        }

        static float Preset(EaseKind k, float x)
        {
            if (k == EaseKind.Linear)
                return x;
            if (k == EaseKind.EaseInOutCubic)
                return x < 0.5f
                    ? 4f * x * x * x
                    : 1f - Mathf.Pow(-2f * x + 2f, 3f) * 0.5f;
            return x * x * (3f - 2f * x);
        }
    }
}
