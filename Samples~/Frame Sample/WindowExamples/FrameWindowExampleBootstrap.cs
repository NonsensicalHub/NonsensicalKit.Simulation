using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem.UI;
#endif
using Frame.Core.Window;

/// <summary>
/// FrameSample Window 最小演示：运行时搭建 Canvas / 按钮 / WindowContainerManager，
/// 窗口壳从 Resources 加载（WmsDemo 设备管理 Window），内容预制体运行时生成。
/// </summary>

namespace Frame.Sample.WindowExamples
{
public class FrameWindowExampleBootstrap : MonoBehaviour
{
    public const string DefaultWindowName = "DemoWindow";
    private const string WindowShellResourcePath = "设备管理/Window/Window";

    [SerializeField] private string m_windowName = DefaultWindowName;
    [SerializeField] private string m_windowShellResourcePath = WindowShellResourcePath;

    private GameObject _contentPrefab;

    private void Awake()
    {
        EnsureEventSystem();
        var canvas = EnsureCanvas();
        var shell = Resources.Load<GameObject>(m_windowShellResourcePath);
        if (shell == null)
        {
            Debug.LogError(
                $"[FrameWindowExample] Resources.Load 失败: \"{m_windowShellResourcePath}\"。" +
                "请确认 FrameSample/WmsDemo/Resources/设备管理/Window/Window.prefab 仍在工程中。");
            return;
        }

        _contentPrefab = BuildContentPrefabAsset();

        var mgrGo = new GameObject("WindowContainerManager");
        mgrGo.transform.SetParent(canvas.transform, false);
        mgrGo.SetActive(false);
        var mgrRt = mgrGo.AddComponent<RectTransform>();
        Stretch(mgrRt);
        var mgr = mgrGo.AddComponent<WindowContainerManager>();
        SetPrivate(mgr, "m_windowContainerPrefab", shell);
        SetPrivate(mgr, "m_windowName", m_windowName);
        SetPrivate(mgr, "m_createNewWhenInstantiate", false);
        mgrGo.SetActive(true); // Awake → IOCC.Set

        var btnGo = CreateButton(canvas.transform, "打开角色详情", new Vector2(0, 80), new Vector2(280, 56));

        var exampleGo = new GameObject("UIManagerExample");
        exampleGo.transform.SetParent(canvas.transform, false);
        exampleGo.SetActive(false);
        var example = exampleGo.AddComponent<UIManagerExample>();
        SetPrivate(example, "m_windowName", m_windowName);
        SetPrivate(example, "_characterDetailPrefab", _contentPrefab);
        SetPrivate(example, "m_openButton", btnGo.GetComponent<Button>());
        exampleGo.SetActive(true); // Awake 绑定 Button

        CreateHint(canvas.transform);
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
            return;
        var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        es.AddComponent<InputSystemUIInputModule>();
#else
        es.AddComponent<StandaloneInputModule>();
#endif
    }

    private static Canvas EnsureCanvas()
    {
        var existing = FindFirstObjectByType<Canvas>();
        if (existing != null)
            return existing;

        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        return canvas;
    }

    private static GameObject BuildContentPrefabAsset()
    {
        // 运行时用的“预制体模板”：隐藏根物体，OpenWindow 时再 Instantiate。
        var root = new GameObject("CharacterDetailContent");
        root.SetActive(false);
        DontDestroyOnLoad(root);
        var rootRt = root.AddComponent<RectTransform>();
        Stretch(rootRt);

        var bg = CreateUi("Background", root.transform);
        Stretch(bg.GetComponent<RectTransform>());
        bg.AddComponent<Image>().color = new Color(0.12f, 0.14f, 0.18f, 1f);

        var nameGo = CreateUi("Name", root.transform);
        var nameRt = nameGo.GetComponent<RectTransform>();
        nameRt.anchorMin = new Vector2(0, 1);
        nameRt.anchorMax = new Vector2(1, 1);
        nameRt.pivot = new Vector2(0.5f, 1);
        nameRt.anchoredPosition = new Vector2(0, -24);
        nameRt.sizeDelta = new Vector2(-48, 48);
        var nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
        nameTmp.fontSize = 32;
        nameTmp.alignment = TextAlignmentOptions.Center;
        nameTmp.color = Color.white;

        var avatarGo = CreateUi("Avatar", root.transform);
        var avatarRt = avatarGo.GetComponent<RectTransform>();
        avatarRt.anchorMin = avatarRt.anchorMax = new Vector2(0.5f, 0.5f);
        avatarRt.sizeDelta = new Vector2(128, 128);
        avatarRt.anchoredPosition = new Vector2(0, 40);
        avatarGo.AddComponent<Image>().color = new Color(0.35f, 0.55f, 0.85f, 1f);

        var descGo = CreateUi("Description", root.transform);
        var descRt = descGo.GetComponent<RectTransform>();
        descRt.anchorMin = new Vector2(0, 0);
        descRt.anchorMax = new Vector2(1, 0);
        descRt.pivot = new Vector2(0.5f, 0);
        descRt.anchoredPosition = new Vector2(0, 24);
        descRt.sizeDelta = new Vector2(-48, 160);
        var descTmp = descGo.AddComponent<TextMeshProUGUI>();
        descTmp.fontSize = 22;
        descTmp.alignment = TextAlignmentOptions.TopLeft;
        descTmp.color = new Color(0.9f, 0.9f, 0.9f, 1f);

        root.AddComponent<CharacterDetailContent>();
        return root;
    }

    private static GameObject CreateButton(Transform parent, string label, Vector2 pos, Vector2 size)
    {
        var go = CreateUi("OpenCharacterDetailButton", parent);
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
        rt.anchoredPosition = new Vector2(0, 36);
        rt.sizeDelta = new Vector2(1000, 70);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = "点击「打开角色详情」弹出 Window 示例。不需要 MQTT/WMS。可用标题栏关闭。";
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

    private static void SetPrivate(object target, string fieldName, object value)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var field = target.GetType().GetField(fieldName, flags);
        if (field == null)
        {
            Debug.LogWarning($"[FrameWindowExample] 字段未找到: {target.GetType().Name}.{fieldName}");
            return;
        }

        field.SetValue(target, value);
    }
}
}
