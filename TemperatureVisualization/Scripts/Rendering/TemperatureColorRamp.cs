using NaughtyAttributes;
using UnityEngine;

namespace TemperatureVisualization
{
    /// <summary>
    /// 将温度 Gradient 烘焙为 1D 颜色贴图，供 Shader 采样。
    /// </summary>
    [ExecuteAlways]
    public class TemperatureColorRamp : MonoBehaviour
    {
        [Label("色标 Gradient")]
        [SerializeField] private Gradient m_Gradient = CreateDefaultGradient();

        [Label("烘焙贴图宽度")]
        [SerializeField] private int m_TextureWidth = 256;

        [Label("预设色标列表")]
        [SerializeField] private Gradient[] m_Presets;

        private Texture2D m_RampTexture;
        private Color32[] m_PixelBuffer;

        public Texture2D RampTexture
        {
            get
            {
                EnsureTexture();
                return m_RampTexture;
            }
        }

        public Gradient Gradient
        {
            get => m_Gradient;
            set
            {
                m_Gradient = value;
                Bake();
            }
        }

        private void Awake()
        {
            EnsurePresets();
        }

        private void OnEnable()
        {
            EnsurePresets();
            Bake();
        }

        private void OnDestroy()
        {
            if (m_RampTexture == null) return;
            if (Application.isPlaying) Destroy(m_RampTexture);
            else DestroyImmediate(m_RampTexture);
        }

        public void ApplyPreset(int index)
        {
            EnsurePresets();
            if (index < 0 || index >= m_Presets.Length) return;
            m_Gradient = DuplicateGradient(m_Presets[index]);
            Bake();
        }

        public int PresetCount
        {
            get
            {
                EnsurePresets();
                return m_Presets.Length;
            }
        }

        public void Bake()
        {
            EnsureTexture();
            int w = m_TextureWidth;
            if (m_PixelBuffer == null || m_PixelBuffer.Length != w)
                m_PixelBuffer = new Color32[w];

            float inv = w <= 1 ? 0f : 1f / (w - 1);
            for (int i = 0; i < w; i++)
                m_PixelBuffer[i] = m_Gradient.Evaluate(i * inv);

            m_RampTexture.SetPixels32(m_PixelBuffer);
            m_RampTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }

        public Color Evaluate(float normalizedTemperature)
        {
            return m_Gradient.Evaluate(Mathf.Clamp01(normalizedTemperature));
        }

        private void EnsurePresets()
        {
            if (m_Presets != null && m_Presets.Length > 0) return;
            m_Presets = new[]
            {
                CreateDefaultGradient(),
                CreateThermalGradient(),
                CreateGrayscaleGradient()
            };
        }

        private void EnsureTexture()
        {
            if (m_RampTexture != null && m_RampTexture.width == m_TextureWidth) return;

            if (m_RampTexture != null)
            {
                if (Application.isPlaying) Destroy(m_RampTexture);
                else DestroyImmediate(m_RampTexture);
            }

            m_RampTexture = new Texture2D(m_TextureWidth, 1, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
        }

        public static Gradient CreateDefaultGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.1f, 0.2f, 0.9f), 0f),
                    new GradientColorKey(new Color(0.1f, 0.9f, 0.95f), 0.2f),
                    new GradientColorKey(new Color(0.2f, 0.9f, 0.2f), 0.4f),
                    new GradientColorKey(new Color(0.95f, 0.95f, 0.1f), 0.6f),
                    new GradientColorKey(new Color(1f, 0.5f, 0.1f), 0.8f),
                    new GradientColorKey(new Color(0.9f, 0.1f, 0.5f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.15f, 0f),
                    new GradientAlphaKey(0.55f, 0.5f),
                    new GradientAlphaKey(0.9f, 1f)
                });
            return gradient;
        }

        private static Gradient DuplicateGradient(Gradient source)
        {
            var copy = new Gradient();
            copy.SetKeys(source.colorKeys, source.alphaKeys);
            copy.mode = source.mode;
            return copy;
        }

        private void Reset()
        {
            m_Gradient = CreateDefaultGradient();
            m_Presets = new[]
            {
                CreateDefaultGradient(),
                CreateThermalGradient(),
                CreateGrayscaleGradient()
            };
        }

        private static Gradient CreateThermalGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.black, 0f),
                    new GradientColorKey(Color.blue, 0.25f),
                    new GradientColorKey(Color.red, 0.75f),
                    new GradientColorKey(Color.yellow, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.2f, 0f),
                    new GradientAlphaKey(0.8f, 1f)
                });
            return gradient;
        }

        private static Gradient CreateGrayscaleGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.black, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.3f, 0f),
                    new GradientAlphaKey(0.9f, 1f)
                });
            return gradient;
        }
    }
}
