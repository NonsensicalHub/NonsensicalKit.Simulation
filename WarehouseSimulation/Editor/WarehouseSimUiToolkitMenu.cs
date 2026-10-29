using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback;
using NonsensicalKit.Simulation.WarehouseSimulation.Runtime;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Editor
{
    /// <summary>
    /// 给当前场景挂上包内的仓储仿真 UI Toolkit 面板。
    /// </summary>
    public static class WarehouseSimUiToolkitMenu
    {
        public const string PackageUiFolder =
            "Packages/com.nonsensicallab.nonsensicalkit.simulation/WarehouseSimulation/Runtime/UI/Toolkit";

        public const string UxmlPath = PackageUiFolder + "/WarehouseSimPanel.uxml";
        public const string PanelSettingsPath = PackageUiFolder + "/WarehouseSimPanelSettings.asset";

        [MenuItem("Tools/NonsensicalKit/Warehouse Simulation/添加 UI Toolkit 面板")]
        private static void AddPanel()
        {
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (visualTree == null || panelSettings == null)
            {
                Debug.LogError($"找不到仿真 UI 资源：{UxmlPath}");
                return;
            }

            var uiObject = GameObject.Find("WarehouseSimUI");
            if (uiObject == null)
            {
                uiObject = new GameObject("WarehouseSimUI");
                Undo.RegisterCreatedObjectUndo(uiObject, "添加仓储仿真 UI");
            }

            var document = uiObject.GetComponent<UIDocument>();
            if (document == null)
            {
                document = Undo.AddComponent<UIDocument>(uiObject);
            }

            document.visualTreeAsset = visualTree;
            document.panelSettings = panelSettings;
            document.sortingOrder = 4000;

            var controller = uiObject.GetComponent<WarehouseSimUiController>();
            if (controller == null)
            {
                controller = Undo.AddComponent<WarehouseSimUiController>(uiObject);
            }

            var serialized = new SerializedObject(controller);
            serialized.FindProperty("m_runner").objectReferenceValue =
                UnityEngine.Object.FindFirstObjectByType<WarehouseSimRunner>();
            serialized.FindProperty("m_playback").objectReferenceValue =
                UnityEngine.Object.FindFirstObjectByType<WarehouseSimPlaybackController>();
            serialized.FindProperty("m_document").objectReferenceValue = document;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(uiObject);
            Debug.Log("已添加仓储仿真 UI Toolkit 面板。可把 Font 拖到 WarehouseSimUiController 上。");
        }
    }
}
