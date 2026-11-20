using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NaughtyAttributes;
using UnityEngine;

namespace TemperatureVisualization
{
    /// <summary>
    /// 使用反距离加权（IDW）将离散传感器插值为 3D 温度场纹理。
    /// PC 端后台线程计算，WebGL 分帧主线程 fallback。
    /// </summary>
    public class TemperatureFieldInterpolator : MonoBehaviour
    {
        [Label("传感器管理器")]
        [SerializeField] private TemperatureSensorManager m_SensorManager;

        [Label("体积边界")]
        [SerializeField] private TemperatureVolumeBounds m_VolumeBounds;

        [Label("3D 纹理分辨率")]
        [SerializeField] private int m_Resolution = 64;

        [Label("IDW 距离幂次")]
        [SerializeField] private float m_IdwPower = 2f;

        [Label("最小距离阈值")]
        [SerializeField] private float m_MinDistance = 0.05f;

        [Label("自动更新")]
        [SerializeField] private bool m_AutoUpdate = true;

        [Label("更新间隔 (秒)")]
        [SerializeField] private float m_UpdateInterval = 0.6f;

        [Label("最小重建间隔 (秒)")]
        [SerializeField] private float m_MinRebuildInterval = 0.35f;

        [Label("实时更新模式")]
        [SerializeField] private bool m_LiveUpdateMode = true;

        [Label("启用平滑过渡")]
        [SerializeField] private bool m_EnableSmoothTransition;

        [Label("过渡时长 (秒)")]
        [SerializeField] private float m_TransitionDuration = 0.5f;

        [Label("WebGL 每帧 Z 层数")]
        [SerializeField] private int m_WebGlZSlicesPerFrame = 2;

        private Texture3D m_TextureA;
        private Texture3D m_TextureB;
        private float[] m_WorkBuffer;
        private float[] m_PreviousBuffer;
        private float[] m_SensorX;
        private float[] m_SensorY;
        private float[] m_SensorZ;
        private float[] m_SensorT;
        private int m_SensorCount;
        private float m_Blend = 1f;
        private float m_UpdateTimer;
        private float m_LastRebuildTime;
        private bool m_IsComputing;
        private bool m_UseTextureA = true;
        private bool m_RebuildPending;
        private CancellationTokenSource m_Cts;
        private float m_MinDistSqr;
        private int m_ResMinus1;
        private int m_ResSq;
        private float[] m_IdwCoeffs;
        private int m_CoeffStride;
        private ulong m_LayoutHash;
        private bool m_IdwUseSquare;
        private float m_IdwPowerCached;

        public Texture3D CurrentTexture => m_LiveUpdateMode ? m_TextureA : (m_UseTextureA ? m_TextureA : m_TextureB);
        public Texture3D PreviousTexture => m_LiveUpdateMode ? m_TextureA : (m_UseTextureA ? m_TextureB : m_TextureA);
        public float BlendFactor => m_LiveUpdateMode || !m_EnableSmoothTransition ? 1f : m_Blend;
        public int Resolution => m_Resolution;
        public bool IsComputing => m_IsComputing;
        public bool IsBlending => !m_LiveUpdateMode && m_EnableSmoothTransition && m_Blend < 0.999f;
        public bool HasBuffer => m_WorkBuffer != null && m_WorkBuffer.Length > 0;

        public event Action TextureUpdated;

        private void Awake()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (m_Resolution > 48) m_Resolution = 48;
            if (m_WebGlZSlicesPerFrame < 1) m_WebGlZSlicesPerFrame = 1;
#endif
            m_MinDistSqr = m_MinDistance * m_MinDistance;
            CacheResolutionDerived();
        }

        private void OnEnable()
        {
            if (m_SensorManager != null)
            {
                m_SensorManager.SensorsChanged += OnSensorsChanged;
            }

            EnsureTextures();
            RequestRebuild(force: true);
        }

