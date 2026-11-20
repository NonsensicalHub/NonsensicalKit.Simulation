using System;
using NaughtyAttributes;
using UnityEngine;

namespace TemperatureVisualization
{
    public enum SliceAxis
    {
        XY = 0,
        XZ = 1,
        YZ = 2
    }

    public class TemperatureSliceRenderer : MonoBehaviour
    {
        private static readonly Quaternion s_RotXZ = Quaternion.Euler(90f, 0f, 0f);
        private static readonly Quaternion s_RotYZ = Quaternion.Euler(0f, 90f, 0f);

        [Label("切片材质")]
        [SerializeField] private Material m_SliceMaterial;

        [Label("显示 XY 切片")]
        [SerializeField] private bool m_ShowSliceXY;

        [Label("显示 XZ 切片")]
        [SerializeField] private bool m_ShowSliceXZ;

        [Label("显示 YZ 切片")]
        [SerializeField] private bool m_ShowSliceYZ;

        [Label("XY 切片位置")]
        [SerializeField] private float m_PositionXY = 0.5f;

        [Label("XZ 切片位置")]
        [SerializeField] private float m_PositionXZ = 0.5f;

        [Label("YZ 切片位置")]
        [SerializeField] private float m_PositionYZ = 0.5f;

        private Mesh m_QuadMesh;
        private Material m_MaterialInstance;
        private MaterialPropertyBlock m_PropertyBlock;
        private Texture m_LastTex;
        private Texture m_LastTexPrev;
        private Texture m_LastRamp;
        private float m_LastBlend = -1f;
        private float m_LastTempMin = float.NaN;
        private float m_LastTempMax = float.NaN;
        private float m_LastOpacity = -1f;
        private Matrix4x4 m_LastBoundsMatrix;
        private bool m_BoundsLayoutDirty = true;

        public event Action<SliceAxis, float> SlicePositionChanged;

        public bool ShowSliceXY { get => m_ShowSliceXY; set => m_ShowSliceXY = value; }
        public bool ShowSliceXZ { get => m_ShowSliceXZ; set => m_ShowSliceXZ = value; }
        public bool ShowSliceYZ { get => m_ShowSliceYZ; set => m_ShowSliceYZ = value; }

        public float PositionXY { get => m_PositionXY; set => SetSlicePosition(SliceAxis.XY, value); }
        public float PositionXZ { get => m_PositionXZ; set => SetSlicePosition(SliceAxis.XZ, value); }
        public float PositionYZ { get => m_PositionYZ; set => SetSlicePosition(SliceAxis.YZ, value); }

        public void SetSlicePosition(SliceAxis axis, float normalizedPosition, bool invokeEvent = true)
        {
            normalizedPosition = Mathf.Clamp01(normalizedPosition);
            switch (axis)
            {
                case SliceAxis.XY: m_PositionXY = normalizedPosition; break;
                case SliceAxis.XZ: m_PositionXZ = normalizedPosition; break;
                default: m_PositionYZ = normalizedPosition; break;
            }

            m_BoundsLayoutDirty = true;
            if (invokeEvent) SlicePositionChanged?.Invoke(axis, normalizedPosition);
        }

