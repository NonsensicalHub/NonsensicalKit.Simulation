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

            DrawDefaultInspector();

            EditorGUILayout.Space(8);
            if (GUILayout.Button("打开可视化地图编辑器", GUILayout.Height(28)))
            {
                ConveyorMapEditorWindow.Open(map);
            }

            if (map.Nodes != null && map.Nodes.Length > 0)
            {
                EditorGUILayout.HelpBox(
                    "双击资源可打开地图编辑器；在 GraphView 中编辑拓扑与画布位置，路段物理参数在侧栏配置。",
                    MessageType.Info);
            }
        }
    }
}
#endif