        private void OnDisable()
        {
            if (m_SensorManager != null)
            {
                m_SensorManager.SensorsChanged -= OnSensorsChanged;
            }

            CancelCompute();
        }

        private void OnDestroy()
        {
            CancelCompute();
            ReleaseTextures();
        }

        private void Update()
        {
            if (!m_LiveUpdateMode && m_EnableSmoothTransition && m_Blend < 1f)
            {
                m_Blend = Mathf.MoveTowards(m_Blend, 1f, Time.deltaTime / Mathf.Max(m_TransitionDuration, 0.01f));
            }

            if (m_RebuildPending && !m_IsComputing && Time.unscaledTime - m_LastRebuildTime >= m_MinRebuildInterval)
            {
                m_RebuildPending = false;
                StartRebuild();
                return;
            }

            if (!m_AutoUpdate || m_IsComputing) return;

            m_UpdateTimer += Time.deltaTime;
            if (m_UpdateTimer >= m_UpdateInterval)
            {
                m_UpdateTimer = 0f;
                RequestRebuild();
            }
        }

        public void Configure(TemperatureSensorManager sensorManager, TemperatureVolumeBounds volumeBounds)
        {
            if (m_SensorManager != null)
            {
                m_SensorManager.SensorsChanged -= OnSensorsChanged;
            }

            m_SensorManager = sensorManager;
            m_VolumeBounds = volumeBounds;

            if (m_SensorManager != null)
            {
                m_SensorManager.SensorsChanged += OnSensorsChanged;
            }
        }

        public void SetResolution(int resolution)
        {
            resolution = Mathf.Clamp(resolution, 8, 128);
            if (resolution == m_Resolution) return;
            m_Resolution = resolution;
            CacheResolutionDerived();
            ReleaseTextures();
            EnsureTextures();
            RequestRebuild(force: true);
        }

        public void RequestRebuild(bool force = false)
        {
            if (!isActiveAndEnabled || m_SensorManager == null || m_VolumeBounds == null) return;
            if (m_SensorManager.Sensors.Count < 1) return;

            if (!force && Time.unscaledTime - m_LastRebuildTime < m_MinRebuildInterval)
            {
                m_RebuildPending = true;
                return;
            }

            if (m_IsComputing)
            {
                m_RebuildPending = true;
                return;
            }

            StartRebuild();
        }

        public float SampleBuffer(int x, int y, int z)
        {
            return m_WorkBuffer[x + y * m_Resolution + z * m_ResSq];
        }

        public float SampleNormalized(Vector3 normalized)
        {
            if (!HasBuffer) return 0f;

            int res = m_Resolution;
            float x = Mathf.Clamp01(normalized.x) * m_ResMinus1;
            float y = Mathf.Clamp01(normalized.y) * m_ResMinus1;
            float z = Mathf.Clamp01(normalized.z) * m_ResMinus1;

            int x0 = (int)x;
            int y0 = (int)y;
            int z0 = (int)z;
            int x1 = x0 < m_ResMinus1 ? x0 + 1 : x0;
            int y1 = y0 < m_ResMinus1 ? y0 + 1 : y0;
            int z1 = z0 < m_ResMinus1 ? z0 + 1 : z0;

            float tx = x - x0;
            float ty = y - y0;
            float tz = z - z0;

            float c000 = SampleBuffer(x0, y0, z0);
            float c100 = SampleBuffer(x1, y0, z0);
            float c010 = SampleBuffer(x0, y1, z0);
            float c110 = SampleBuffer(x1, y1, z0);
            float c001 = SampleBuffer(x0, y0, z1);
            float c101 = SampleBuffer(x1, y0, z1);
            float c011 = SampleBuffer(x0, y1, z1);
            float c111 = SampleBuffer(x1, y1, z1);

            float c00 = c000 + (c100 - c000) * tx;
            float c10 = c010 + (c110 - c010) * tx;
            float c01 = c001 + (c101 - c001) * tx;
            float c11 = c011 + (c111 - c011) * tx;
            float c0 = c00 + (c10 - c00) * ty;
            float c1 = c01 + (c11 - c01) * ty;
            return c0 + (c1 - c0) * tz;
        }

