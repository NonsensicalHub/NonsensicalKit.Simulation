using System.Collections.Generic;
using NonsensicalKit.ScriptAnimation;
using UnityEngine;
using UnityEngine.Rendering;

namespace NonsensicalKit.ScriptAnimation.Demo
{
    public enum FilmSurfaceMode
    {
        [InspectorName("射线贴合")]
        Raycast = 0,
        [InspectorName("立方体预设")]
        Cuboid = 1
    }

    /// <summary>
    /// 缠膜生成（与运动解耦）：跟踪喷嘴位移追加膜段；静止则停止生成。
    /// </summary>
    [DisallowMultipleComponent]
    public class FilmRibbonBuilder : MonoBehaviour
    {
        [Header("包裹目标")]
        [InspectorLabel("货物根节点")]
        [SerializeField] private Transform m_cargo;

        [InspectorLabel("射线起点距离")]
        [SerializeField] private float m_rayStartDistance = 2.5f;

        [SerializeField, InspectorLabel("膜节点")] private Transform m_ribbonModel;

        [Header("贴合表面")]
        [InspectorLabel("贴合表面")]
        [SerializeField] private bool m_fitToSurface = true;

        [InspectorLabel("贴合模式")]
        [SerializeField] private FilmSurfaceMode m_surfaceMode = FilmSurfaceMode.Cuboid;

        [InspectorLabel("表面外扩")]
        [SerializeField] [Min(0.005f)] private float m_surfaceOffset = 0.04f;

        [InspectorLabel("立方体尺寸 XYZ)")]
        [Tooltip("货物本地空间的完整长宽高（米）。缠膜对象多为立方体时推荐手动设定或从 Bounds 同步。")]
        [SerializeField] private Vector3 m_cuboidSize = new Vector3(1f, 1.2f, 1f);

        [InspectorLabel("尺寸中心偏移")]
        [Tooltip("相对货物根节点的本地中心偏移；枢轴不在几何中心时调整")]
        [SerializeField] private Vector3 m_cuboidCenterLocal = Vector3.zero;

        [InspectorLabel("转角圆角半径")]
        [Tooltip("水平截面圆角，避免转角弦切穿模。实际生效值不会超过「表面外扩」，以免切角穿模。设为 0 则用尖角并自动插入角点。")]
        [SerializeField, Min(0f)] private float m_cornerRadius = 0.04f;

        [Header("膜带参数")]
        [InspectorLabel("膜带宽度")]
        [SerializeField] private float m_filmWidth = 0.28f;

        [Header("喷嘴跟踪")]
        [InspectorLabel("喷嘴节点")]
        [SerializeField] private Transform m_nozzleTip;

        [InspectorLabel("启用缠膜")]
        [SerializeField] private bool m_wrappingEnabled = true;

        [InspectorLabel("最小追加距离")]
        [SerializeField] [Min(0.01f)] private float m_minSegmentDistance = 0.08f;

        [InspectorLabel("静止速度阈值")]
        [SerializeField] [Min(0f)] private float m_stopSpeedThreshold = 0.05f;

        [InspectorLabel("瞬移判定距离")]
        [SerializeField] private float m_teleportDistance = 0.75f;

        [Header("喷嘴出膜")]
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

        private Mesh _mesh;
        private MeshFilter _filter;
        private Mesh _feedMesh;
        private MeshRenderer _feedRenderer;
        private Collider[] _cargoColliders;

        private Vector3 _lastNozzlePos;
        private Vector3 _lastSamplePos;
        private Vector3 _lastContactMid;
        private Vector3 _lastContactNormal;
        private Vector3 _lastRadial;
        private int _lastFace;
        private bool _hasLastNozzlePos;
        private bool _hasLastSamplePos;
        private bool _hasContact;
        private bool _meshDirty;
        private float _pathLength;
        private float _minSegSqr;
        private float _teleportSqr;
        private float _stopSpeed;
        private bool _isNozzleMoving;

        public Transform Cargo => m_cargo;
        public Transform NozzleTip => m_nozzleTip;
        public Vector3 CuboidSize => m_cuboidSize;
        public FilmSurfaceMode SurfaceMode => m_surfaceMode;

