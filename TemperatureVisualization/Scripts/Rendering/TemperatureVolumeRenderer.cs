using NaughtyAttributes;
using UnityEngine;

namespace TemperatureVisualization
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class TemperatureVolumeRenderer : MonoBehaviour
    {
        [Label("体积材质")]
        [SerializeField] private Material m_VolumeMaterial;

        [Label("Raymarch 步数")]
        [SerializeField] private int m_StepCount = 96;

        [Label("密度缩放")]
        [SerializeField] private float m_DensityScale = 1.5f;

        [Label("边缘柔化")]
        [SerializeField] private float m_EdgeSoftness = 0.35f;

        [Label("噪声缩放")]
        [SerializeField] private float m_NoiseScale = 4f;

        private MeshRenderer m_Renderer;
        private Material m_MaterialInstance;
        private Texture m_LastTexture;
        private Texture m_LastRamp;
        private float m_LastBlend = -1f;
        private float m_LastTempMin = float.NaN;
        private float m_LastTempMax = float.NaN;
        private float m_LastOpacity = -1f;

        public int StepCount
        {
            get => m_StepCount;
            set => m_StepCount = Mathf.Clamp(value, 8, 128);
        }

        private void Awake()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (m_StepCount > 64) m_StepCount = 64;
#endif
            m_Renderer = GetComponent<MeshRenderer>();
            var filter = GetComponent<MeshFilter>();
            if (filter.sharedMesh == null) filter.sharedMesh = CreateCubeMesh();
        }

        private void OnDestroy()
        {
            if (m_MaterialInstance != null)
            {
                if (Application.isPlaying) Destroy(m_MaterialInstance);
                else DestroyImmediate(m_MaterialInstance);
            }
        }

        public void SetEnabled(bool enabled)
        {
            if (m_Renderer == null) m_Renderer = GetComponent<MeshRenderer>();
            if (m_Renderer != null) m_Renderer.enabled = enabled;
        }

        public void InvalidateMaterialCache()
        {
            m_LastTexture = null;
            m_LastRamp = null;
            m_LastBlend = -1f;
            m_LastTempMin = float.NaN;
        }

        public bool ApplySharedState(
            TemperatureFieldInterpolator interpolator,
            TemperatureColorRamp colorRamp,
            TemperatureVolumeBounds bounds,
            float tempMin,
            float tempMax,
            float opacity)
        {
            if (interpolator == null || colorRamp == null || bounds == null) return false;

            EnsureMaterial();

            bounds.ApplyVolumeTransform(transform);

            Texture currentTexture = interpolator.CurrentTexture;
            Texture ramp = colorRamp.RampTexture;
            float blend = interpolator.BlendFactor;

            bool changed = false;
            if (m_Renderer.sharedMaterial != m_MaterialInstance)
            {
                m_Renderer.sharedMaterial = m_MaterialInstance;
                changed = true;
            }

            if (m_LastTexture != currentTexture)
            {
                m_MaterialInstance.SetTexture("_TemperatureTex", currentTexture);
                m_MaterialInstance.SetTexture("_TemperatureTexPrev", interpolator.PreviousTexture);
                m_LastTexture = currentTexture;
                changed = true;
            }

            if (m_LastRamp != ramp)
            {
                m_MaterialInstance.SetTexture("_ColorRamp", ramp);
                m_LastRamp = ramp;
                changed = true;
            }

            if (!Mathf.Approximately(m_LastBlend, blend))
            {
                m_MaterialInstance.SetFloat("_Blend", blend);
                m_LastBlend = blend;
                changed = true;
            }

            if (!Mathf.Approximately(m_LastTempMin, tempMin))
            {
                m_MaterialInstance.SetFloat("_TempMin", tempMin);
                m_LastTempMin = tempMin;
                changed = true;
            }

            if (!Mathf.Approximately(m_LastTempMax, tempMax))
            {
                m_MaterialInstance.SetFloat("_TempMax", tempMax);
                m_LastTempMax = tempMax;
                changed = true;
            }

            if (!Mathf.Approximately(m_LastOpacity, opacity))
            {
                m_MaterialInstance.SetFloat("_Opacity", opacity);
                m_LastOpacity = opacity;
                changed = true;
            }

            return changed;
        }

        private void EnsureMaterial()
        {
            if (m_MaterialInstance != null) return;
            m_MaterialInstance = m_VolumeMaterial != null
                ? new Material(m_VolumeMaterial)
                : new Material(Shader.Find("TemperatureVisualization/VolumeRaymarch"));
            m_MaterialInstance.SetFloat("_StepCount", m_StepCount);
            m_MaterialInstance.SetFloat("_DensityScale", m_DensityScale);
            m_MaterialInstance.SetFloat("_EdgeSoftness", m_EdgeSoftness);
            m_MaterialInstance.SetFloat("_NoiseScale", m_NoiseScale);
        }

        private static Mesh CreateCubeMesh()
        {
            var mesh = new Mesh { name = "TemperatureVolumeCube" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f)
            };
            mesh.triangles = new[]
            {
                0, 2, 1, 0, 3, 2, 1, 2, 6, 1, 6, 5, 4, 5, 6, 4, 6, 7,
                0, 4, 7, 0, 7, 3, 0, 1, 5, 0, 5, 4, 3, 7, 6, 3, 6, 2
            };
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
