using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>路径上的一个稳定采样点。t 为工艺路径参数 0~1。</summary>
    public struct WrapSample
    {
        public float t;
        public float pathLength;
        public Vector3 nozzleWorld;
        public Vector3 radialWorld;
        public float localY;

        public static WrapSample Lerp(in WrapSample a, in WrapSample b, float f)
        {
            Vector3 radial = Vector3.Slerp(a.radialWorld, b.radialWorld, f);
            radial.y = 0f;
            if (radial.sqrMagnitude < 1e-8f)
                radial = a.radialWorld;
            else
                radial.Normalize();

            return new WrapSample
            {
                t = Mathf.Lerp(a.t, b.t, f),
                pathLength = Mathf.Lerp(a.pathLength, b.pathLength, f),
                nozzleWorld = Vector3.Lerp(a.nozzleWorld, b.nozzleWorld, f),
                radialWorld = radial,
                localY = Mathf.Lerp(a.localY, b.localY, f)
            };
        }
    }

    /// <summary>
    /// 缠绕路径（纯数据）：按立方体表面预采样螺旋点列，含弧长。
    /// 进度 t 沿工艺路径均匀，与帧率、喷嘴位移无关。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public class WrapPath : MonoBehaviour
    {
        [Header("包裹目标")]
        [InspectorLabel("货物根节点")]
        [SerializeField] private Transform m_cargo;

        [InspectorLabel("立方体尺寸 XYZ)")]
        [SerializeField] private Vector3 m_cuboidSize = new Vector3(1f, 1.2f, 1f);

        [InspectorLabel("尺寸中心偏移")]
        [SerializeField] private Vector3 m_cuboidCenterLocal = Vector3.zero;

        [InspectorLabel("表面外扩")]
        [SerializeField] [Min(0.005f)] private float m_surfaceOffset = 0.04f;

        [InspectorLabel("转角圆角半径")]
        [SerializeField] [Min(0f)] private float m_cornerRadius = 0.04f;

        [Header("螺旋路径")]
        [InspectorLabel("绕行半径")]
        [SerializeField] private float m_radius = 1.45f;

        [InspectorLabel("起始高度")]
        [Tooltip("相对货物原点的最低高度（米）")]
        [SerializeField] private float m_bottomY = 0.2f;

        [InspectorLabel("结束高度")]
        [Tooltip("相对货物原点的最高高度（米）")]
        [SerializeField] private float m_topY = 1.55f;

        [InspectorLabel("圈数")]
        [SerializeField] [Range(1f, 12f)] private float m_revolutions = 6f;

        [InspectorLabel("先升后略降")]
        [SerializeField] private bool m_upThenSlightDown = true;

        [InspectorLabel("每圈采样数")]
        [SerializeField] [Range(16, 128)] private int m_samplesPerRevolution = 64;

        private readonly List<WrapSample> _samples = new List<WrapSample>(512);
        private bool _baked;
        private int _bakeVersion;

        public Transform Cargo => m_cargo;
        public Vector3 CuboidSize => m_cuboidSize;
        public Vector3 CuboidCenterLocal => m_cuboidCenterLocal;
        public float SurfaceOffset => m_surfaceOffset;
        public float CornerRadius => m_cornerRadius;
        public float OrbitRadius => m_radius;
        public IReadOnlyList<WrapSample> Samples => _samples;
        public int SampleCount => _samples.Count;
        public float TotalPathLength => _samples.Count > 0 ? _samples[_samples.Count - 1].pathLength : 0f;
        public int BakeVersion => _bakeVersion;
        public bool IsBaked => _baked && _samples.Count >= 2;

        private void Awake()
        {
            Bake();
        }

        public void EnsureBaked()
        {
            if (!_baked || _samples.Count < 2)
                Bake();
        }

        [ContextMenu("烘焙路径")]
        public void Bake()
        {
            _samples.Clear();
            _baked = false;

            if (m_cargo == null)
                return;

            int steps = Mathf.Max(8, m_samplesPerRevolution) * Mathf.Max(1, Mathf.CeilToInt(m_revolutions));
            bool sharpCorners = m_cornerRadius <= 1e-5f;
            int lastFace = -1;
            Vector3 lastRadial = Vector3.right;
            Vector3 lastMid = Vector3.zero;
            bool hasLastMid = false;
            float pathLength = 0f;

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector3 radial = EvaluateRadial(t);
                int face = WrapSurface.GetCuboidFace(m_cargo, m_cuboidSize, m_surfaceOffset, radial);

                if (sharpCorners && i > 0 && lastFace >= 0 && face != lastFace)
                {
                    float tCorner = Mathf.Lerp(_samples[_samples.Count - 1].t, t, 0.5f);
                    AppendCornerSamples(lastRadial, radial, lastFace, face, tCorner, ref pathLength, ref lastMid, ref hasLastMid);
                }

                AppendSample(t, radial, ref pathLength, ref lastMid, ref hasLastMid);
                lastFace = face;
                lastRadial = radial;
            }

            _baked = _samples.Count >= 2;
            _bakeVersion++;
        }

        public WrapSample Evaluate(float t)
        {
            EnsureBaked();
            if (_samples.Count == 0)
                return default;
            if (_samples.Count == 1)
                return _samples[0];

            t = Mathf.Clamp01(t);
            int i = GetSpanIndex(t);
            WrapSample a = _samples[i];
            WrapSample b = _samples[i + 1];
            float dt = b.t - a.t;
            float f = dt > 1e-8f ? Mathf.Clamp01((t - a.t) / dt) : 1f;
            return WrapSample.Lerp(a, b, f);
        }

        /// <summary>最后一个 t 不超过 progress 的样本下标；progress 过小返回 -1。</summary>
        public int GetLastIndexAtOrBefore(float t)
        {
            EnsureBaked();
            if (_samples.Count == 0 || t <= 1e-8f)
                return -1;

            t = Mathf.Clamp01(t);
            if (t >= _samples[_samples.Count - 1].t)
                return _samples.Count - 1;

            int span = GetSpanIndex(t);
            if (_samples[span].t <= t)
                return span;
            return Mathf.Max(-1, span - 1);
        }

        public void SampleSurface(Vector3 radialWorld, float localY, out Vector3 point, out Vector3 normal)
        {
            WrapSurface.SampleCuboid(
                m_cargo,
                m_cuboidSize,
                m_cuboidCenterLocal,
                m_surfaceOffset,
                m_cornerRadius,
                radialWorld,
                localY,
                out point,
                out normal);
        }

        public Vector3 EvaluateNozzlePosition(float t)
        {
            t = Mathf.Clamp01(t);
            Vector3 center = m_cargo != null ? m_cargo.position : Vector3.zero;
            float angle = t * m_revolutions * Mathf.PI * 2f;
            float y = EvaluateHeight(t);
            return new Vector3(
                center.x + Mathf.Cos(angle) * m_radius,
                center.y + y,
                center.z + Mathf.Sin(angle) * m_radius);
        }

        public Vector3 GetWrapCenterWorld()
        {
            return WrapSurface.GetCenterWorld(m_cargo, m_cuboidCenterLocal);
        }

        [ContextMenu("从货物同步立方体尺寸")]
        public void SyncCuboidSizeFromCargo()
        {
            AutoFitFromCargoBounds();
            Bake();
        }

        public void AutoFitFromCargoBounds()
        {
            if (m_cargo == null)
                return;

            var renderers = m_cargo.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
            {
                var cols = m_cargo.GetComponentsInChildren<Collider>();
                if (cols == null || cols.Length == 0)
                    return;

                Bounds wb = cols[0].bounds;
                for (int i = 1; i < cols.Length; i++)
                    wb.Encapsulate(cols[i].bounds);

                ApplyWorldBoundsAsCuboid(wb);
                return;
            }

            Bounds worldBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                worldBounds.Encapsulate(renderers[i].bounds);

            ApplyWorldBoundsAsCuboid(worldBounds);
        }

        private Vector3 EvaluateRadial(float t)
        {
            Vector3 nozzle = EvaluateNozzlePosition(t);
            Vector3 radial = nozzle - GetWrapCenterWorld();
            radial.y = 0f;
            if (radial.sqrMagnitude < 1e-8f)
                return Vector3.right;
            return radial.normalized;
        }

        private float EvaluateHeight(float t)
        {
            t = Mathf.Clamp01(t);
            if (!m_upThenSlightDown)
                return Mathf.Lerp(m_bottomY, m_topY, t);

            if (t <= 0.8f)
                return Mathf.Lerp(m_bottomY, m_topY, t / 0.8f);

            float tDown = (t - 0.8f) / 0.2f;
            return Mathf.Lerp(m_topY, Mathf.Lerp(m_topY, m_bottomY, 0.25f), tDown);
        }

        private void AppendCornerSamples(
            Vector3 fromRadial,
            Vector3 toRadial,
            int fromFace,
            int toFace,
            float t,
            ref float pathLength,
            ref Vector3 lastMid,
            ref bool hasLastMid)
        {
            Vector3 cornerRadial = WrapSurface.GetCornerRadial(m_cargo, fromFace, toFace);
            if (cornerRadial.sqrMagnitude < 1e-8f)
            {
                int midFace = WrapSurface.PickIntermediateFace(fromFace, toFace, fromRadial, toRadial);
                Vector3 c0 = WrapSurface.GetCornerRadial(m_cargo, fromFace, midFace);
                Vector3 c1 = WrapSurface.GetCornerRadial(m_cargo, midFace, toFace);
                if (c0.sqrMagnitude > 1e-8f)
                    AppendSample(t, c0, ref pathLength, ref lastMid, ref hasLastMid);
                if (c1.sqrMagnitude > 1e-8f)
                    AppendSample(t, c1, ref pathLength, ref lastMid, ref hasLastMid);
                return;
            }

            AppendSample(t, cornerRadial, ref pathLength, ref lastMid, ref hasLastMid);
        }

        private void AppendSample(
            float t,
            Vector3 radial,
            ref float pathLength,
            ref Vector3 lastMid,
            ref bool hasLastMid)
        {
            Vector3 nozzle = EvaluateNozzlePosition(t);
            Vector3 center = GetWrapCenterWorld();
            float localY = nozzle.y - center.y;

            SampleSurface(radial, localY, out Vector3 mid, out _);
            if (hasLastMid)
                pathLength += Vector3.Distance(mid, lastMid);

            _samples.Add(new WrapSample
            {
                t = t,
                pathLength = pathLength,
                nozzleWorld = nozzle,
                radialWorld = radial,
                localY = localY
            });

            lastMid = mid;
            hasLastMid = true;
        }

        private int GetSpanIndex(float t)
        {
            int n = _samples.Count;
            if (n < 2)
                return 0;
            if (t <= _samples[0].t)
                return 0;
            if (t >= _samples[n - 1].t)
                return n - 2;

            int lo = 0;
            int hi = n - 2;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (_samples[mid + 1].t < t)
                    lo = mid + 1;
                else if (_samples[mid].t > t)
                    hi = mid - 1;
                else
                    return mid;
            }

            return Mathf.Clamp(lo, 0, n - 2);
        }

        private void ApplyWorldBoundsAsCuboid(Bounds worldBounds)
        {
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            Vector3 c = worldBounds.center;
            Vector3 e = worldBounds.extents;
            Vector3[] corners =
            {
                c + new Vector3( e.x,  e.y,  e.z),
                c + new Vector3( e.x,  e.y, -e.z),
                c + new Vector3( e.x, -e.y,  e.z),
                c + new Vector3( e.x, -e.y, -e.z),
                c + new Vector3(-e.x,  e.y,  e.z),
                c + new Vector3(-e.x,  e.y, -e.z),
                c + new Vector3(-e.x, -e.y,  e.z),
                c + new Vector3(-e.x, -e.y, -e.z)
            };

            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 lp = m_cargo.InverseTransformPoint(corners[i]);
                min = Vector3.Min(min, lp);
                max = Vector3.Max(max, lp);
            }

            m_cuboidSize = max - min;
            m_cuboidCenterLocal = (min + max) * 0.5f;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_surfaceOffset = Mathf.Max(0.005f, m_surfaceOffset);
            m_cornerRadius = Mathf.Max(0f, m_cornerRadius);
            m_cuboidSize.x = Mathf.Max(0.01f, Mathf.Abs(m_cuboidSize.x));
            m_cuboidSize.y = Mathf.Max(0.01f, Mathf.Abs(m_cuboidSize.y));
            m_cuboidSize.z = Mathf.Max(0.01f, Mathf.Abs(m_cuboidSize.z));
            m_samplesPerRevolution = Mathf.Clamp(m_samplesPerRevolution, 16, 128);
            m_revolutions = Mathf.Clamp(m_revolutions, 1f, 12f);
            _baked = false;
        }

        private void OnDrawGizmosSelected()
        {
            if (m_cargo == null)
                return;

            Vector3 size = m_cuboidSize;
            size.x = Mathf.Max(0.01f, Mathf.Abs(size.x));
            size.y = Mathf.Max(0.01f, Mathf.Abs(size.y));
            size.z = Mathf.Max(0.01f, Mathf.Abs(size.z));

            Vector3 expanded = size + Vector3.one * (m_surfaceOffset * 2f);
            Matrix4x4 m = m_cargo.localToWorldMatrix
                          * Matrix4x4.TRS(m_cuboidCenterLocal, Quaternion.identity, Vector3.one);

            Gizmos.matrix = m;
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
            Gizmos.DrawWireCube(Vector3.zero, size);
            Gizmos.color = new Color(0.2f, 1f, 0.45f, 0.7f);
            Gizmos.DrawWireCube(Vector3.zero, expanded);
        }
#endif
    }
}
