using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Rendering;

namespace TemperatureVisualization
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class TemperatureIsosurfaceRenderer : MonoBehaviour
    {
        private static readonly Vector3Int[] s_CubeCornerOffset =
        {
            new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(1, 1, 0), new Vector3Int(0, 1, 0),
            new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 1), new Vector3Int(1, 1, 1), new Vector3Int(0, 1, 1)
        };

        [Label("等温面材质")]
        [SerializeField] private Material m_SurfaceMaterial;

        [Label("等温面温度 (℃)")]
        [SerializeField] private float m_IsoTemperature = 26f;

        [Label("网格分辨率")]
        [SerializeField] private int m_GridResolution = 32;

        [Label("平滑法线")]
        [SerializeField] private bool m_SmoothNormals = true;

        private MeshFilter m_MeshFilter;
        private MeshRenderer m_MeshRenderer;
        private Mesh m_Mesh;
        private Material m_MaterialInstance;
        private Vector3[] m_Vertices;
        private Color[] m_Colors;
        private int[] m_Triangles;
        private Vector3[] m_CubeCorners = new Vector3[8];
        private float[] m_CornerValues = new float[8];
        private Vector3[] m_EdgeVertexCache = new Vector3[12];
        private Color m_LastIsoColor;
        private bool m_HasLastIsoColor;
        private bool m_Visible;

        public float IsoTemperature { get => m_IsoTemperature; set => m_IsoTemperature = value; }
        public int GridResolution { get => m_GridResolution; set => m_GridResolution = Mathf.Clamp(value, 8, 64); }

        private void Awake()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (m_GridResolution > 28) m_GridResolution = 28;
