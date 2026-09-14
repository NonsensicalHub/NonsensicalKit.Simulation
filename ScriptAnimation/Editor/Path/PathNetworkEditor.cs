using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(PathNetwork))]
    public class PathNetworkEditor : UnityEditor.Editor
    {
        private static readonly string[] BasicPropertyNames =
        {
            "m_nodes",
            "m_autoCollectFromChildren",
            "m_autoLinkOnAwake",
            "m_clearBeforeAutoLink",
            "m_autoLinkBidirectional",
            "m_maxCorridorLength",
            "m_orthogonalTolerance",
            "m_corridorClearance",
            "m_showNodeGizmos",
            "m_showNodeLabels",
            "m_gizmoColor",
            "m_labelColor",
            "m_labelHeight",
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var network = (PathNetwork)target;
            EditorGUILayout.HelpBox(
                "节点管理、连边、排序命名、路径编辑等操作请在「路网配置」窗口中进行。",
                MessageType.Info);

            for (int i = 0; i < BasicPropertyNames.Length; i++)
            {
                var prop = serializedObject.FindProperty(BasicPropertyNames[i]);
                if (prop != null)
                    EditorGUILayout.PropertyField(prop, true);
            }

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            if (GUILayout.Button("刷新扩展缓存", GUILayout.Height(22f)))
                network.BindNodesNetwork();
            if (GUILayout.Button("打开路网图", GUILayout.Height(28f)))
                PathNetworkGraphWindow.Open(network);
        }
    }
}