        private bool IsCuboidMode => m_surfaceMode == FilmSurfaceMode.Cuboid;

        public bool WrappingEnabled
        {
            get => m_wrappingEnabled;
            set => m_wrappingEnabled = value;
        }

        public bool IsNozzleMoving => _isNozzleMoving;
        public float PathLength => _pathLength;
        public int RingCount => _hasLastSamplePos ? _vertices.Count / 2 : 0;

        public void SetCargo(Transform cargo)
        {
            m_cargo = cargo;
            CacheCargoColliders();
        }

        public void SetNozzleTip(Transform tip) => m_nozzleTip = tip;

        public void SetCuboidSize(Vector3 size, Vector3 centerLocal = default)
        {
            m_cuboidSize = new Vector3(
                Mathf.Max(0.01f, Mathf.Abs(size.x)),
                Mathf.Max(0.01f, Mathf.Abs(size.y)),
                Mathf.Max(0.01f, Mathf.Abs(size.z)));
            m_cuboidCenterLocal = centerLocal;
            m_surfaceMode = FilmSurfaceMode.Cuboid;
            m_rayStartDistance = Mathf.Max(m_cuboidSize.x, m_cuboidSize.z) * 0.5f + 1.2f;
        }

        private void Awake()
        {
            CacheThresholds();
            CreateNewRibbon();

            var hostRenderer = GetComponent<MeshRenderer>();
            var ribbonRenderer = m_ribbonModel.GetComponent<MeshRenderer>();
            if (ribbonRenderer != null)
            {
                if (hostRenderer != null && hostRenderer.sharedMaterial != null)
                    ribbonRenderer.sharedMaterial = hostRenderer.sharedMaterial;
                ribbonRenderer.shadowCastingMode = ShadowCastingMode.Off;
                ribbonRenderer.receiveShadows = false;
                if (hostRenderer != null)
                    hostRenderer.enabled = false;
            }

            EnsureFeedMesh(ribbonRenderer != null ? ribbonRenderer.sharedMaterial : null);
            CacheCargoColliders();
            AutoFitFromCargoBounds();
        }

        private void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
            if (_feedMesh != null)
                Destroy(_feedMesh);
        }

        private void LateUpdate()
        {
            TrackNozzleAndWrap();
            if (_meshDirty)
                ApplyMesh();
        }

        public void ResetWrap()
        {
            _vertices.Clear();
            _normals.Clear();
            _uvs.Clear();
            _triangles.Clear();
            _pathLength = 0f;
            _hasLastSamplePos = false;
            _hasLastNozzlePos = false;
            _hasContact = false;
            _lastFace = -1;
            _meshDirty = true;

            if (m_nozzleTip != null)
            {
                _lastNozzlePos = m_nozzleTip.position;
                _hasLastNozzlePos = true;
            }

            ApplyMesh();
            UpdateFeedStrip(force: true);
        }

        [ContextMenu("创建新缠膜")]
        public void CreateNewRibbon()
        {
            if (m_ribbonModel == null)
            {
                m_ribbonModel = new GameObject("Ribbon", typeof(MeshRenderer), typeof(MeshFilter)).transform;
                m_ribbonModel.SetParent(transform, false);
                m_ribbonModel.localPosition = Vector3.zero;
                m_ribbonModel.localRotation = Quaternion.identity;
                m_ribbonModel.localScale = Vector3.one;
            }

            _filter = m_ribbonModel.GetComponent<MeshFilter>();
            if (_filter == null)
                _filter = m_ribbonModel.gameObject.AddComponent<MeshFilter>();

            _mesh = new Mesh { name = "FilmRibbon" };
            _mesh.MarkDynamic();
            _filter.sharedMesh = _mesh;

            _vertices.Clear();
            _normals.Clear();
            _uvs.Clear();
            _triangles.Clear();
            _pathLength = 0f;
            _hasLastSamplePos = false;
            _hasContact = false;
            _lastFace = -1;
            _meshDirty = false;
        }

