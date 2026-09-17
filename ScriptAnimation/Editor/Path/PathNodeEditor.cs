using System;
using NonsensicalKit.ScriptAnimation;
using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(PathNode))]
    public sealed class PathNodeEditor : UnityEditor.Editor
    {
        /// <summary>路径编辑对方节点（编辑器会话内共用，切换选中节点时保留）。</summary>
        private static PathNode s_pairNode;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(
                serializedObject,
                "m_Script",
                "m_features",
                "m_showGizmo",
                "m_showLabel",
                "m_showForward",
                "m_gizmoColor",
                "m_gizmoRadius",
                "m_forwardLength",
                "m_labelHeight");

            var node = (PathNode)target;
            DrawNodeActions(node);
            DrawPathEditWithPair(node);
            DrawFeatureSection();

            serializedObject.ApplyModifiedProperties();
        }

        void DrawNodeActions(PathNode node)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("节点操作", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(
                    new GUIContent("所属路网"),
                    node.Network,
                    typeof(PathNetwork),
                    true);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("断开本节点全部连线"))
                PathNetworkEditActions.DisconnectNodeNeighbors(node);
            if (GUILayout.Button("清除本节点错误邻居"))
                PathNetworkEditActions.ClearNodeInvalidNeighbors(node);
            EditorGUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(node.Network == null))
            {
                if (GUILayout.Button("打开所属路网图", GUILayout.Height(22f)))
                    PathNetworkGraphWindow.Open(node.Network);
            }
        }

        void DrawPathEditWithPair(PathNode node)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("路径编辑（相对另一节点）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "本节点为一方，再指定对方节点后，可对两点间最短路径做单向改写或断开；若有直接连线，可在中点插入新节点。",
                MessageType.None);

            s_pairNode = (PathNode)EditorGUILayout.ObjectField(
                new GUIContent("对方节点"),
                s_pairNode,
                typeof(PathNode),
                true);

            bool canEdit = node.Network != null &&
                           s_pairNode != null &&
                           s_pairNode != node;

            using (new EditorGUI.DisabledScope(!canEdit))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("本节点 → 对方（单向）", GUILayout.Height(24f)))
                {
                    PathNetworkEditActions.ApplyShortestPathOneWay(node.Network, node, s_pairNode);
                }

                if (GUILayout.Button("对方 → 本节点（单向）", GUILayout.Height(24f)))
                {
                    PathNetworkEditActions.ApplyShortestPathOneWay(node.Network, s_pairNode, node);
                }

                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button("断开最短连线", GUILayout.Height(24f)))
                {
                    PathNetworkEditActions.ApplyDisconnectShortestPath(node.Network, node, s_pairNode);
                }

                if (GUILayout.Button("在两点间插入节点", GUILayout.Height(24f)))
                {
                    PathNetworkEditActions.ApplyInsertNodeBetween(node.Network, node, s_pairNode);
                }
            }

            if (node.Network == null)
            {
                EditorGUILayout.HelpBox(
                    "本节点尚未绑定所属路网。请在路网图中执行「收集子节点」，或确认层级挂在 PathNetwork 下。",
                    MessageType.Warning);
            }
        }

        void DrawFeatureSection()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("扩展模块", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "停留仍用上方「顶升移栽」。这里只放业务扩展模块（点位映射等）。热路径走 PathNetwork.FeatureStore。",
                MessageType.None);

            var featuresProp = serializedObject.FindProperty("m_features");
            if (featuresProp != null)
                EditorGUILayout.PropertyField(featuresProp, new GUIContent("模块列表"), true);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("添加扩展模块"))
                ShowAddFeatureMenu();
            EditorGUI.BeginDisabledGroup(featuresProp == null || featuresProp.arraySize <= 0);
            if (GUILayout.Button("移除末项"))
            {
                featuresProp.DeleteArrayElementAtIndex(featuresProp.arraySize - 1);
            }

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        void ShowAddFeatureMenu()
        {
            var menu = new GenericMenu();
            var types = TypeCache.GetTypesDerivedFrom<IPathNodeFeature>();
            int added = 0;
            for (int i = 0; i < types.Count; i++)
            {
                var type = types[i];
                if (type == null || type.IsAbstract || type.IsInterface)
                    continue;
                if (type.GetConstructor(Type.EmptyTypes) == null)
                    continue;

                string label = type.Name;
                var attrs = type.GetCustomAttributes(typeof(PathNodeFeatureMenuAttribute), false);
                if (attrs != null && attrs.Length > 0 && attrs[0] is PathNodeFeatureMenuAttribute menuAttr &&
                    !string.IsNullOrEmpty(menuAttr.MenuPath))
                    label = menuAttr.MenuPath;

                var captured = type;
                menu.AddItem(new GUIContent(label), false, () => AddFeature(captured));
                added++;
            }

            if (added == 0)
                menu.AddDisabledItem(new GUIContent("没有可用的 IPathNodeFeature 实现"));
            menu.ShowAsContext();
        }

        void AddFeature(Type type)
        {
            var node = (PathNode)target;
            Undo.RecordObject(node, "添加路网扩展模块");
            var feature = (IPathNodeFeature)Activator.CreateInstance(type);
            node.SetFeature(feature);
            EditorUtility.SetDirty(node);
            if (node.Network != null)
                node.Network.BindNodesNetwork();
        }
    }
}
