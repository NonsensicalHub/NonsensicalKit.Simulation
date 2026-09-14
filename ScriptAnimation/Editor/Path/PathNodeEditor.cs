using System;
using NonsensicalKit.ScriptAnimation;
using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(PathNode))]
    public sealed class PathNodeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", "m_features");

            EditorGUILayout.Space(4);
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

            serializedObject.ApplyModifiedProperties();
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