        private void CacheResolutionDerived()
        {
            m_ResMinus1 = Mathf.Max(m_Resolution - 1, 1);
            m_ResSq = m_Resolution * m_Resolution;
        }

        private void OnSensorsChanged()
        {
            RequestRebuild();
        }

        private void StartRebuild()
        {
            EnsureTextures();
            BuildSensorArrays();

            ulong layoutHash = ComputeLayoutHash();
            if (layoutHash == m_LayoutHash && m_IdwCoeffs != null)
            {
                ApplyTemperatureFromCoeffs();
                UploadToTexture();
                return;
            }

            m_LayoutHash = layoutHash;
            m_IdwUseSquare = Mathf.Approximately(m_IdwPower, 2f);
            m_IdwPowerCached = m_IdwPower;
            EnsureCoeffBuffer();

#if UNITY_WEBGL && !UNITY_EDITOR
            StartCoroutine(ComputeOnMainThreadCoroutine());
#else
            _ = ComputeOnBackgroundThreadAsync();
#endif
        }

        private void BuildSensorArrays()
        {
            var list = m_SensorManager.Sensors;
            int count = list.Count;
            if (m_SensorX == null || m_SensorX.Length < count)
            {
                m_SensorX = new float[count];
                m_SensorY = new float[count];
                m_SensorZ = new float[count];
                m_SensorT = new float[count];
            }

            Matrix4x4 localToWorld = m_VolumeBounds != null
                ? m_VolumeBounds.transform.localToWorldMatrix
                : Matrix4x4.identity;
            m_SensorCount = count;
            for (int i = 0; i < count; i++)
            {
                Vector3 world = localToWorld.MultiplyPoint3x4(list[i].Position);
                m_SensorX[i] = world.x;
                m_SensorY[i] = world.y;
                m_SensorZ[i] = world.z;
                m_SensorT[i] = list[i].Temperature;
            }
        }

        private void EnsureTextures()
        {
            int voxelCount = m_ResSq * m_Resolution;
            if (m_WorkBuffer == null || m_WorkBuffer.Length != voxelCount)
            {
                m_WorkBuffer = new float[voxelCount];
                m_PreviousBuffer = new float[voxelCount];
                m_IdwCoeffs = null;
                m_LayoutHash = 0;
            }

            if (m_TextureA == null || m_TextureA.width != m_Resolution)
            {
                ReleaseTextures();
                m_TextureA = CreateTexture3D(m_Resolution);
                m_IdwCoeffs = null;
                m_LayoutHash = 0;
                if (!m_LiveUpdateMode)
                {
                    m_TextureB = CreateTexture3D(m_Resolution);
                }
            }
        }

        private void EnsureCoeffBuffer()
        {
            int voxelCount = m_ResSq * m_Resolution;
            m_CoeffStride = m_SensorCount;
            int required = voxelCount * m_CoeffStride;
            if (m_IdwCoeffs == null || m_IdwCoeffs.Length < required)
            {
                m_IdwCoeffs = new float[required];
            }
        }

        private void ApplyTemperatureFromCoeffs()
        {
            int voxelCount = m_ResSq * m_Resolution;
            int count = m_SensorCount;
            int stride = m_CoeffStride;
            float[] coeffs = m_IdwCoeffs;
            float[] buffer = m_WorkBuffer;
            float[] st = m_SensorT;

            for (int v = 0, c = 0; v < voxelCount; v++, c += stride)
            {
                float sum = 0f;
                for (int s = 0; s < count; s++)
                {
                    sum += coeffs[c + s] * st[s];
                }

                buffer[v] = sum;
            }
        }