        /// <summary>将当前缠膜网格复制一份，挂到 Cargo 下（本地空间对齐货物）。</summary>
        [ContextMenu("保存缠膜到货物")]
        public void SaveRibbonToCargo()
        {
            if (m_cargo == null || m_ribbonModel == null)
                return;

            if (_meshDirty)
                ApplyMesh();

            if (_mesh == null || _mesh.vertexCount < 2)
                return;

            Mesh copy = Instantiate(_mesh);
            copy.name = "FilmRibbonSaved";

            // 顶点按 Builder.transform 本地写入，转到 Cargo 本地，使子节点可随货物移动
            Vector3[] verts = copy.vertices;
            Vector3[] norms = copy.normals;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 world = transform.TransformPoint(verts[i]);
                verts[i] = m_cargo.InverseTransformPoint(world);
            }

            if (norms != null && norms.Length == verts.Length)
            {
                for (int i = 0; i < norms.Length; i++)
                {
                    Vector3 worldN = transform.TransformDirection(norms[i]);
                    norms[i] = m_cargo.InverseTransformDirection(worldN);
                }

                copy.normals = norms;
            }

            copy.vertices = verts;
            copy.RecalculateBounds();

            var saved = new GameObject("Ribbon");
            saved.transform.SetParent(m_cargo, false);
            saved.transform.localPosition = Vector3.zero;
            saved.transform.localRotation = Quaternion.identity;
            saved.transform.localScale = Vector3.one;

            var mf = saved.AddComponent<MeshFilter>();
            mf.sharedMesh = copy;

            var srcMr = m_ribbonModel.GetComponent<MeshRenderer>();
            var mr = saved.AddComponent<MeshRenderer>();
            if (srcMr != null)
            {
                mr.sharedMaterials = srcMr.sharedMaterials;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }

        [ContextMenu("完成并开始新缠膜")]
        public void FillRibbonComplete()
        {
            SaveRibbonToCargo();
            CreateNewRibbon();
        }