#endif
            m_MeshFilter = GetComponent<MeshFilter>();
            m_MeshRenderer = GetComponent<MeshRenderer>();
            m_Mesh = new Mesh { name = "TemperatureIsosurface" };
            m_Mesh.indexFormat = IndexFormat.UInt32;
            m_MeshFilter.sharedMesh = m_Mesh;
        }

        private void OnDestroy()
        {
            if (m_Mesh != null)
            {
                if (Application.isPlaying) Destroy(m_Mesh);
                else DestroyImmediate(m_Mesh);
            }

            if (m_MaterialInstance != null)
            {
                if (Application.isPlaying) Destroy(m_MaterialInstance);
                else DestroyImmediate(m_MaterialInstance);
            }
        }

        public void SetEnabled(bool enabled)
        {
            m_Visible = enabled;
            if (m_MeshRenderer != null) m_MeshRenderer.enabled = enabled;
        }

        public void SyncBoundsTransform(TemperatureVolumeBounds bounds)
        {
            bounds?.ApplyVolumeTransform(transform);
        }

        public bool SyncBoundsTransformIfChanged(TemperatureVolumeBounds bounds)
        {
            return bounds != null && bounds.ApplyVolumeTransformIfChanged(transform);
        }

        public void RebuildMesh(
            TemperatureFieldInterpolator interpolator,
            TemperatureColorRamp colorRamp,
            TemperatureVolumeBounds bounds,
            float tempMin,
            float tempMax)
        {
            if (interpolator == null || bounds == null || colorRamp == null || !interpolator.HasBuffer) return;

            SyncBoundsTransform(bounds);
            EnsureMaterial(colorRamp, tempMin, tempMax, m_IsoTemperature);

            int dataRes = interpolator.Resolution;
            int res = Mathf.Min(m_GridResolution, dataRes);
            int resMinus1 = res - 1;
            int dataResMinus1 = dataRes - 1;
            bool directGrid = res == dataRes;
            float iso = m_IsoTemperature;
            float invDataResMinus1 = 1f / dataResMinus1;

            int vertCap = 65536;
            int triCap = 98304;
            if (m_Vertices == null || m_Vertices.Length < vertCap)
            {
                m_Vertices = new Vector3[vertCap];
                m_Colors = new Color[vertCap];
                m_Triangles = new int[triCap];
            }

            int vertCount = 0;
            int triCount = 0;
            float invResMinus1 = 1f / resMinus1;

            for (int z = 0; z < resMinus1; z++)
            {
                for (int y = 0; y < resMinus1; y++)
                {
                    for (int x = 0; x < resMinus1; x++)
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            Vector3Int c = s_CubeCornerOffset[i];
                            int gx = x + c.x;
                            int gy = y + c.y;
                            int gz = z + c.z;

                            if (directGrid)
                            {
                                m_CornerValues[i] = interpolator.SampleBuffer(gx, gy, gz);
                                float nx = gx * invResMinus1 - 0.5f;
                                float ny = gy * invResMinus1 - 0.5f;
                                float nz = gz * invResMinus1 - 0.5f;
                                m_CubeCorners[i] = new Vector3(nx, ny, nz);
                            }
                            else
                            {
                                int sx = gx * dataResMinus1 / resMinus1;
                                int sy = gy * dataResMinus1 / resMinus1;
                                int sz = gz * dataResMinus1 / resMinus1;
                                m_CornerValues[i] = interpolator.SampleBuffer(sx, sy, sz);
                                float nx = sx * invDataResMinus1 - 0.5f;
                                float ny = sy * invDataResMinus1 - 0.5f;
                                float nz = sz * invDataResMinus1 - 0.5f;
                                m_CubeCorners[i] = new Vector3(nx, ny, nz);
                            }
                        }

                        int cubeIndex = 0;
                        for (int i = 0; i < 8; i++)
                        {
                            if (m_CornerValues[i] < iso) cubeIndex |= 1 << i;
                        }

                        int edgeFlags = MarchingCubesTables.EdgeTable[cubeIndex];
                        if (edgeFlags == 0) continue;

                        for (int i = 0; i < 12; i++)
                        {
                            if ((edgeFlags & (1 << i)) == 0) continue;
                            m_EdgeVertexCache[i] = InterpolateEdge(i, m_CubeCorners, m_CornerValues, iso);
                        }

                        for (int i = 0; MarchingCubesTables.TriTable[cubeIndex, i] != -1; i += 3)
                        {
                            int e0 = MarchingCubesTables.TriTable[cubeIndex, i];
                            int e1 = MarchingCubesTables.TriTable[cubeIndex, i + 1];
                            int e2 = MarchingCubesTables.TriTable[cubeIndex, i + 2];

                            if (vertCount + 3 > m_Vertices.Length || triCount + 3 > m_Triangles.Length) break;

                            m_Vertices[vertCount] = m_EdgeVertexCache[e0];
                            m_Vertices[vertCount + 1] = m_EdgeVertexCache[e1];
                            m_Vertices[vertCount + 2] = m_EdgeVertexCache[e2];
                            m_Colors[vertCount] = m_LastIsoColor;
                            m_Colors[vertCount + 1] = m_LastIsoColor;
                            m_Colors[vertCount + 2] = m_LastIsoColor;
                            m_Triangles[triCount] = vertCount;
                            m_Triangles[triCount + 1] = vertCount + 1;
                            m_Triangles[triCount + 2] = vertCount + 2;
                            vertCount += 3;
                            triCount += 3;
                        }
                    }
                }
            }

            m_Mesh.Clear();
            if (vertCount == 0)
            {
                if (m_MeshRenderer != null) m_MeshRenderer.enabled = false;
                return;
            }

            m_Mesh.SetVertices(m_Vertices, 0, vertCount);
            m_Mesh.SetColors(m_Colors, 0, vertCount);
            m_Mesh.SetTriangles(m_Triangles, 0, triCount, 0, false);
            if (m_SmoothNormals) m_Mesh.RecalculateNormals();
            m_Mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
            if (m_MeshRenderer != null) m_MeshRenderer.enabled = m_Visible;
        }

        private void EnsureMaterial(TemperatureColorRamp colorRamp, float tempMin, float tempMax, float isoTemperature)
        {
            if (m_MaterialInstance == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                m_MaterialInstance = m_SurfaceMaterial != null
                    ? new Material(m_SurfaceMaterial)
                    : new Material(shader);
                m_MaterialInstance.SetFloat("_Surface", 1);
                m_MaterialInstance.SetFloat("_Blend", 0);
                m_MaterialInstance.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m_MaterialInstance.renderQueue = 3000;
                m_MaterialInstance.SetFloat("_Cull", (float)CullMode.Off);
                m_MeshRenderer.sharedMaterial = m_MaterialInstance;
            }

            float normalized = Mathf.InverseLerp(tempMin, tempMax, isoTemperature);
            Color color = colorRamp.Evaluate(normalized);
            color.a = 0.65f;
            if (!m_HasLastIsoColor || color != m_LastIsoColor)
            {
                m_LastIsoColor = color;
                m_HasLastIsoColor = true;
                m_MaterialInstance.color = color;
            }
        }

        private static Vector3 InterpolateEdge(int edgeIndex, Vector3[] corners, float[] values, float isoTemperature)
        {
            GetEdgeVertices(edgeIndex, out int v0, out int v1);
            float t = (isoTemperature - values[v0]) / (values[v1] - values[v0] + 1e-6f);
            return Vector3.LerpUnclamped(corners[v0], corners[v1], Mathf.Clamp01(t));
        }

        private static void GetEdgeVertices(int edgeIndex, out int v0, out int v1)
        {
            switch (edgeIndex)
            {
                case 0: v0 = 0; v1 = 1; return;
                case 1: v0 = 1; v1 = 2; return;
                case 2: v0 = 2; v1 = 3; return;
                case 3: v0 = 3; v1 = 0; return;
                case 4: v0 = 4; v1 = 5; return;
                case 5: v0 = 5; v1 = 6; return;
                case 6: v0 = 6; v1 = 7; return;
                case 7: v0 = 7; v1 = 4; return;
                case 8: v0 = 0; v1 = 4; return;
                case 9: v0 = 1; v1 = 5; return;
                case 10: v0 = 2; v1 = 6; return;
                default: v0 = 3; v1 = 7; return;
            }
        }
    }
}