        private ulong ComputeLayoutHash()
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                hash = HashU64(hash, (ulong)(uint)m_Resolution);
                hash = HashU64(hash, (ulong)m_IdwPower.GetHashCode());
                hash = HashU64(hash, (ulong)m_MinDistSqr.GetHashCode());
                hash = HashU64(hash, (ulong)m_SensorCount);

                if (m_VolumeBounds != null)
                {
                    Matrix4x4 m = m_VolumeBounds.transform.localToWorldMatrix;
                    hash = HashU64(hash, (ulong)m.m00.GetHashCode());
                    hash = HashU64(hash, (ulong)m.m01.GetHashCode());
                    hash = HashU64(hash, (ulong)m.m02.GetHashCode());
                    hash = HashU64(hash, (ulong)m.m03.GetHashCode());
                    hash = HashU64(hash, (ulong)m.m10.GetHashCode());
                    hash = HashU64(hash, (ulong)m.m11.GetHashCode());
                    hash = HashU64(hash, (ulong)m.m12.GetHashCode());
                    hash = HashU64(hash, (ulong)m.m13.GetHashCode());
                    hash = HashU64(hash, (ulong)m.m20.GetHashCode());
                    hash = HashU64(hash, (ulong)m.m21.GetHashCode());
                    hash = HashU64(hash, (ulong)m.m22.GetHashCode());
                    hash = HashU64(hash, (ulong)m.m23.GetHashCode());
                    hash = HashU64(hash, (ulong)m.m30.GetHashCode());
                    hash = HashU64(hash, (ulong)m.m31.GetHashCode());
                    hash = HashU64(hash, (ulong)m.m32.GetHashCode());
                    hash = HashU64(hash, (ulong)m.m33.GetHashCode());

                    Vector3 center = m_VolumeBounds.Center;
                    Vector3 size = m_VolumeBounds.Size;
                    hash = HashU64(hash, (ulong)center.x.GetHashCode());
                    hash = HashU64(hash, (ulong)center.y.GetHashCode());
                    hash = HashU64(hash, (ulong)center.z.GetHashCode());
                    hash = HashU64(hash, (ulong)size.x.GetHashCode());
                    hash = HashU64(hash, (ulong)size.y.GetHashCode());
                    hash = HashU64(hash, (ulong)size.z.GetHashCode());
                }

                var list = m_SensorManager.Sensors;
                for (int i = 0; i < list.Count; i++)
                {
                    Vector3 p = list[i].Position;
                    hash = HashU64(hash, (ulong)p.x.GetHashCode());
                    hash = HashU64(hash, (ulong)p.y.GetHashCode());
                    hash = HashU64(hash, (ulong)p.z.GetHashCode());
                }

