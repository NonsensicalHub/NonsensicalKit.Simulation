using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(CartonRigConfig))]
    public class CartonRigConfigEditor : UnityEditor.Editor
    {
        const float MinSize = 0.05f;
        bool m_dragging;

        void OnSceneGUI()
        {
            var cfg = (CartonRigConfig)target;
            if (cfg == null || !cfg.DrawSizeHandles)
                return;

            Transform t = cfg.transform;
            float w = Mathf.Max(MinSize, cfg.PreviewWidth);
            float d = Mathf.Max(MinSize, cfg.PreviewDepth);
            float h = Mathf.Max(MinSize, cfg.PreviewHeight);

            Vector3 center = t.TransformPoint(new Vector3(0f, h * 0.5f, 0f));
            Vector3 right = t.TransformDirection(Vector3.right);
            Vector3 up = t.TransformDirection(Vector3.up);
            Vector3 forward = t.TransformDirection(Vector3.forward);
            float handleSize = HandleUtility.GetHandleSize(center) * 0.15f;

            Handles.color = cfg.HandleColor;
            Handles.DrawWireCube(center, t.rotation * new Vector3(w, h, d));

            EditorGUI.BeginChangeCheck();
            Vector3 xPos = Handles.Slider(center + right * (w * 0.5f), right, handleSize, Handles.CubeHandleCap, 0.01f);
            Vector3 zPos = Handles.Slider(center + forward * (d * 0.5f), forward, handleSize, Handles.CubeHandleCap, 0.01f);
            Vector3 yPos = Handles.Slider(center + up * (h * 0.5f), up, handleSize, Handles.CubeHandleCap, 0.01f);

            float newW = Mathf.Max(MinSize, Vector3.Dot(xPos - center, right) * 2f);
            float newD = Mathf.Max(MinSize, Vector3.Dot(zPos - center, forward) * 2f);
            float newH = Mathf.Max(MinSize, Vector3.Dot(yPos - center, up) * 2f);

            Handles.Label(xPos + right * handleSize, $"宽 {newW:0.00}");
            Handles.Label(zPos + forward * handleSize, $"深 {newD:0.00}");
            Handles.Label(yPos + up * handleSize, $"高 {newH:0.00}");

            if (EditorGUI.EndChangeCheck())
            {
                m_dragging = true;
                cfg.SetPreviewSize(newW, newD, newH);
                SceneView.RepaintAll();
            }

            if (m_dragging && EditorGUIUtility.hotControl == 0)
            {
                m_dragging = false;
                Undo.RecordObject(cfg, "拖拽调整纸箱尺寸");
                cfg.CommitPreviewSize();
                EditorUtility.SetDirty(cfg);
            }
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var cfg = (CartonRigConfig)target;
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Scene：右=宽、前=深、上=高；拖拽中只预览外框，松手后重建。",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("生成纸箱节点树", GUILayout.Height(26)))
            {
                Undo.RecordObject(cfg, "生成纸箱节点树");
                cfg.GenerateCartonTree();
            }

            if (GUILayout.Button("清除节点树", GUILayout.Height(26)))
            {
                Undo.RecordObject(cfg, "清除节点树");
                cfg.ClearCartonTree();
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("按当前尺寸重建", GUILayout.Height(26)))
            {
                Undo.RecordObject(cfg, "按当前尺寸重建");
                cfg.SetSizeAndRebuild(cfg.Width, cfg.Depth, cfg.Height);
            }
        }
    }
}