        private void CacheThresholds()
        {
            _minSegSqr = m_minSegmentDistance * m_minSegmentDistance;
            _teleportSqr = m_teleportDistance * m_teleportDistance;
            _stopSpeed = m_stopSpeedThreshold;
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

        private void TrackNozzleAndWrap()
        {
            if (m_nozzleTip == null)
            {
                _isNozzleMoving = false;
                if (_feedRenderer != null)
                    _feedRenderer.enabled = false;
                return;
            }

            Vector3 nozzlePos = m_nozzleTip.position;
            float dt = Time.deltaTime;
            if (dt < 1e-6f)
                dt = 1e-6f;

            if (!_hasLastNozzlePos)
            {
                _lastNozzlePos = nozzlePos;
                _hasLastNozzlePos = true;
                _isNozzleMoving = false;
                UpdateFeedStrip(force: true);
                return;
            }

            Vector3 delta = nozzlePos - _lastNozzlePos;
            float moveSqr = delta.sqrMagnitude;

            if (moveSqr >= _teleportSqr)
            {
                _lastNozzlePos = nozzlePos;
                _lastSamplePos = nozzlePos;
                _hasLastSamplePos = false;
                _hasContact = false;
                _lastFace = -1;
                _isNozzleMoving = false;
                UpdateFeedStrip(force: true);
                return;
            }

            float speed = Mathf.Sqrt(moveSqr) / dt;
            _isNozzleMoving = speed >= _stopSpeed;

            bool appended = false;
            if (m_wrappingEnabled && _isNozzleMoving && m_cargo != null)
            {
                if (!_hasLastSamplePos)
                {
                    AppendRing(nozzlePos);
                    _lastSamplePos = nozzlePos;
                    _hasLastSamplePos = true;
                    appended = true;
                }
                else if ((nozzlePos - _lastSamplePos).sqrMagnitude >= _minSegSqr)
                {
                    _pathLength += Vector3.Distance(nozzlePos, _lastSamplePos);
                    AppendRing(nozzlePos);
                    _lastSamplePos = nozzlePos;
                    appended = true;
                }
            }

            _lastNozzlePos = nozzlePos;
            // 出膜带：追加段时必刷；否则仅在移动时刷，静止跳过
            if (appended || _isNozzleMoving)
                UpdateFeedStrip(force: appended);
        }

        private void AppendRing(Vector3 nozzlePos)
        {
            Vector3 center = GetWrapCenterWorld();
            Vector3 radial = nozzlePos - center;
            radial.y = 0f;
            if (radial.sqrMagnitude < 1e-8f)
                radial = Vector3.right;
            else
                radial.Normalize();

            float halfW = m_filmWidth * 0.5f;
            float localY = nozzlePos.y - center.y;

            // 立方体尖角模式：跨面时先插入转角环，避免弦切穿模
            if (m_fitToSurface
                && m_surfaceMode == FilmSurfaceMode.Cuboid
                && m_cornerRadius <= 1e-5f
                && _hasLastSamplePos
                && _lastFace >= 0)
            {
                int face = GetCuboidFace(radial);
                if (face != _lastFace)
                    AppendCornerBridge(_lastRadial, radial, localY, _lastFace, face);
            }

            AddRingSamples(radial, localY + halfW, localY - halfW);
            _lastRadial = radial;
            _lastFace = GetCuboidFace(radial);
        }

        private void AppendCornerBridge(Vector3 fromRadial, Vector3 toRadial, float localY, int fromFace, int toFace)
        {
            float halfW = m_filmWidth * 0.5f;
            Vector3 cornerRadial = GetCornerRadial(fromFace, toFace);
            if (cornerRadial.sqrMagnitude < 1e-8f)
            {
                // 对角跳面：补两个角
                int midFace = PickIntermediateFace(fromFace, toFace, fromRadial, toRadial);
                Vector3 c0 = GetCornerRadial(fromFace, midFace);
                Vector3 c1 = GetCornerRadial(midFace, toFace);
                if (c0.sqrMagnitude > 1e-8f)
                    AddRingSamples(c0, localY + halfW, localY - halfW);
                if (c1.sqrMagnitude > 1e-8f)
                    AddRingSamples(c1, localY + halfW, localY - halfW);
                return;
            }

            AddRingSamples(cornerRadial, localY + halfW, localY - halfW);
        }

        private void AddRingSamples(Vector3 radial, float y0, float y1)
        {
            SampleSurface(radial, y0, out Vector3 v0, out Vector3 n0);
            SampleSurface(radial, y1, out Vector3 v1, out Vector3 n1);

            Vector3 n = n0 + n1;
            if (n.sqrMagnitude < 1e-8f)
                n = radial;
            n.Normalize();

            _lastContactMid = (v0 + v1) * 0.5f;
            _lastContactNormal = n;
            _hasContact = true;

            int baseIndex = _vertices.Count;
            _vertices.Add(transform.InverseTransformPoint(v0));
            _vertices.Add(transform.InverseTransformPoint(v1));
            Vector3 localN = transform.InverseTransformDirection(n);
            _normals.Add(localN);
            _normals.Add(localN);
            _uvs.Add(new Vector2(_pathLength, 1f));
            _uvs.Add(new Vector2(_pathLength, 0f));

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

            _meshDirty = true;
        }

        private void CacheCargoColliders()
        {
            _cargoColliders = m_cargo != null ? m_cargo.GetComponentsInChildren<Collider>() : null;
        }

        private Vector3 GetWrapCenterWorld()
        {
            if (m_cargo == null)
                return Vector3.zero;
            if (m_surfaceMode == FilmSurfaceMode.Cuboid)
                return m_cargo.TransformPoint(m_cuboidCenterLocal);
            return m_cargo.position;
        }

        /// <summary>水平径向向内采样货物表面。</summary>
        private void SampleSurface(Vector3 radial, float localY, out Vector3 point, out Vector3 normal)
        {
            if (m_fitToSurface && m_surfaceMode == FilmSurfaceMode.Cuboid)
            {
                SampleCuboidSurface(radial, localY, out point, out normal);
                return;
            }

            Vector3 center = GetWrapCenterWorld();
            float startDist = m_rayStartDistance > 1.5f ? m_rayStartDistance : 1.5f;
            Vector3 probe = new Vector3(center.x, center.y + localY, center.z) + radial * startDist;

            if (m_fitToSurface)
            {
                if (_cargoColliders == null || _cargoColliders.Length == 0)
                    CacheCargoColliders();

                if (_cargoColliders != null && _cargoColliders.Length > 0)
                {
                    Ray ray = new Ray(probe, -radial);
                    float bestDist = float.MaxValue;
                    RaycastHit bestHit = default;
                    bool hitAny = false;

                    for (int i = 0; i < _cargoColliders.Length; i++)
                    {
                        Collider col = _cargoColliders[i];
                        if (col == null || !col.enabled)
                            continue;
                        if (col.Raycast(ray, out RaycastHit hit, startDist + 2f) && hit.distance < bestDist)
                        {
                            bestDist = hit.distance;
                            bestHit = hit;
                            hitAny = true;
                        }
                    }

                    if (hitAny)
                    {
                        normal = bestHit.normal;
                        if (normal.sqrMagnitude < 1e-8f)
                            normal = radial;
                        else
                            normal.Normalize();
                        if (Vector3.Dot(normal, radial) < 0f)
                            normal = -normal;
                        point = bestHit.point + normal * m_surfaceOffset;
                        return;
                    }

                    float bestSqr = float.MaxValue;
                    Vector3 bestPoint = probe;
                    bool found = false;
                    for (int i = 0; i < _cargoColliders.Length; i++)
                    {
                        Collider col = _cargoColliders[i];
                        if (col == null || !col.enabled)
                            continue;
                        Vector3 cp = col.ClosestPoint(probe);
                        float sqr = (cp - probe).sqrMagnitude;
                        if (sqr < bestSqr)
                        {
                            bestSqr = sqr;
                            bestPoint = cp;
                            found = true;
                        }
                    }

                    if (found)
                    {
                        Vector3 outward = probe - bestPoint;
                        outward.y = 0f;
                        normal = outward.sqrMagnitude > 1e-8f ? outward.normalized : radial;
                        point = bestPoint + normal * m_surfaceOffset;
                        return;
                    }
                }
            }

            float r = Mathf.Max(0.2f, startDist * 0.35f);
            point = new Vector3(center.x + radial.x * r, center.y + localY, center.z + radial.z * r);
            normal = radial;
        }

        /// <summary>在货物本地空间按预设立方体 + 圆角解析采样，避免转角穿模。</summary>
        private void SampleCuboidSurface(Vector3 worldRadial, float localY, out Vector3 point, out Vector3 normal)
        {
            Vector3 size = m_cuboidSize;
            size.x = Mathf.Max(0.01f, Mathf.Abs(size.x));
            size.y = Mathf.Max(0.01f, Mathf.Abs(size.y));
            size.z = Mathf.Max(0.01f, Mathf.Abs(size.z));

            float hx = size.x * 0.5f + m_surfaceOffset;
            float hz = size.z * 0.5f + m_surfaceOffset;
            // 圆角半径不超过外扩，避免切角后反穿入货物本体
            float radius = Mathf.Min(m_cornerRadius, m_surfaceOffset);
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(hx, hz) - 0.001f);

            Vector3 localDir = m_cargo != null
                ? m_cargo.InverseTransformDirection(worldRadial)
                : worldRadial;
            localDir.y = 0f;
            if (localDir.sqrMagnitude < 1e-8f)
                localDir = Vector3.right;
            else
                localDir.Normalize();

            Vector2 dir2 = new Vector2(localDir.x, localDir.z);
            Vector2 p2 = PointOnRoundedRect(dir2, hx, hz, radius);
            Vector2 n2 = NormalOnRoundedRect(p2, hx, hz, radius);

            // 高度限制在立方体上下面外扩范围内，避免膜带钻入顶部
            float hy = size.y * 0.5f + m_surfaceOffset;
            float clampedY = Mathf.Clamp(localY, -hy, hy);

            Vector3 localPoint = m_cuboidCenterLocal + new Vector3(p2.x, clampedY, p2.y);
            Vector3 localNormal = new Vector3(n2.x, 0f, n2.y);
            if (localNormal.sqrMagnitude < 1e-8f)
                localNormal = localDir;
            else
                localNormal.Normalize();

            if (m_cargo != null)
            {
                point = m_cargo.TransformPoint(localPoint);
                normal = m_cargo.TransformDirection(localNormal).normalized;
            }
            else
            {
                point = localPoint;
                normal = localNormal;
            }
        }

