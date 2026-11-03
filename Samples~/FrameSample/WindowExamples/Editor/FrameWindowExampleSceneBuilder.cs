#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Frame.Core.Window;

/// <summary>
/// 生成 FrameSample Window 最小示例场景与内容预制体。
/// 菜单：Tools/FrameSample/Build Window Example Scene
/// </summary>

namespace Frame.Sample.WindowExamples
{
public static class FrameWindowExampleSceneBuilder
{
    private const string ScenePath = "Assets/FrameSample/Scenes/FrameWindowExample.unity";
    private const string ContentPrefabPath = "Assets/FrameSample/WindowExamples/Prefabs/CharacterDetailContent.prefab";
    private const string WindowShellPath = "Assets/FrameSample/WmsDemo/Resources/设备管理/Window/Window.prefab";
    private const string WindowName = "DemoWindow";

    [MenuItem("Tools/FrameSample/Build Window Example Scene")]
    public static void Build()
    {
        EnsureFolder("Assets/FrameSample/Scenes");
        EnsureFolder("Assets/FrameSample/WindowExamples/Prefabs");

        var shell = AssetDatabase.LoadAssetAtPath<GameObject>(WindowShellPath);
        if (shell == null)
        {
            Debug.LogError("[FrameWindowExample] 找不到窗口壳: " + WindowShellPath);
            return;
        }

        var contentPrefab = BuildOrLoadContentPrefab();
        if (contentPrefab == null)
        {
            Debug.LogError("[FrameWindowExample] 内容预制体创建失败");
            return;
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // EventSystem
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }

        // Canvas root
        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // Window manager host
        var mgrGo = new GameObject("WindowContainerManager");
        mgrGo.transform.SetParent(canvasGo.transform, false);
        var mgrRt = mgrGo.AddComponent<RectTransform>();
        Stretch(mgrRt);
        var mgr = mgrGo.AddComponent<WindowContainerManager>();
        var soMgr = new SerializedObject(mgr);
        soMgr.FindProperty("m_windowContainerPrefab").objectReferenceValue = shell;
        soMgr.FindProperty("m_windowName").stringValue = WindowName;
        soMgr.FindProperty("m_createNewWhenInstantiate").boolValue = false;
        soMgr.ApplyModifiedPropertiesWithoutUndo();

        // Open button
        var btnGo = CreateUiButton(canvasGo.transform, "OpenCharacterDetailButton", "打开角色详情", new Vector2(0, 80), new Vector2(280, 56));
        var button = btnGo.GetComponent<Button>();

        // Example driver
        var driverGo = new GameObject("UIManagerExample");
        driverGo.transform.SetParent(canvasGo.transform, false);
        var example = driverGo.AddComponent<UIManagerExample>();
        var soEx = new SerializedObject(example);
        soEx.FindProperty("m_windowName").stringValue = WindowName;
        soEx.FindProperty("_characterDetailPrefab").objectReferenceValue = contentPrefab;
        soEx.FindProperty("m_openButton").objectReferenceValue = button;
        soEx.ApplyModifiedPropertiesWithoutUndo();

        // Hint text
        CreateHint(canvasGo.transform);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[FrameWindowExample] 已生成: " + ScenePath);
    }

    private static GameObject BuildOrLoadContentPrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(ContentPrefabPath);
        if (existing != null)
            return existing;

        var root = new GameObject("CharacterDetailContent", typeof(RectTransform));
        var rootRt = root.GetComponent<RectTransform>();
        Stretch(rootRt);

        var bg = CreateUi("Background", root.transform);
        Stretch(bg.GetComponent<RectTransform>());
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.12f, 0.14f, 0.18f, 1f);

        var nameGo = CreateUi("Name", root.transform);
        var nameRt = nameGo.GetComponent<RectTransform>();
        nameRt.anchorMin = new Vector2(0, 1);
        nameRt.anchorMax = new Vector2(1, 1);
        nameRt.pivot = new Vector2(0.5f, 1);
        nameRt.anchoredPosition = new Vector2(0, -24);
        nameRt.sizeDelta = new Vector2(-48, 48);
        var nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
        nameTmp.text = "角色名";
        nameTmp.fontSize = 32;
        nameTmp.alignment = TextAlignmentOptions.Center;
        nameTmp.color = Color.white;

        var avatarGo = CreateUi("Avatar", root.transform);
        var avatarRt = avatarGo.GetComponent<RectTransform>();
        avatarRt.anchorMin = new Vector2(0.5f, 0.5f);
        avatarRt.anchorMax = new Vector2(0.5f, 0.5f);
        avatarRt.sizeDelta = new Vector2(128, 128);
        avatarRt.anchoredPosition = new Vector2(0, 40);
        var avatarImg = avatarGo.AddComponent<Image>();
        avatarImg.color = new Color(0.35f, 0.55f, 0.85f, 1f);

        var descGo = CreateUi("Description", root.transform);
        var descRt = descGo.GetComponent<RectTransform>();
        descRt.anchorMin = new Vector2(0, 0);
        descRt.anchorMax = new Vector2(1, 0);
        descRt.pivot = new Vector2(0.5f, 0);
        descRt.anchoredPosition = new Vector2(0, 24);
        descRt.sizeDelta = new Vector2(-48, 160);
        var descTmp = descGo.AddComponent<TextMeshProUGUI>();
        descTmp.text = "描述";
        descTmp.fontSize = 22;
        descTmp.alignment = TextAlignmentOptions.TopLeft;
        descTmp.color = new Color(0.9f, 0.9f, 0.9f, 1f);

        var content = root.AddComponent<CharacterDetailContent>();
        var so = new SerializedObject(content);
        so.FindProperty("_nameLabel").objectReferenceValue = nameTmp;
        so.FindProperty("_avatarImage").objectReferenceValue = avatarImg;
        so.FindProperty("_descLabel").objectReferenceValue = descTmp;
        so.ApplyModifiedPropertiesWithoutUndo();

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, ContentPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static GameObject CreateUiButton(Transform parent, string name, string label, Vector2 pos, Vector2 size)
    {
        var go = CreateUi(name, parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.color = new Color(0.2f, 0.45f, 0.75f, 1f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        var textGo = CreateUi("Label", go.transform);
        Stretch(textGo.GetComponent<RectTransform>());
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 24;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        return go;
    }

    private static void CreateHint(Transform parent)
    {
        var go = CreateUi("Hint", parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0);
        rt.anchorMax = new Vector2(0.5f, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.anchoredPosition = new Vector2(0, 40);
        rt.sizeDelta = new Vector2(900, 60);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = "点击「打开角色详情」→ WindowContainerManager 弹出示例窗。无需 MQTT/WMS。关闭按钮在窗口标题栏。";
        tmp.fontSize = 18;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.85f, 0.85f, 0.85f, 1f);
        tmp.raycastTarget = false;
    }

    private static GameObject CreateUi(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parts = path.Split('/');
        var cur = parts[0];
        for (var i = 1; i < parts.Length; i++)
        {
            var next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }
}
#endif
}
