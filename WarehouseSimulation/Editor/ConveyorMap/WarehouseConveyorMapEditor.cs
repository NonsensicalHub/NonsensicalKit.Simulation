#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Editor
{
    [CustomEditor(typeof(WarehouseConveyorMap))]
    public sealed class WarehouseConveyorMapEditor : UnityEditor.Editor
    {
        [UnityEditor.Callbacks.OnOpenAsset(0)]
        public static bool OnOpenAsset(int instanceID, int line)
        {
            var map = EditorUtility.InstanceIDToObject(instanceID) as WarehouseConveyorMap;
            if (map == null)
            {
                return false;
            }

            ConveyorMapEditorWindow.Open(map);
            return true;
        }

        public override void OnInspectorGUI()
        {
            var map = (WarehouseConveyorMap)target;
            if (ConveyorMapNodeIdentityUtility.EnsureNodeIdentity(map))
            {
                serializedObject.Update();
            }

            serializedObject.Update();
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("DefaultSpeedMetersPerSecond"),
                new GUIContent("默认速度 (m/s)"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("CargoUnitLengthMeters"),
                new GUIContent("单箱长度 (m)"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("DefaultEdgeDistanceMeters"),
                new GUIContent("新建路段距离 (m)"));
            serializedObject.ApplyModifiedProperties();

            var nodeCount = map.Nodes?.Length ?? 0;
            var edgeCount = map.Edges?.Length ?? 0;
            EditorGUILayout.HelpBox(
                $"节点 {nodeCount}，路段 {edgeCount}。节点和路段请在俯视地图编辑器里改，不要在 Inspector 展开整表。",
                MessageType.Info);

            EditorGUILayout.Space(8);
            if (GUILayout.Button("打开可视化地图编辑器", GUILayout.Height(28)))
            {
                ConveyorMapEditorWindow.Open(map);
            }
        }
    }
}
#endif