        private static Vector2 PointOnRoundedRect(Vector2 dir, float hx, float hz, float radius)
        {
            dir.Normalize();
            float ax = Mathf.Abs(dir.x);
            float az = Mathf.Abs(dir.y);

            if (radius <= 1e-6f)
            {
                float t = Mathf.Min(
                    hx / Mathf.Max(ax, 1e-8f),
                    hz / Mathf.Max(az, 1e-8f));
                return dir * t;
            }

            float ix = hx - radius;
            float iz = hz - radius;

            if (ax > 1e-8f)
            {
                float t = hx / ax;
                float z = t * dir.y;
                if (Mathf.Abs(z) <= iz + 1e-5f)
                    return new Vector2(Mathf.Sign(dir.x) * hx, z);
            }

            if (az > 1e-8f)
            {
                float t = hz / az;
                float x = t * dir.x;
                if (Mathf.Abs(x) <= ix + 1e-5f)
                    return new Vector2(x, Mathf.Sign(dir.y) * hz);
            }

            Vector2 corner = new Vector2(Mathf.Sign(dir.x) * ix, Mathf.Sign(dir.y) * iz);
            float b = Vector2.Dot(dir, corner);
            float disc = b * b - (corner.sqrMagnitude - radius * radius);
            float tHit = b + Mathf.Sqrt(Mathf.Max(0f, disc));
            return dir * tHit;
        }

