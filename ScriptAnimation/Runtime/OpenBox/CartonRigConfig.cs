using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>生成中文节点树 + 占位网格；Scene 手柄调尺寸。</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("ScriptAnimation/纸箱节点树 (CartonRigConfig)")]
    public class CartonRigConfig : MonoBehaviour
    {
        const string VisualName = "Visual";
        const float MinSize = 0.05f;

        [Header("折叠驱动")]
        [SerializeField] private OpenBoxAnim m_carton;

        [Header("箱尺寸")]
        [SerializeField] private float m_width = 1f;
        [SerializeField] private float m_depth = 1f;
        [SerializeField] private float m_height = 1f;
        [SerializeField] private float m_thickness = 0.02f;
        [SerializeField] [Range(0.25f, 0.55f)] private float m_longFlapRatio = 0.5f;
        [SerializeField] [Range(0.15f, 0.5f)] private float m_shortFlapRatio = 0.4f;
        [SerializeField] private Material m_panelMaterial;

        [Header("辅助线")]
        [SerializeField] private bool m_drawGizmos = true;
        [SerializeField] private bool m_drawPanelFrames = true;
        [SerializeField] private bool m_drawSizeHandles = true;
        [SerializeField] private Color m_creaseColor = new Color(1f, 0.55f, 0.1f, 1f);
        [SerializeField] private Color m_panelColor = new Color(0.4f, 0.75f, 1f, 0.35f);
        [SerializeField] private Color m_handleColor = new Color(0.2f, 0.9f, 0.45f, 1f);

        [SerializeField] private Transform m_rigRoot;
        [SerializeField] private List<FaceSlot> m_slots = new List<FaceSlot>(12);
        [SerializeField] private List<CreaseGizmo> m_creases = new List<CreaseGizmo>(11);

        // 拖拽预览尺寸（未松手时不重建）
        [NonSerialized] float _previewW, _previewD, _previewH;
        [NonSerialized] bool _hasPreview;

        public float Width => m_width;
        public float Depth => m_depth;
        public float Height => m_height;
        public float PreviewWidth => _hasPreview ? _previewW : m_width;
        public float PreviewDepth => _hasPreview ? _previewD : m_depth;
        public float PreviewHeight => _hasPreview ? _previewH : m_height;
        public bool DrawSizeHandles => m_drawSizeHandles;
        public Color HandleColor => m_handleColor;

        [Serializable]
        public class FaceSlot
        {
            public string faceKey;
            public string label;
            public Transform faceNode;
            public Transform meshAnchor;
            public Vector3 size;
        }

        [Serializable]
        public class CreaseGizmo
        {
            public string label;
            public Transform pivot;
            public Vector3 hingeLineLocal;
            public float length;
        }

        [ContextMenu("生成纸箱节点树")]
        public void GenerateCartonTree() => Rebuild(selectRoot: true, log: true);

        [ContextMenu("清除节点树")]
        public void ClearCartonTree()
        {
            if (m_rigRoot != null)
                DestroyObj(m_rigRoot.gameObject);
            m_rigRoot = null;
            m_slots.Clear();
            m_creases.Clear();
            _hasPreview = false;
            if (m_carton != null)
                m_carton.SetBindings(Array.Empty<CartonFoldBinding>());
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        public void SetPreviewSize(float width, float depth, float height)
        {
            _previewW = Mathf.Max(MinSize, width);
            _previewD = Mathf.Max(MinSize, depth);
            _previewH = Mathf.Max(MinSize, height);
            _hasPreview = true;
        }

        public void CommitPreviewSize()
        {
            if (!_hasPreview)
                return;
            m_width = _previewW;
            m_depth = _previewD;
            m_height = _previewH;
            _hasPreview = false;
            Rebuild(selectRoot: false, log: false);
        }

        public void SetSizeAndRebuild(float width, float depth, float height)
        {
            m_width = Mathf.Max(MinSize, width);
            m_depth = Mathf.Max(MinSize, depth);
            m_height = Mathf.Max(MinSize, height);
            _hasPreview = false;
            Rebuild(selectRoot: false, log: false);
        }

        private void Rebuild(bool selectRoot, bool log)
        {
#if UNITY_EDITOR
            Undo.SetCurrentGroupName("重建纸箱");
            int group = Undo.GetCurrentGroup();
            Undo.RecordObject(this, "重建纸箱");
#endif
            var preserved = CaptureExtraMeshes();
            BuildTreeInternal(log);
            RestoreExtraMeshes(preserved);
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
            Undo.CollapseUndoOperations(group);
            if (selectRoot && m_rigRoot != null)
                Selection.activeGameObject = m_rigRoot.gameObject;
#endif
        }

        private void BuildTreeInternal(bool log)
        {
            EnsureCarton();
            EnsureMaterial();

            if (m_rigRoot != null)
                DestroyObj(m_rigRoot.gameObject);

            m_slots.Clear();
            m_creases.Clear();

            float w = Mathf.Max(MinSize, m_width);
            float d = Mathf.Max(MinSize, m_depth);
            float h = Mathf.Max(MinSize, m_height);
            float t = Mathf.Max(0.005f, m_thickness);

            float span = Mathf.Min(w, d);
            float longLen = Mathf.Clamp(m_longFlapRatio, 0.25f, 0.55f) * span;
            float shortLen = Mathf.Min(Mathf.Clamp(m_shortFlapRatio, 0.15f, 0.5f) * span, longLen);

            bool fbLong = w >= d;
            float fbLen = fbLong ? longLen : shortLen;
            float lrLen = fbLong ? shortLen : longLen;
            string fbTag = fbLong ? "长边" : "短边";
            string lrTag = fbLong ? "短边" : "长边";
            var fbTop = fbLong ? CartonFoldChannel.TopLong : CartonFoldChannel.TopShort;
            var fbBot = fbLong ? CartonFoldChannel.BottomLong : CartonFoldChannel.BottomShort;
            var lrTop = fbLong ? CartonFoldChannel.TopShort : CartonFoldChannel.TopLong;
            var lrBot = fbLong ? CartonFoldChannel.BottomShort : CartonFoldChannel.BottomLong;

            var root = new GameObject("纸箱节点树");
#if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(root, "纸箱节点树");
#endif
            root.transform.SetParent(transform, false);
            m_rigRoot = root.transform;

            var bindings = new List<CartonFoldBinding>(11);

            var front = CreateFace(m_rigRoot, "前壁", new Vector3(0f, h * 0.5f, -d * 0.5f), new Vector3(w, h, t));
            AddSlot("前壁", "前壁", front, new Vector3(w, h, t));

            var right = AddWall(bindings, front, w, d, h, t, "右壁", "前-右折痕", CartonFoldChannel.WallAcute, 0f, -90f);
            var back = AddWall(bindings, right, d, w, h, t, "后壁", "右-后折痕", CartonFoldChannel.WallObtuse, -180f, -90f);
            var left = AddWall(bindings, back, w, d, h, t, "左壁", "后-左折痕", CartonFoldChannel.WallAcute, 0f, -90f);

            AddFlap(bindings, front, "前壁顶盖", "前壁顶折痕", w, fbLen, h, t, true, fbTop, fbTag);
            AddFlap(bindings, front, "前壁底盖", "前壁底折痕", w, fbLen, h, t, false, fbBot, fbTag);
            AddFlap(bindings, back, "后壁顶盖", "后壁顶折痕", w, fbLen, h, t, true, fbTop, fbTag);
            AddFlap(bindings, back, "后壁底盖", "后壁底折痕", w, fbLen, h, t, false, fbBot, fbTag);
            AddFlap(bindings, right, "右壁顶盖", "右壁顶折痕", d, lrLen, h, t, true, lrTop, lrTag);
            AddFlap(bindings, right, "右壁底盖", "右壁底折痕", d, lrLen, h, t, false, lrBot, lrTag);
            AddFlap(bindings, left, "左壁顶盖", "左壁顶折痕", d, lrLen, h, t, true, lrTop, lrTag);
            AddFlap(bindings, left, "左壁底盖", "左壁底折痕", d, lrLen, h, t, false, lrBot, lrTag);

            m_carton.SetBindings(bindings.ToArray());
            if (m_carton is FoldableCarton foldable)
            {
                float master = foldable.MasterFold;
                foldable.SetMasterFold(master > 0f ? master : 0.55f);
            }
            else
            {
                m_carton.ApplyFolds();
            }

#if UNITY_EDITOR
            EditorUtility.SetDirty(m_carton);
#endif
            if (log)
                Debug.Log($"[纸箱] {w:0.##}×{d:0.##}×{h:0.##} 前向{fbTag} 已可{lrTag}", this);
        }

        private Transform AddWall(
            List<CartonFoldBinding> bindings, Transform parent,
            float parentW, float childW, float childH, float thick,
            string face, string crease, CartonFoldChannel ch, float flat, float closed)
        {
            var pivot = CreateNode(crease, parent, new Vector3(parentW * 0.5f, 0f, 0f));
            var node = CreateFace(pivot, face, new Vector3(childW * 0.5f, 0f, 0f), new Vector3(childW, childH, thick));
            AddSlot(face, face, node, new Vector3(childW, childH, thick));
            AddCrease(crease, pivot, Vector3.up, childH);
            bindings.Add(Bind(crease, pivot, Vector3.up, flat, closed, ch));
            return node;
        }

        private void AddFlap(
            List<CartonFoldBinding> bindings, Transform wall,
            string face, string creaseBase, float width, float len, float wallH, float thick,
            bool top, CartonFoldChannel ch, string tag)
        {
            string crease = $"{creaseBase}({tag})";
            float y = top ? wallH * 0.5f : -wallH * 0.5f;
            var pivot = CreateNode(crease, wall, new Vector3(0f, y, 0f));
            var node = CreateFace(pivot, face, new Vector3(0f, top ? len * 0.5f : -len * 0.5f, 0f),
                new Vector3(width, len, thick));
            AddSlot(face, $"{face} · {tag}", node, new Vector3(width, len, thick));
            AddCrease(crease, pivot, Vector3.right, width);
            bindings.Add(Bind(crease, pivot, top ? Vector3.right : Vector3.left, 0f, 90f, ch));
        }

        private static CartonFoldBinding Bind(
            string name, Transform pivot, Vector3 axis, float flat, float closed, CartonFoldChannel ch)
        {
            return new CartonFoldBinding
            {
                name = name,
                pivot = pivot,
                localAxis = axis,
                flatAngle = flat,
                closedAngle = closed,
                channel = ch
            };
        }

        private Transform CreateFace(Transform parent, string name, Vector3 localPos, Vector3 meshSize)
        {
            var face = CreateNode(name, parent, localPos);
            CreateVisual(CreateNode("网格", face, Vector3.zero), meshSize);
            return face;
        }

        private void CreateVisual(Transform meshRoot, Vector3 size)
        {
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
#if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(visual, VisualName);
#endif
            visual.name = VisualName;
            visual.transform.SetParent(meshRoot, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = size;
            var col = visual.GetComponent<Collider>();
            if (col != null)
                DestroyObj(col);
            if (m_panelMaterial != null)
                visual.GetComponent<MeshRenderer>().sharedMaterial = m_panelMaterial;
        }

        private Transform CreateNode(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
#if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(go, name);
#endif
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        private void AddSlot(string key, string label, Transform face, Vector3 size)
        {
            m_slots.Add(new FaceSlot
            {
                faceKey = key,
                label = label,
                faceNode = face,
                meshAnchor = face.Find("网格"),
                size = size
            });
        }

        private void AddCrease(string label, Transform pivot, Vector3 hingeDir, float length)
        {
            m_creases.Add(new CreaseGizmo
            {
                label = label,
                pivot = pivot,
                hingeLineLocal = hingeDir.normalized,
                length = length
            });
        }

        private Dictionary<string, List<Transform>> CaptureExtraMeshes()
        {
            var map = new Dictionary<string, List<Transform>>();
            for (int s = 0; s < m_slots.Count; s++)
            {
                var slot = m_slots[s];
                if (slot.meshAnchor == null || string.IsNullOrEmpty(slot.faceKey))
                    continue;
                List<Transform> list = null;
                for (int i = slot.meshAnchor.childCount - 1; i >= 0; i--)
                {
                    var child = slot.meshAnchor.GetChild(i);
                    if (child.name == VisualName)
                        continue;
                    if (list == null)
                        list = new List<Transform>();
                    list.Add(child);
                    child.SetParent(null, true);
                }

                if (list != null)
                    map[slot.faceKey] = list;
            }

            return map;
        }

        private void RestoreExtraMeshes(Dictionary<string, List<Transform>> map)
        {
            if (map == null || map.Count == 0)
                return;
            for (int s = 0; s < m_slots.Count; s++)
            {
                var slot = m_slots[s];
                if (slot.meshAnchor == null || !map.TryGetValue(slot.faceKey, out var list))
                    continue;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] != null)
                        list[i].SetParent(slot.meshAnchor, true);
                }
            }
        }

        private void EnsureCarton()
        {
            if (m_carton == null)
                m_carton = GetComponent<OpenBoxAnim>();
            if (m_carton == null)
            {
                m_carton = gameObject.AddComponent<FoldableCarton>();
#if UNITY_EDITOR
                Undo.RegisterCreatedObjectUndo(m_carton, "Add FoldableCarton");
#endif
            }
        }

        private void EnsureMaterial()
        {
            if (m_panelMaterial != null)
                return;
#if UNITY_EDITOR
            const string packageMatPath =
                "Packages/com.nonsensicallab.nonsensicalkit.simulation/ScriptAnimation/Runtime/OpenBox/Materials/Carton.mat";
            m_panelMaterial = AssetDatabase.LoadAssetAtPath<Material>(packageMatPath);
            if (m_panelMaterial != null)
                return;

            foreach (string guid in AssetDatabase.FindAssets("Carton t:Material"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (!path.EndsWith("/Carton.mat", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!path.Contains("ScriptAnimation", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                m_panelMaterial = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m_panelMaterial != null)
                    return;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader != null)
            {
                m_panelMaterial = new Material(shader)
                {
                    name = "Carton (Generated)",
                    color = new Color(0.82f, 0.68f, 0.45f, 1f)
                };
            }
#endif
        }

        private void OnDrawGizmosSelected()
        {
            if (!m_drawGizmos)
                return;

            float w = Mathf.Max(MinSize, PreviewWidth);
            float d = Mathf.Max(MinSize, PreviewDepth);
            float h = Mathf.Max(MinSize, PreviewHeight);

            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.55f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d));
            Gizmos.matrix = Matrix4x4.identity;

            if (m_drawPanelFrames)
            {
                for (int i = 0; i < m_slots.Count; i++)
                {
                    var s = m_slots[i];
                    if (s.faceNode == null)
                        continue;
                    Gizmos.color = m_panelColor;
                    Gizmos.matrix = s.faceNode.localToWorldMatrix;
                    Gizmos.DrawWireCube(Vector3.zero, s.size);
                }

                Gizmos.matrix = Matrix4x4.identity;
            }

            for (int i = 0; i < m_creases.Count; i++)
            {
                var c = m_creases[i];
                if (c.pivot == null)
                    continue;
                Vector3 dir = c.pivot.TransformDirection(c.hingeLineLocal);
                Vector3 mid = c.pivot.position;
                float half = Mathf.Max(0.05f, c.length) * 0.5f;
                Gizmos.color = m_creaseColor;
                Gizmos.DrawLine(mid - dir * half, mid + dir * half);
            }
        }

        private static void DestroyObj(UnityEngine.Object obj)
        {
            if (obj == null)
                return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Undo.DestroyObjectImmediate(obj);
                return;
            }
#endif
            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }
    }
}