                return hash;
            }
        }

        private static ulong HashU64(ulong hash, ulong value)
        {
            unchecked
            {
                return (hash ^ value) * 1099511628211UL;
            }
        }

        private static Texture3D CreateTexture3D(int resolution)
        {
            var format = SystemInfo.SupportsTextureFormat(TextureFormat.RFloat)
                ? TextureFormat.RFloat
                : TextureFormat.RHalf;

            var texture = new Texture3D(resolution, resolution, resolution, format, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return texture;
        }

        private void ReleaseTextures()
        {
            if (m_TextureA != null)
            {
                if (Application.isPlaying) Destroy(m_TextureA);
                else DestroyImmediate(m_TextureA);
                m_TextureA = null;
            }

            if (m_TextureB != null)
            {
                if (Application.isPlaying) Destroy(m_TextureB);
                else DestroyImmediate(m_TextureB);
                m_TextureB = null;
            }
        }

        private void CancelCompute()
        {
            m_Cts?.Cancel();
            m_Cts?.Dispose();
            m_Cts = null;
            m_IsComputing = false;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private IEnumerator ComputeOnMainThreadCoroutine()
        {
            m_IsComputing = true;
            CaptureVolumeGrid(out Matrix4x4 localToWorld, out Vector3 localCenter, out Vector3 localSize);
            int res = m_Resolution;
            int resMinus1 = m_ResMinus1;
            float invResMinus1 = 1f / resMinus1;
            float power = m_IdwPowerCached;
            float minDistSqr = m_MinDistSqr;
            int sensorCount = m_SensorCount;
            float[] sx = m_SensorX;
            float[] sy = m_SensorY;
            float[] sz = m_SensorZ;
            float[] st = m_SensorT;
            bool useSquare = m_IdwUseSquare;
            float[] buffer = m_WorkBuffer;
            float[] coeffs = m_IdwCoeffs;
            int coeffStride = m_CoeffStride;
            int slicesPerFrame = Mathf.Max(1, m_WebGlZSlicesPerFrame);
            int zBatch = 0;
            int voxelIndex = 0;

            for (int z = 0; z < res; z++)
            {
                float nz = z * invResMinus1;
                float lz = localCenter.z + (nz - 0.5f) * localSize.z;
                for (int y = 0; y < res; y++)
                {
                    float ny = y * invResMinus1;
                    float ly = localCenter.y + (ny - 0.5f) * localSize.y;
                    for (int x = 0; x < res; x++)
                    {
                        float nx = x * invResMinus1;
                        float lx = localCenter.x + (nx - 0.5f) * localSize.x;
                        GridPointToWorld(localToWorld, lx, ly, lz, out float wx, out float wy, out float wz);
                        buffer[voxelIndex] = ComputeIdw(
                            wx, wy, wz,
                            sx, sy, sz, st, sensorCount, power, minDistSqr, useSquare,
                            coeffs, voxelIndex * coeffStride);
                        voxelIndex++;
                    }
                }

                if (++zBatch >= slicesPerFrame)
                {
                    zBatch = 0;
                    yield return null;
                }
            }

            UploadToTexture();
            m_IsComputing = false;
            if (m_RebuildPending)
            {
                m_RebuildPending = false;
                StartRebuild();
            }
        }
#else
        private async Task ComputeOnBackgroundThreadAsync()
        {
            m_IsComputing = true;
            m_Cts = new CancellationTokenSource();
            CancellationToken token = m_Cts.Token;

            CaptureVolumeGrid(out Matrix4x4 localToWorld, out Vector3 localCenter, out Vector3 localSize);
            int res = m_Resolution;
            int resSq = m_ResSq;
            int resMinus1 = m_ResMinus1;
            float invResMinus1 = 1f / resMinus1;
            float power = m_IdwPowerCached;
            float minDistSqr = m_MinDistSqr;
            int sensorCount = m_SensorCount;
            float[] sx = m_SensorX;
            float[] sy = m_SensorY;
            float[] sz = m_SensorZ;
            float[] st = m_SensorT;
            float[] buffer = m_WorkBuffer;
            float[] coeffs = m_IdwCoeffs;
            int coeffStride = m_CoeffStride;
            bool useSquare = m_IdwUseSquare;

            try
            {
                await Task.Run(() =>
                {
                    int voxelIndex = 0;
                    for (int z = 0; z < res; z++)
                    {
                        token.ThrowIfCancellationRequested();
                        float nz = z * invResMinus1;
                        float lz = localCenter.z + (nz - 0.5f) * localSize.z;
                        for (int y = 0; y < res; y++)
                        {
                            float ny = y * invResMinus1;
                            float ly = localCenter.y + (ny - 0.5f) * localSize.y;
                            for (int x = 0; x < res; x++)
                            {
                                float nx = x * invResMinus1;
                                float lx = localCenter.x + (nx - 0.5f) * localSize.x;
                                GridPointToWorld(localToWorld, lx, ly, lz, out float wx, out float wy, out float wz);
                                buffer[voxelIndex] = ComputeIdw(
                                    wx, wy, wz,
                                    sx, sy, sz, st, sensorCount, power, minDistSqr, useSquare,
                                    coeffs, voxelIndex * coeffStride);
                                voxelIndex++;
                            }
                        }
                    }
                }, token);

                UploadToTexture();
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                m_IsComputing = false;
                if (m_RebuildPending)
                {
                    m_RebuildPending = false;
                    StartRebuild();
                }
            }
        }
#endif

        private void CaptureVolumeGrid(out Matrix4x4 localToWorld, out Vector3 localCenter, out Vector3 localSize)
        {
            localToWorld = m_VolumeBounds.transform.localToWorldMatrix;
            localCenter = m_VolumeBounds.Center;
            localSize = m_VolumeBounds.Size;
        }

        private static void GridPointToWorld(
            Matrix4x4 localToWorld,
            float lx, float ly, float lz,
            out float wx, out float wy, out float wz)
        {
            wx = localToWorld.m00 * lx + localToWorld.m01 * ly + localToWorld.m02 * lz + localToWorld.m03;
            wy = localToWorld.m10 * lx + localToWorld.m11 * ly + localToWorld.m12 * lz + localToWorld.m13;
            wz = localToWorld.m20 * lx + localToWorld.m21 * ly + localToWorld.m22 * lz + localToWorld.m23;
        }

        private void UploadToTexture()
        {
            m_LastRebuildTime = Time.unscaledTime;

            if (m_LiveUpdateMode)
            {
                m_TextureA.SetPixelData(m_WorkBuffer, 0);
                m_TextureA.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                m_Blend = 1f;
                TextureUpdated?.Invoke();
                return;
            }

            if (!m_EnableSmoothTransition)
            {
                m_TextureA.SetPixelData(m_WorkBuffer, 0);
                m_TextureA.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                m_UseTextureA = true;
                m_Blend = 1f;
                TextureUpdated?.Invoke();
                return;
            }

            Array.Copy(m_WorkBuffer, m_PreviousBuffer, m_WorkBuffer.Length);
            Texture3D target = m_UseTextureA ? m_TextureB : m_TextureA;
            target.SetPixelData(m_WorkBuffer, 0);
            target.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            m_UseTextureA = !m_UseTextureA;
            m_Blend = 0f;
            TextureUpdated?.Invoke();
        }

        private static float ComputeIdw(
            float wx, float wy, float wz,
            float[] sx, float[] sy, float[] sz, float[] st,
            int count, float power, float minDistSqr, bool useSquare,
            float[] coeffs, int coeffBase)
        {
            if (count == 0) return 0f;
            if (count == 1)
            {
                if (coeffs != null) coeffs[coeffBase] = 1f;
                return st[0];
            }

            float weightSum = 0f;
            float valueSum = 0f;

            for (int i = 0; i < count; i++)
            {
                float dx = wx - sx[i];
                float dy = wy - sy[i];
                float dz = wz - sz[i];
                float distSqr = dx * dx + dy * dy + dz * dz;
                if (distSqr < minDistSqr)
                {
                    if (coeffs != null)
                    {
                        for (int j = 0; j < count; j++)
                        {
                            coeffs[coeffBase + j] = 0f;
                        }

                        coeffs[coeffBase + i] = 1f;
                    }

                    return st[i];
                }

                float weight = useSquare ? 1f / distSqr : Mathf.Pow(distSqr, -power * 0.5f);
                if (coeffs != null) coeffs[coeffBase + i] = weight;
                weightSum += weight;
                valueSum += weight * st[i];
            }

            if (coeffs != null)
            {
                float invWeightSum = 1f / weightSum;
                for (int i = 0; i < count; i++)
                {
                    coeffs[coeffBase + i] *= invWeightSum;
                }
            }

            return weightSum > 0f ? valueSum / weightSum : st[0];
        }
    }
}