        private static Vector2 NormalOnRoundedRect(Vector2 point, float hx, float hz, float radius)
        {
            if (radius > 1e-6f)
            {
                float ix = hx - radius;
                float iz = hz - radius;
                if (Mathf.Abs(point.x) > ix - 1e-4f && Mathf.Abs(point.y) > iz - 1e-4f)
                {
                    Vector2 c = new Vector2(Mathf.Sign(point.x) * ix, Mathf.Sign(point.y) * iz);
                    Vector2 n = point - c;
                    if (n.sqrMagnitude > 1e-8f)
                        return n.normalized;
                }
            }

            if (Mathf.Abs(point.x) / Mathf.Max(hx, 1e-8f) >= Mathf.Abs(point.y) / Mathf.Max(hz, 1e-8f))
                return new Vector2(Mathf.Sign(point.x), 0f);
            return new Vector2(0f, Mathf.Sign(point.y));
        }

        /// <summary>0=+X, 1=-X, 2=+Z, 3=-Z（货物本地水平面）。</summary>
        private int GetCuboidFace(Vector3 worldRadial)
        {
            Vector3 local = m_cargo != null
                ? m_cargo.InverseTransformDirection(worldRadial)
                : worldRadial;
            local.y = 0f;
            if (local.sqrMagnitude < 1e-8f)
                return 0;
            local.Normalize();

            float hx = Mathf.Max(0.01f, Mathf.Abs(m_cuboidSize.x) * 0.5f) + m_surfaceOffset;
            float hz = Mathf.Max(0.01f, Mathf.Abs(m_cuboidSize.z) * 0.5f) + m_surfaceOffset;
            float tx = hx / Mathf.Max(Mathf.Abs(local.x), 1e-8f);
            float tz = hz / Mathf.Max(Mathf.Abs(local.z), 1e-8f);
            if (tx <= tz)
                return local.x >= 0f ? 0 : 1;
            return local.z >= 0f ? 2 : 3;
        }

        private Vector3 GetCornerRadial(int faceA, int faceB)
        {
            float sx = 0f;
            float sz = 0f;
            ApplyFaceSign(faceA, ref sx, ref sz);
            ApplyFaceSign(faceB, ref sx, ref sz);
            if (Mathf.Abs(sx) < 0.5f || Mathf.Abs(sz) < 0.5f)
                return Vector3.zero;

            Vector3 local = new Vector3(sx, 0f, sz).normalized;
            return m_cargo != null ? m_cargo.TransformDirection(local) : local;
        }