        public void Render(
            TemperatureFieldInterpolator interpolator,
            TemperatureColorRamp colorRamp,
            TemperatureVolumeBounds bounds,
            float tempMin,
            float tempMax,
            float opacity,
            byte sliceMask = 0xFF)
        {
            if (interpolator == null || colorRamp == null || bounds == null || sliceMask == 0) return;

            EnsureResources();

            Transform boundsTransform = bounds.transform;
            Vector3 localCenter = bounds.Center;
            Vector3 localSize = bounds.Size;
            Matrix4x4 localToWorld = boundsTransform.localToWorldMatrix;
            Matrix4x4 worldToLocal = boundsTransform.worldToLocalMatrix;

            if (m_LastBoundsMatrix != localToWorld || m_BoundsLayoutDirty)
            {
                m_MaterialInstance.SetMatrix("_WorldToVolume", worldToLocal);
                m_MaterialInstance.SetVector("_VolumeCenter", localCenter);
                m_MaterialInstance.SetVector("_VolumeSize", localSize);
                m_LastBoundsMatrix = localToWorld;
                m_BoundsLayoutDirty = false;
            }

            Texture tex = interpolator.CurrentTexture;
            Texture texPrev = interpolator.PreviousTexture;
            Texture ramp = colorRamp.RampTexture;
            float blend = interpolator.BlendFactor;

            if (m_LastTex != tex) { m_MaterialInstance.SetTexture("_TemperatureTex", tex); m_LastTex = tex; }
            if (m_LastTexPrev != texPrev) { m_MaterialInstance.SetTexture("_TemperatureTexPrev", texPrev); m_LastTexPrev = texPrev; }
            if (m_LastRamp != ramp) { m_MaterialInstance.SetTexture("_ColorRamp", ramp); m_LastRamp = ramp; }
            if (!Mathf.Approximately(m_LastBlend, blend)) { m_MaterialInstance.SetFloat("_Blend", blend); m_LastBlend = blend; }
            if (!Mathf.Approximately(m_LastTempMin, tempMin)) { m_MaterialInstance.SetFloat("_TempMin", tempMin); m_LastTempMin = tempMin; }
            if (!Mathf.Approximately(m_LastTempMax, tempMax)) { m_MaterialInstance.SetFloat("_TempMax", tempMax); m_LastTempMax = tempMax; }
            if (!Mathf.Approximately(m_LastOpacity, opacity)) { m_MaterialInstance.SetFloat("_Opacity", opacity); m_LastOpacity = opacity; }

            if ((sliceMask & 1) != 0)
            {
                DrawOrientedSlice(SliceAxis.XY, m_PositionXY, localToWorld, localCenter, localSize);
            }
            if ((sliceMask & 2) != 0)
            {
                DrawOrientedSlice(SliceAxis.XZ, m_PositionXZ, localToWorld, localCenter, localSize);
            }
            if ((sliceMask & 4) != 0)
            {
                DrawOrientedSlice(SliceAxis.YZ, m_PositionYZ, localToWorld, localCenter, localSize);
            }
        }

        public void InvalidateCache()
        {
            m_LastTex = null;
            m_LastTexPrev = null;
            m_LastRamp = null;
            m_LastBlend = -1f;
            m_LastTempMin = float.NaN;
            m_BoundsLayoutDirty = true;
            m_LastBoundsMatrix = Matrix4x4.zero;
        }

        private void DrawOrientedSlice(
            SliceAxis axis,
            float normalizedPosition,
            Matrix4x4 localToWorld,
            Vector3 localCenter,
            Vector3 localSize)
        {
            Vector3 localPos = localCenter;
            Vector3 localScale;
            Quaternion localRot;

            switch (axis)
            {
                case SliceAxis.XY:
                    localPos.z += (normalizedPosition - 0.5f) * localSize.z;
                    localScale = new Vector3(localSize.x, localSize.y, 1f);
                    localRot = Quaternion.identity;
                    break;
                case SliceAxis.XZ:
                    localPos.y += (normalizedPosition - 0.5f) * localSize.y;
                    localScale = new Vector3(localSize.x, localSize.z, 1f);
                    localRot = s_RotXZ;
                    break;
                default:
                    localPos.x += (normalizedPosition - 0.5f) * localSize.x;
                    localScale = new Vector3(localSize.z, localSize.y, 1f);
                    localRot = s_RotYZ;
                    break;
            }

            Matrix4x4 localMatrix = Matrix4x4.TRS(localPos, localRot, localScale);
            Matrix4x4 worldMatrix = localToWorld * localMatrix;

            m_PropertyBlock.SetFloat("_SliceAxis", (float)axis);
            m_PropertyBlock.SetFloat("_SlicePosition", normalizedPosition);
            Graphics.DrawMesh(
                m_QuadMesh,
                worldMatrix,
                m_MaterialInstance,
                gameObject.layer,
                null,
                0,
                m_PropertyBlock);
        }

        private void EnsureResources()
        {
            if (m_QuadMesh == null) m_QuadMesh = CreateQuadMesh();
            if (m_PropertyBlock == null) m_PropertyBlock = new MaterialPropertyBlock();
            if (m_MaterialInstance == null)
            {
                m_MaterialInstance = m_SliceMaterial != null
                    ? new Material(m_SliceMaterial)
                    : new Material(Shader.Find("TemperatureVisualization/Slice"));
            }
        }

        private void OnDestroy()
        {
            if (m_MaterialInstance != null)
            {
                if (Application.isPlaying) Destroy(m_MaterialInstance);
                else DestroyImmediate(m_MaterialInstance);
            }

            if (m_QuadMesh != null)
            {
                if (Application.isPlaying) Destroy(m_QuadMesh);
                else DestroyImmediate(m_QuadMesh);
            }
        }

        private static Mesh CreateQuadMesh()
        {
            var mesh = new Mesh { name = "TemperatureSliceQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f)
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
