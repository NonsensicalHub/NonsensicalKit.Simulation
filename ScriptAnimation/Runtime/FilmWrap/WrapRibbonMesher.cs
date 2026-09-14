using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 按路径样本前缀建膜：progress 增大追加，减小截断，可插值膜头。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class WrapRibbonMesher : MonoBehaviour
    {
        [Header("引用")]
        [InspectorLabel("缠绕路径")]
        [SerializeField] private WrapPath m_path;

        [InspectorLabel("喷嘴节点")]
        [SerializeField] private Transform m_nozzleTip;

        [Header("膜带参数")]
        [InspectorLabel("膜带宽度")]
        [SerializeField] private float m_filmWidth = 0.28f;

        [InspectorLabel("显示出膜带")]
        [SerializeField] private bool m_showFeedStrip = true;

        [InspectorLabel("出膜起点偏移")]
        [SerializeField] private Vector3 m_feedStartLocalOffset = new Vector3(-0.12f, 0f, -0.05f);

        private readonly List<Vector3> _vertices = new List<Vector3>(1024);
        private readonly List<Vector3> _normals = new List<Vector3>(1024);
        private readonly List<Vector2> _uvs = new List<Vector2>(1024);
        private readonly List<int> _triangles = new List<int>(2048);

        private readonly List<Vector3> _feedVertices = new List<Vector3>(8);
        private readonly List<Vector3> _feedNormals = new List<Vector3>(8);
        private readonly List<Vector2> _feedUvs = new List<Vector2>(8);
        private readonly List<int> _feedTriangles = new List<int>(12);

        private readonly List<CachedRing> _cache = new List<CachedRing>(512);

        private Mesh _mesh;
        private MeshFilter _filter;
        private Mesh _feedMesh;
        private MeshRenderer _feedRenderer;
        private int _cacheBakeVersion = -1;
        private float _cacheFilmWidth = -1f;
        private int _ringCount;

        private struct CachedRing
        {
            public Vector3 v0;
            public Vector3 v1;
            public Vector3 n;
            public float pathLength;
        }

        public int RingCount => _ringCount;
        public WrapPath Path => m_path;

        public void SetPath(WrapPath path) => m_path = path;

        public void SetNozzleTip(Transform tip) => m_nozzleTip = tip;

        private void Awake()
        {
            _filter = GetComponent<MeshFilter>();
            _mesh = new Mesh { name = "WrapRibbon" };
            _mesh.MarkDynamic();
            _filter.sharedMesh = _mesh;

            var renderer = GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            EnsureFeedMesh(renderer != null ? renderer.sharedMaterial : null);
        }

        private void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
            if (_feedMesh != null)
                Destroy(_feedMesh);
        }

        /// <summary>按路径参数 0~1 重建膜带前缀。progress=0 时清空。</summary>
        public void BuildUntil(float progress)
        {
            progress = Mathf.Clamp01(progress);
            if (m_path == null || progress <= 1e-6f)
            {
                ClearRibbon();
                UpdateFeedStrip(default, show: false);
                return;
            }

            m_path.EnsureBaked();
            EnsureRingCache();
            if (_cache.Count < 2)
            {
                ClearRibbon();
                UpdateFeedStrip(default, show: false);
                return;
            }

            int last = m_path.GetLastIndexAtOrBefore(progress);
            if (last < 0)
            {
                ClearRibbon();
                UpdateFeedStrip(default, show: false);
                return;
            }

            WrapSample tip = m_path.Evaluate(progress);
            bool atFinal = last >= m_path.SampleCount - 1;
            bool onSample = Mathf.Abs(m_path.Samples[last].t - progress) <= 1e-5f;

            _vertices.Clear();
            _normals.Clear();
            _uvs.Clear();
            _triangles.Clear();

            int committed = Mathf.Min(last + 1, _cache.Count);
            for (int i = 0; i < committed; i++)
                AddRing(_cache[i].v0, _cache[i].v1, _cache[i].n, _cache[i].pathLength);

            if (!atFinal && (!onSample || committed < 2))
            {
                SampleRing(tip, out Vector3 v0, out Vector3 v1, out Vector3 n);
                AddRing(v0, v1, n, tip.pathLength);
            }

            _ringCount = _vertices.Count / 2;
            UploadMesh();
            UpdateFeedStrip(tip, show: true);
        }

        public void ClearRibbon()
        {
            _vertices.Clear();
            _normals.Clear();
            _uvs.Clear();
            _triangles.Clear();
            _ringCount = 0;
            UploadMesh();
        }

        private void EnsureRingCache()
        {
            if (m_path == null)
                return;
            if (_cacheBakeVersion == m_path.BakeVersion && Mathf.Abs(_cacheFilmWidth - m_filmWidth) < 1e-6f)
                return;

            _cache.Clear();
            IReadOnlyList<WrapSample> samples = m_path.Samples;
            for (int i = 0; i < samples.Count; i++)
            {
                SampleRing(samples[i], out Vector3 v0, out Vector3 v1, out Vector3 n);
                _cache.Add(new CachedRing
                {
                    v0 = v0,
                    v1 = v1,
                    n = n,
                    pathLength = samples[i].pathLength
                });
            }

            _cacheBakeVersion = m_path.BakeVersion;
            _cacheFilmWidth = m_filmWidth;
        }

        private void SampleRing(WrapSample sample, out Vector3 v0, out Vector3 v1, out Vector3 n)
        {
            float halfW = m_filmWidth * 0.5f;
            m_path.SampleSurface(sample.radialWorld, sample.localY + halfW, out v0, out Vector3 n0);
            m_path.SampleSurface(sample.radialWorld, sample.localY - halfW, out v1, out Vector3 n1);
            n = n0 + n1;
            if (n.sqrMagnitude < 1e-8f)
                n = sample.radialWorld;
            else
                n.Normalize();
        }

        private void AddRing(Vector3 v0, Vector3 v1, Vector3 n, float pathLength)
        {
            int baseIndex = _vertices.Count;
            _vertices.Add(transform.InverseTransformPoint(v0));
            _vertices.Add(transform.InverseTransformPoint(v1));
            Vector3 localN = transform.InverseTransformDirection(n);
            _normals.Add(localN);
            _normals.Add(localN);
            _uvs.Add(new Vector2(pathLength, 1f));
            _uvs.Add(new Vector2(pathLength, 0f));

            if (baseIndex >= 2)
            {
                int i0 = baseIndex - 2;
                int i1 = baseIndex - 1;
                int i2 = baseIndex;
                int i3 = baseIndex + 1;
                _triangles.Add(i0);
                _triangles.Add(i2);
                _triangles.Add(i1);
                _triangles.Add(i1);
                _triangles.Add(i2);
                _triangles.Add(i3);
            }
        }

        private void UploadMesh()
        {
            if (_mesh == null)
                return;

            if (_vertices.Count < 2)
            {
                _mesh.Clear();
                return;
            }

            _mesh.Clear(false);
            _mesh.SetVertices(_vertices);
            _mesh.SetNormals(_normals);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetTriangles(_triangles, 0, false);
            _mesh.RecalculateBounds();
        }

        private void EnsureFeedMesh(Material filmMaterial)
        {
            Transform feedTf = transform.Find("FilmFeed");
            GameObject feedGo;
            if (feedTf == null)
            {
                feedGo = new GameObject("FilmFeed", typeof(MeshFilter), typeof(MeshRenderer));
                feedGo.transform.SetParent(transform, false);
            }
            else
            {
                feedGo = feedTf.gameObject;
            }

            var filter = feedGo.GetComponent<MeshFilter>();
            if (filter == null)
                filter = feedGo.AddComponent<MeshFilter>();

            _feedRenderer = feedGo.GetComponent<MeshRenderer>();
            if (_feedRenderer == null)
                _feedRenderer = feedGo.AddComponent<MeshRenderer>();

            if (filmMaterial != null)
                _feedRenderer.sharedMaterial = filmMaterial;
            _feedRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _feedRenderer.receiveShadows = false;

            if (_feedMesh == null)
            {
                _feedMesh = new Mesh { name = "FilmFeed" };
                _feedMesh.MarkDynamic();
            }

            filter.sharedMesh = _feedMesh;
        }

        private void UpdateFeedStrip(WrapSample tip, bool show)
        {
            show = show && m_showFeedStrip && m_nozzleTip != null && m_path != null;
            if (_feedRenderer != null)
                _feedRenderer.enabled = show;
            if (!show || _feedMesh == null)
                return;

            float halfW = m_filmWidth * 0.5f;
            m_path.SampleSurface(tip.radialWorld, tip.localY + halfW, out Vector3 top, out Vector3 n0);
            m_path.SampleSurface(tip.radialWorld, tip.localY - halfW, out Vector3 bot, out Vector3 n1);
            Vector3 contactMid = (top + bot) * 0.5f;
            Vector3 contactN = n0 + n1;
            if (contactN.sqrMagnitude < 1e-8f)
                contactN = tip.radialWorld;
            contactN.Normalize();

            Vector3 start = m_nozzleTip.TransformPoint(m_feedStartLocalOffset);
            Vector3 up = Vector3.up * halfW;
            Vector3 radial = tip.radialWorld;
            Vector3 mid = (start + contactMid) * 0.5f + radial * 0.12f;
            Vector3 feedSide = Vector3.Cross(contactMid - start, Vector3.up);
            if (feedSide.sqrMagnitude < 1e-8f)
                feedSide = contactN;
            else
                feedSide.Normalize();
            if (Vector3.Dot(feedSide, radial) < 0f)
                feedSide = -feedSide;

            _feedVertices.Clear();
            _feedNormals.Clear();
            _feedUvs.Clear();
            _feedTriangles.Clear();

            AddFeedRing(start, feedSide, up, 0f);
            AddFeedRing(mid, Vector3.Normalize(feedSide + contactN), up, 0.5f);
            AddFeedRing(contactMid, contactN, up, 1f);

            _feedTriangles.Add(0);
            _feedTriangles.Add(2);
            _feedTriangles.Add(1);
            _feedTriangles.Add(1);
            _feedTriangles.Add(2);
            _feedTriangles.Add(3);
            _feedTriangles.Add(2);
            _feedTriangles.Add(4);
            _feedTriangles.Add(3);
            _feedTriangles.Add(3);
            _feedTriangles.Add(4);
            _feedTriangles.Add(5);

            _feedMesh.Clear(false);
            _feedMesh.SetVertices(_feedVertices);
            _feedMesh.SetNormals(_feedNormals);
            _feedMesh.SetUVs(0, _feedUvs);
            _feedMesh.SetTriangles(_feedTriangles, 0, false);
            _feedMesh.RecalculateBounds();
        }

        private void AddFeedRing(Vector3 mid, Vector3 normal, Vector3 up, float u)
        {
            _feedVertices.Add(transform.InverseTransformPoint(mid + up));
            _feedVertices.Add(transform.InverseTransformPoint(mid - up));
            Vector3 localN = transform.InverseTransformDirection(normal);
            _feedNormals.Add(localN);
            _feedNormals.Add(localN);
            _feedUvs.Add(new Vector2(u, 1f));
            _feedUvs.Add(new Vector2(u, 0f));
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_filmWidth = Mathf.Max(0.02f, m_filmWidth);
            _cacheBakeVersion = -1;
        }
#endif
    }
}