        private static void ApplyFaceSign(int face, ref float sx, ref float sz)
        {
            switch (face)
            {
                case 0: sx = 1f; break;
                case 1: sx = -1f; break;
                case 2: sz = 1f; break;
                case 3: sz = -1f; break;
            }
        }

        private static int PickIntermediateFace(int fromFace, int toFace, Vector3 fromRadial, Vector3 toRadial)
        {
            // 选择与转角扫掠方向更一致的中间面
            Vector3 cross = Vector3.Cross(fromRadial, toRadial);
            bool ccw = cross.y >= 0f;
            // 面顺序 CCW: +X(0) -> +Z(2) -> -X(1) -> -Z(3)
            int[] order = { 0, 2, 1, 3 };
            int fi = System.Array.IndexOf(order, fromFace);
            if (fi < 0)
                return toFace;
            return ccw ? order[(fi + 1) % 4] : order[(fi + 3) % 4];
        }

        private void ApplyMesh()
        {
            _meshDirty = false;
            if (_mesh == null)
                return;

            if (_vertices.Count == 0)
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

        private void UpdateFeedStrip(bool force)
        {
            bool show = m_showFeedStrip && m_wrappingEnabled && m_nozzleTip != null && m_cargo != null;
            if (_feedRenderer != null)
                _feedRenderer.enabled = show;
            if (!show || _feedMesh == null)
                return;

            Vector3 nozzlePos = m_nozzleTip.position;
            Vector3 center = GetWrapCenterWorld();
            Vector3 radial = nozzlePos - center;
            radial.y = 0f;
            if (radial.sqrMagnitude < 1e-8f)
                radial = Vector3.right;
            else
                radial.Normalize();

            Vector3 contactMid;
            Vector3 contactN;
            if (_hasContact && !force)
            {
                contactMid = _lastContactMid;
                contactN = _lastContactNormal;
            }
            else
            {
                float halfW = m_filmWidth * 0.5f;
                float localY = nozzlePos.y - center.y;
                SampleSurface(radial, localY + halfW, out Vector3 top, out Vector3 n0);
                SampleSurface(radial, localY - halfW, out Vector3 bot, out Vector3 n1);
                contactMid = (top + bot) * 0.5f;
                contactN = n0 + n1;
                if (contactN.sqrMagnitude < 1e-8f)
                    contactN = radial;
                contactN.Normalize();
                _lastContactMid = contactMid;
                _lastContactNormal = contactN;
                _hasContact = true;
            }

            Vector3 start = m_nozzleTip.TransformPoint(m_feedStartLocalOffset);
            float half = m_filmWidth * 0.5f;
            Vector3 up = Vector3.up * half;

            // 两段折线：喷嘴 → 中点外扩 → 接触，避免每点 ClosestPoint
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

        [ContextMenu("从货物同步立方体尺寸")]
        public void SyncCuboidSizeFromCargo()
        {
            AutoFitFromCargoBounds();
        }

        public void AutoFitFromCargoBounds()
        {
            if (m_cargo == null)
                return;

            var renderers = m_cargo.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
            {
                // 无 Renderer 时用 Collider 兜底
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
            m_rayStartDistance = Mathf.Max(m_cuboidSize.x, m_cuboidSize.z) * 0.5f + 1.2f;
            m_surfaceMode = FilmSurfaceMode.Cuboid;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_surfaceOffset = Mathf.Max(0.005f, m_surfaceOffset);
            m_minSegmentDistance = Mathf.Max(0.01f, m_minSegmentDistance);
            m_cornerRadius = Mathf.Max(0f, m_cornerRadius);
            m_cuboidSize.x = Mathf.Max(0.01f, Mathf.Abs(m_cuboidSize.x));
            m_cuboidSize.y = Mathf.Max(0.01f, Mathf.Abs(m_cuboidSize.y));
            m_cuboidSize.z = Mathf.Max(0.01f, Mathf.Abs(m_cuboidSize.z));
            CacheThresholds();
        }

        private void OnDrawGizmosSelected()
        {
            if (m_cargo == null || m_surfaceMode != FilmSurfaceMode.Cuboid)
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
