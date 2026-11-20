using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Rendering;

namespace TemperatureVisualization
{
    /// <summary>
    /// 可配置大小的立方体体积边界，作为插值与渲染的空间参照。
    /// 在 Scene 视图中选中后可像 BoxCollider 一样拖拽调整大小与位置。
    /// </summary>
    [ExecuteAlways]
    public class TemperatureVolumeBounds : MonoBehaviour
    {
        [Label("本地中心偏移")]
        [SerializeField] private Vector3 m_Center;

        [Label("体积尺寸")]
        [SerializeField] private Vector3 m_Size = new Vector3(10f, 6f, 8f);

        [Label("显示 Gizmo")]
        [SerializeField] private bool m_ShowGizmo = true;

        [Label("运行时显示")]
        [SerializeField] private bool m_ShowRuntimeGizmo;

        [Label("Scene 视图可编辑")]
        [SerializeField] private bool m_EditBoundsInScene = true;

        [Label("Gizmo 颜色")]
        [SerializeField] private Color m_GizmoColor = new Color(0.2f, 0.8f, 1f, 0.35f);

        private Bounds m_CachedBounds;
        private Vector3 m_CachedPosition;
        private Quaternion m_CachedRotation;
        private Vector3 m_CachedLossyScale;
        private Vector3 m_CachedCenter;
        private Vector3 m_CachedSize;
        private bool m_BoundsDirty = true;

        private static Material s_RuntimeLineMaterial;
        private static Mesh s_WireCubeMesh;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public Vector3 Center
        {
            get => m_Center;
            set
            {
                m_Center = value;
                m_BoundsDirty = true;
            }
        }

        public Vector3 Size
        {
            get => m_Size;
            set
            {
                m_Size = Vector3.Max(value, Vector3.one * 0.1f);
                m_BoundsDirty = true;
            }
        }

        public bool ShowGizmo
        {
            get => m_ShowGizmo;
            set => m_ShowGizmo = value;
        }

        public bool ShowRuntimeGizmo
        {
            get => m_ShowRuntimeGizmo;
            set => m_ShowRuntimeGizmo = value;
        }

        public bool EditBoundsInScene
        {
            get => m_EditBoundsInScene;
            set => m_EditBoundsInScene = value;
        }

        public Color GizmoColor
        {
            get => m_GizmoColor;
            set => m_GizmoColor = value;
        }

        public Bounds WorldBounds
        {
            get
            {
                if (m_BoundsDirty
                    || transform.position != m_CachedPosition
                    || transform.rotation != m_CachedRotation
                    || transform.lossyScale != m_CachedLossyScale
                    || m_Center != m_CachedCenter
                    || m_Size != m_CachedSize)
                {
                    Vector3 worldCenter = transform.TransformPoint(m_Center);
                    Vector3 worldSize = Vector3.Scale(m_Size, Abs(transform.lossyScale));
                    m_CachedBounds = new Bounds(worldCenter, worldSize);
                    m_CachedPosition = transform.position;
                    m_CachedRotation = transform.rotation;
                    m_CachedLossyScale = transform.lossyScale;
                    m_CachedCenter = m_Center;
                    m_CachedSize = m_Size;
                    m_BoundsDirty = false;
                }

                return m_CachedBounds;
            }
        }

        public void SetWorldBounds(Bounds worldBounds)
        {
            Vector3 localCenter = transform.InverseTransformPoint(worldBounds.center);
            Vector3 lossyScale = Abs(transform.lossyScale);
            Vector3 localSize = new Vector3(
                worldBounds.size.x / Mathf.Max(lossyScale.x, 1e-6f),
                worldBounds.size.y / Mathf.Max(lossyScale.y, 1e-6f),
                worldBounds.size.z / Mathf.Max(lossyScale.z, 1e-6f));

            m_Center = localCenter;
            m_Size = Vector3.Max(localSize, Vector3.one * 0.1f);
            m_BoundsDirty = true;
        }

        public Vector3 WorldToNormalized(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            return new Vector3(
                (local.x - m_Center.x) / Mathf.Max(m_Size.x, 1e-6f) + 0.5f,
                (local.y - m_Center.y) / Mathf.Max(m_Size.y, 1e-6f) + 0.5f,
                (local.z - m_Center.z) / Mathf.Max(m_Size.z, 1e-6f) + 0.5f);
        }

        public Vector3 NormalizedToLocal(Vector3 normalized)
        {
            return m_Center + new Vector3(
                (normalized.x - 0.5f) * m_Size.x,
                (normalized.y - 0.5f) * m_Size.y,
                (normalized.z - 0.5f) * m_Size.z);
        }

        /// <summary>归一化坐标 [0,1] → 单位立方体局部坐标 [-0.5,0.5]，供缩放 Transform 后对齐体积盒。</summary>
        public Vector3 NormalizedToUnitLocal(Vector3 normalized)
        {
            return new Vector3(
                normalized.x - 0.5f,
                normalized.y - 0.5f,
                normalized.z - 0.5f);
        }

