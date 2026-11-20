using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using NonsensicalKit.Simulation.WarehouseSimulation.Runtime;

/// <summary>
/// 把仓储仿真场景里的 UGUI 控制面板换成 UI Toolkit。场景已打开时，脚本编译完成后会自动执行一次。
/// </summary>
[InitializeOnLoad]
public static class WarehouseSimUiToolkitMigrator
{
    private const string ScenePath = "Assets/Scenes/仓储仿真.unity";
    private const string UxmlPath =
        NonsensicalKit.Simulation.WarehouseSimulation.Editor.WarehouseSimUiToolkitMenu.UxmlPath;
    private const string PanelSettingsPath =
        NonsensicalKit.Simulation.WarehouseSimulation.Editor.WarehouseSimUiToolkitMenu.PanelSettingsPath;
    private const string FontPath = "Assets/TextMesh Pro Chinese/Fonts/阿里汉仪智能黑体.ttf";

    private static readonly string[] UguiTypeNames =
    {
        "WarehouseSimRunControlPart",
        "GraphicRaycaster",
        "CanvasScaler",
        "Canvas",
    };

    static WarehouseSimUiToolkitMigrator()
    {
        EditorApplication.delayCall += () => ScheduleMigrate(0);
    }

    [MenuItem("Tools/仓储仿真/切换为 UI Toolkit")]
    private static void MigrateFromMenu()
    {
        if (!TryMigrate(logWhenSkipped: true))
        {
            return;
        }

        Debug.Log("[仓储仿真] 已切换为 UI Toolkit 面板。");
    }

    private static void ScheduleMigrate(int attempt)
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            if (attempt < 20)
            {
                EditorApplication.delayCall += () => ScheduleMigrate(attempt + 1);
            }

            return;
        }

        TryMigrate(logWhenSkipped: false);
    }

    private static bool TryMigrate(bool logWhenSkipped)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            if (logWhenSkipped)
            {
                Debug.LogWarning("[仓储仿真] 请先退出 Play 再切换界面。");
            }

            return false;
        }

        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (logWhenSkipped)
            {
                Debug.LogWarning("[仓储仿真] 请先打开仓储仿真场景。");
            }

            return false;
        }

        var uiObject = GameObject.Find("WarehouseSimUI");
        if (uiObject == null)
        {
            Debug.LogError("[仓储仿真] 场景里找不到 WarehouseSimUI。");
            return false;
        }

        if (uiObject.GetComponent<WarehouseSimUiController>() != null
            && uiObject.GetComponent<Canvas>() == null
            && uiObject.GetComponent<UIDocument>() != null)
        {
            return false;
        }

        var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
        if (visualTree == null)
        {
            if (logWhenSkipped)
            {
                Debug.LogError("[仓储仿真] 找不到 " + UxmlPath);
            }

            return false;
        }

        var panelSettings = LoadOrCreatePanelSettings();
        var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        var runner = Object.FindFirstObjectByType<NonsensicalKit.Simulation.WarehouseSimulation.Runtime.WarehouseSimRunner>();
        var playback = Object.FindFirstObjectByType<NonsensicalKit.Simulation.WarehouseSimulation.Playback.WarehouseSimPlaybackController>();

        Undo.SetCurrentGroupName("仓储仿真切换 UI Toolkit");
        StripUgui(uiObject);

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
        serialized.FindProperty("m_runner").objectReferenceValue = runner;
        serialized.FindProperty("m_playback").objectReferenceValue = playback;
        serialized.FindProperty("m_document").objectReferenceValue = document;
        serialized.FindProperty("m_font").objectReferenceValue = font;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[仓储仿真] WarehouseSimUI 已改为 UI Toolkit，并保存场景。");
        return true;
    }

    private static void StripUgui(GameObject uiObject)
    {
        var children = new List<GameObject>();
        foreach (Transform child in uiObject.transform)
        {
            children.Add(child.gameObject);
        }

        foreach (var child in children)
        {
            Undo.DestroyObjectImmediate(child);
        }

        foreach (var typeName in UguiTypeNames)
        {
            foreach (var component in uiObject.GetComponents<Component>())
            {
                if (component == null || component.GetType().Name != typeName)
                {
                    continue;
                }

                Undo.DestroyObjectImmediate(component);
            }
        }
    }

    private static PanelSettings LoadOrCreatePanelSettings()
    {
        var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        if (panelSettings == null)
        {
            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            ApplyDefaultTheme(panelSettings);
            AssetDatabase.CreateAsset(panelSettings, PanelSettingsPath);
        }

        panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        panelSettings.referenceResolution = new Vector2Int(1920, 1080);
        panelSettings.match = 0.5f;
        panelSettings.sortingOrder = 4000;
        if (panelSettings.themeStyleSheet == null)
        {
            ApplyDefaultTheme(panelSettings);
        }

        EditorUtility.SetDirty(panelSettings);
        AssetDatabase.SaveAssets();
        return panelSettings;
    }

    private static void ApplyDefaultTheme(PanelSettings panelSettings)
    {
        var reset = typeof(PanelSettings).GetMethod(
            "Reset",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        reset?.Invoke(panelSettings, null);

        if (panelSettings.themeStyleSheet != null)
        {
            return;
        }

        foreach (var guid in AssetDatabase.FindAssets("t:ThemeStyleSheet UnityDefaultRuntimeTheme"))
        {
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(AssetDatabase.GUIDToAssetPath(guid));
            if (theme == null)
            {
                continue;
            }

            panelSettings.themeStyleSheet = theme;
            return;
        }
    }
}