        public Vector3 NormalizedToWorld(Vector3 normalized) => transform.TransformPoint(NormalizedToLocal(normalized));

        public void ApplyVolumeTransform(Transform target) => ApplyVolumeTransformIfChanged(target);

        public bool ApplyVolumeTransformIfChanged(Transform target)
        {
            if (target == null) return false;

            Vector3 worldCenter = transform.TransformPoint(m_Center);
            Vector3 lossy = Abs(transform.lossyScale);
            Vector3 worldSize = Vector3.Scale(m_Size, lossy);
            Vector3 parentLossy = target.parent != null ? Abs(target.parent.lossyScale) : Vector3.one;
            Vector3 localScale = new Vector3(
                worldSize.x / Mathf.Max(parentLossy.x, 1e-6f),
                worldSize.y / Mathf.Max(parentLossy.y, 1e-6f),
                worldSize.z / Mathf.Max(parentLossy.z, 1e-6f));

            if (target.position == worldCenter
                && target.rotation == transform.rotation
                && target.localScale == localScale)
            {
                return false;
            }

            target.SetPositionAndRotation(worldCenter, transform.rotation);
            target.localScale = localScale;
            return true;
        }

        public bool Contains(Vector3 worldPosition) => WorldBounds.Contains(worldPosition);

        private void LateUpdate()
        {
            if (!Application.isPlaying || !m_ShowRuntimeGizmo || !isActiveAndEnabled)
                return;

            EnsureRuntimeLineMaterial();
            EnsureWireCubeMesh();
            if (s_RuntimeLineMaterial == null || s_WireCubeMesh == null)
                return;

            s_RuntimeLineMaterial.SetColor(ColorId, m_GizmoColor);

            Bounds bounds = WorldBounds;
            // 单位线框立方体 [-0.5,0.5]，按 WorldBounds 平移缩放，走普通渲染队列，可被 UI 遮挡。
            Matrix4x4 matrix = Matrix4x4.TRS(bounds.center, Quaternion.identity, bounds.size);
            Graphics.DrawMesh(s_WireCubeMesh, matrix, s_RuntimeLineMaterial, gameObject.layer);
        }

        private void OnValidate()
        {
            m_Size = Vector3.Max(m_Size, Vector3.one * 0.1f);
            m_BoundsDirty = true;
        }

        private void OnDrawGizmos()
        {
            DrawBoundsGizmo(false);
        }

        private void OnDrawGizmosSelected()
        {
            DrawBoundsGizmo(true);
        }

        private void DrawBoundsGizmo(bool selected)
        {
            if (!m_ShowGizmo) return;

            Bounds bounds = WorldBounds;
            Color fill = m_GizmoColor;
            if (selected)
                fill.a = Mathf.Clamp01(m_GizmoColor.a + 0.15f);

            Gizmos.color = new Color(fill.r, fill.g, fill.b, fill.a * 0.2f);
            Gizmos.DrawCube(bounds.center, bounds.size);
            Gizmos.color = fill;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }

        private static void EnsureWireCubeMesh()
        {
            if (s_WireCubeMesh != null) return;

            // 单位立方体线框，边长 1，中心在原点。
            var vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f),
            };

            var indices = new int[]
            {
                0, 1, 1, 2, 2, 3, 3, 0,
                4, 5, 5, 6, 6, 7, 7, 4,
                0, 4, 1, 5, 2, 6, 3, 7,
            };

            s_WireCubeMesh = new Mesh
            {
                name = "TemperatureVolumeBoundsWireCube",
                hideFlags = HideFlags.HideAndDontSave
            };
            s_WireCubeMesh.vertices = vertices;
            s_WireCubeMesh.SetIndices(indices, MeshTopology.Lines, 0);
            s_WireCubeMesh.RecalculateBounds();
        }

        private static void EnsureRuntimeLineMaterial()
        {
            if (s_RuntimeLineMaterial != null) return;

            // Resources 引用可保证正式包不被剥离；勿依赖 Hidden/Internal-Colored。
            Material resourceMat = Resources.Load<Material>("TemperatureRuntimeBoundsLine");
            if (resourceMat != null)
            {
                s_RuntimeLineMaterial = new Material(resourceMat)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
            else
            {
                Shader shader = Shader.Find("TemperatureVisualization/RuntimeBoundsLine");
                if (shader == null)
                    shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null) return;

                s_RuntimeLineMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            // Transparent 队列，排在多数世界物体之后、Overlay UI 之前（Screen Space - Camera UI 通常更晚）。
            s_RuntimeLineMaterial.renderQueue = (int)RenderQueue.Transparent;
            s_RuntimeLineMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            s_RuntimeLineMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            s_RuntimeLineMaterial.SetInt("_Cull", (int)CullMode.Off);
            s_RuntimeLineMaterial.SetInt("_ZWrite", 0);
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }
}
