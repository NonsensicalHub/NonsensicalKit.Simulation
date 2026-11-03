using NonsensicalKit.UGUI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 运行时生成一套回放 UI：打开按钮 + 可拖拽面板（含关闭）。
/// </summary>
public class RecordDataPlayerSimpleUI : MonoBehaviour
{
    [SerializeField] private RecordDataPlayer m_player;
    [SerializeField] private bool m_buildOnAwake = true;
    [SerializeField] private bool m_panelVisibleOnStart;
    [SerializeField] private Vector2 m_openButtonPosition = new(-16f, -16f);
    [SerializeField] private Vector2 m_panelPosition = new(-16f, -64f);
    [SerializeField] private Vector2 m_panelSize = new(360f, 310f);
    [SerializeField] private KeyCode m_toggleKey = KeyCode.F8;

    private RecordDataPlayerUI _ui;

    private void Awake()
    {
        if (m_player == null)
        {
            m_player = GetComponent<RecordDataPlayer>();
            if (m_player == null)
            {
                m_player = FindFirstObjectByType<RecordDataPlayer>();
            }
        }

        if (m_buildOnAwake)
        {
            Build();
        }
    }

    private void Update()
    {
        if (m_toggleKey != KeyCode.None && Input.GetKeyDown(m_toggleKey))
        {
            _ui?.TogglePanel();
        }
    }

    public void Build()
    {
        if (_ui != null)
        {
            return;
        }

        EnsureEventSystem();

        var canvasGo = new GameObject("RecordPlaybackCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var openBtn = CreateOpenButton(canvasGo.transform);
        var panel = CreatePanel(canvasGo.transform);
        panel.SetActive(m_panelVisibleOnStart);

        var closeBtn = panel.transform.Find("DragHeader/CloseButton").GetComponent<Button>();
        var fileDropdown = panel.transform.Find("FileDropdown").GetComponent<TMP_Dropdown>();
        var playBtn = panel.transform.Find("PlayButton").GetComponent<Button>();
        var pauseBtn = panel.transform.Find("PauseButton").GetComponent<Button>();
        var stopBtn = panel.transform.Find("StopButton").GetComponent<Button>();
        var loopToggle = panel.transform.Find("LoopToggle").GetComponent<Toggle>();
        var speedSlider = panel.transform.Find("SpeedSlider").GetComponent<Slider>();
        var status = panel.transform.Find("Status").GetComponent<TMP_Text>();
        var time = panel.transform.Find("Time").GetComponent<TMP_Text>();
        var progress = panel.transform.Find("Progress").GetComponent<TMP_Text>();
        var speedValue = panel.transform.Find("SpeedValue").GetComponent<TMP_Text>();
        var playLabel = playBtn.GetComponentInChildren<TMP_Text>();
        var pauseLabel = pauseBtn.GetComponentInChildren<TMP_Text>();

        _ui = canvasGo.AddComponent<RecordDataPlayerUI>();
        _ui.Bind(
            m_player, panel, openBtn, closeBtn, fileDropdown,
            playBtn, stopBtn, pauseBtn, loopToggle, speedSlider,
            playLabel, pauseLabel, status, time, progress, speedValue);
    }

    public void SetVisible(bool visible) => _ui?.SetPanelVisible(visible);

    private Button CreateOpenButton(Transform parent)
    {
        var btn = CreateButton(parent, "OpenButton", "录制回放", 0f, 0f, 120f, 40f);
        var rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = m_openButtonPosition;
        rt.sizeDelta = new Vector2(120f, 40f);
        btn.GetComponent<Image>().color = new Color(0.18f, 0.42f, 0.72f, 0.95f);
        return btn;
    }

    private GameObject CreatePanel(Transform parent)
    {
        var panel = CreateUiObject("RecordPlaybackPanel", parent);
        var panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(1f, 1f);
        panelRt.anchorMax = new Vector2(1f, 1f);
        panelRt.pivot = new Vector2(1f, 1f);
        panelRt.anchoredPosition = m_panelPosition;
        panelRt.sizeDelta = m_panelSize;

        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.1f, 0.14f, 0.92f);

        var dragHeader = CreateUiObject("DragHeader", panel.transform);
        var headerRt = dragHeader.GetComponent<RectTransform>();
        headerRt.anchorMin = new Vector2(0f, 1f);
        headerRt.anchorMax = new Vector2(1f, 1f);
        headerRt.pivot = new Vector2(0.5f, 1f);
        headerRt.anchoredPosition = Vector2.zero;
        headerRt.sizeDelta = new Vector2(0f, 36f);
        var headerImage = dragHeader.AddComponent<Image>();
        headerImage.color = new Color(0.12f, 0.16f, 0.22f, 1f);
        headerImage.raycastTarget = true;

        var drag = dragHeader.AddComponent<DragSpace>();
        // Runtime：直接赋值私有字段不可行，用反射或公共 API。DragSpace 无公开 SetControl，序列化字段在运行时 AddComponent 后需设。
        SetDragControl(drag, panelRt);

        CreateLabel(dragHeader.transform, "Title", "录制回放", 20, 12f, 4f, 280f, 28f, TextAlignmentOptions.Left);
        var closeBtn = CreateButton(dragHeader.transform, "CloseButton", "×", 0f, 0f, 32f, 28f);
        var closeRt = closeBtn.GetComponent<RectTransform>();
        closeRt.anchorMin = new Vector2(1f, 0.5f);
        closeRt.anchorMax = new Vector2(1f, 0.5f);
        closeRt.pivot = new Vector2(1f, 0.5f);
        closeRt.anchoredPosition = new Vector2(-8f, 0f);
        closeRt.sizeDelta = new Vector2(32f, 28f);
        closeBtn.GetComponent<Image>().color = new Color(0.55f, 0.22f, 0.22f, 1f);

        CreateLabel(panel.transform, "Status", "状态：空闲（在线数据）", 15, 14f, 48f, m_panelSize.x - 28f, 22f, TextAlignmentOptions.Left);
        CreateLabel(panel.transform, "Time", "时间：-", 14, 14f, 76f, m_panelSize.x - 28f, 20f, TextAlignmentOptions.Left);
        CreateLabel(panel.transform, "Progress", "进度：0/0  (0%)", 14, 14f, 100f, m_panelSize.x - 28f, 20f, TextAlignmentOptions.Left);

        CreateLabel(panel.transform, "FileLabel", "文件", 14, 14f, 128f, 48f, 24f, TextAlignmentOptions.Left);
        CreateDropdown(panel.transform, "FileDropdown", 62f, 126f, 284f, 28f);

        CreateButton(panel.transform, "PlayButton", "播放", 14f, 168f, 104f, 36f);
        CreateButton(panel.transform, "PauseButton", "暂停", 128f, 168f, 104f, 36f);
        CreateButton(panel.transform, "StopButton", "停止", 242f, 168f, 104f, 36f);

        CreateLabel(panel.transform, "SpeedLabel", "速度", 14, 14f, 216f, 48f, 20f, TextAlignmentOptions.Left);
        CreateLabel(panel.transform, "SpeedValue", "x1", 14, 290f, 216f, 56f, 20f, TextAlignmentOptions.Right);
        CreateSlider(panel.transform, "SpeedSlider", 64f, 218f, 220f, 18f);
        CreateToggle(panel.transform, "LoopToggle", "循环", 14f, 248f);

        return panel;
    }

    private static TMP_Dropdown CreateDropdown(Transform parent, string name, float x, float y, float w, float h)
    {
        var go = CreateUiObject(name, parent);
        TopLeft(go.GetComponent<RectTransform>(), x, y, w, h);
        var image = go.AddComponent<Image>();
        image.color = new Color(0.16f, 0.2f, 0.26f, 1f);

        var labelGo = CreateUiObject("Label", go.transform);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(10f, 2f);
        labelRt.offsetMax = new Vector2(-28f, -2f);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.text = "选择文件";
        label.fontSize = 14;
        label.alignment = TextAlignmentOptions.Left;
        label.color = Color.white;
        label.raycastTarget = false;

        var arrowGo = CreateUiObject("Arrow", go.transform);
        var arrowRt = arrowGo.GetComponent<RectTransform>();
        arrowRt.anchorMin = new Vector2(1f, 0.5f);
        arrowRt.anchorMax = new Vector2(1f, 0.5f);
        arrowRt.pivot = new Vector2(1f, 0.5f);
        arrowRt.anchoredPosition = new Vector2(-6f, 0f);
        arrowRt.sizeDelta = new Vector2(16f, 16f);
        arrowGo.AddComponent<Image>().color = new Color(0.75f, 0.8f, 0.9f, 1f);

        var template = CreateUiObject("Template", go.transform);
        var templateRt = template.GetComponent<RectTransform>();
        templateRt.anchorMin = new Vector2(0f, 0f);
        templateRt.anchorMax = new Vector2(1f, 0f);
        templateRt.pivot = new Vector2(0.5f, 1f);
        templateRt.anchoredPosition = new Vector2(0f, 2f);
        templateRt.sizeDelta = new Vector2(0f, 150f);
        template.AddComponent<Image>().color = new Color(0.12f, 0.14f, 0.18f, 0.98f);
        var scroll = template.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        template.SetActive(false);

        var viewport = CreateUiObject("Viewport", template.transform);
        var viewportRt = viewport.GetComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = Vector2.zero;
        viewportRt.offsetMax = Vector2.zero;
        viewport.AddComponent<Image>().color = Color.white;
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        var content = CreateUiObject("Content", viewport.transform);
        var contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0f, 28f);
        scroll.viewport = viewportRt;
        scroll.content = contentRt;

        var item = CreateUiObject("Item", content.transform);
        var itemRt = item.GetComponent<RectTransform>();
        itemRt.anchorMin = new Vector2(0f, 0.5f);
        itemRt.anchorMax = new Vector2(1f, 0.5f);
        itemRt.sizeDelta = new Vector2(0f, 28f);
        var itemToggle = item.AddComponent<Toggle>();

        var itemBg = CreateUiObject("Item Background", item.transform);
        var itemBgRt = itemBg.GetComponent<RectTransform>();
        itemBgRt.anchorMin = Vector2.zero;
        itemBgRt.anchorMax = Vector2.one;
        itemBgRt.offsetMin = Vector2.zero;
        itemBgRt.offsetMax = Vector2.zero;
        var itemBgImage = itemBg.AddComponent<Image>();
        itemBgImage.color = new Color(0.2f, 0.45f, 0.75f, 0.55f);

        var itemLabelGo = CreateUiObject("Item Label", item.transform);
        var itemLabelRt = itemLabelGo.GetComponent<RectTransform>();
        itemLabelRt.anchorMin = Vector2.zero;
        itemLabelRt.anchorMax = Vector2.one;
        itemLabelRt.offsetMin = new Vector2(10f, 1f);
        itemLabelRt.offsetMax = new Vector2(-10f, -1f);
        var itemLabel = itemLabelGo.AddComponent<TextMeshProUGUI>();
        itemLabel.fontSize = 14;
        itemLabel.color = Color.white;
        itemLabel.alignment = TextAlignmentOptions.Left;
        itemLabel.raycastTarget = false;

        itemToggle.targetGraphic = itemBgImage;
        itemToggle.graphic = itemBgImage;

        var dropdown = go.AddComponent<TMP_Dropdown>();
        dropdown.targetGraphic = image;
        dropdown.captionText = label;
        dropdown.itemText = itemLabel;
        dropdown.template = templateRt;
        return dropdown;
    }

    private static void SetDragControl(DragSpace drag, RectTransform control)
    {
        var field = typeof(DragSpace).GetField("m_controlRect",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field?.SetValue(drag, control);
        // AddComponent 时 Awake 可能因 control 未赋值而禁用，这里补上。
        drag.enabled = true;
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
        {
            return;
        }

        _ = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static RectTransform TopLeft(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    private static TMP_Text CreateLabel(
        Transform parent, string name, string content, float fontSize,
        float x, float y, float w, float h, TextAlignmentOptions align)
    {
        var go = CreateUiObject(name, parent);
        TopLeft(go.GetComponent<RectTransform>(), x, y, w, h);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = fontSize;
        text.alignment = align;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static Button CreateButton(Transform parent, string name, string label, float x, float y, float w, float h)
    {
        var go = CreateUiObject(name, parent);
        TopLeft(go.GetComponent<RectTransform>(), x, y, w, h);

        var image = go.AddComponent<Image>();
        image.color = new Color(0.2f, 0.45f, 0.75f, 1f);

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;

        var textGo = CreateUiObject("Label", go.transform);
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        var text = textGo.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 16;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;

        return button;
    }

    private static Slider CreateSlider(Transform parent, string name, float x, float y, float w, float h)
    {
        var go = CreateUiObject(name, parent);
        TopLeft(go.GetComponent<RectTransform>(), x, y, w, h);

        var bg = CreateUiObject("Background", go.transform);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        var bgImage = bg.AddComponent<Image>();
        bgImage.color = new Color(0.15f, 0.18f, 0.22f, 1f);

        var fillArea = CreateUiObject("Fill Area", go.transform);
        var fillAreaRt = fillArea.GetComponent<RectTransform>();
        fillAreaRt.anchorMin = Vector2.zero;
        fillAreaRt.anchorMax = Vector2.one;
        fillAreaRt.offsetMin = new Vector2(4f, 0f);
        fillAreaRt.offsetMax = new Vector2(-4f, 0f);

        var fill = CreateUiObject("Fill", fillArea.transform);
        var fillRt = fill.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        var fillImage = fill.AddComponent<Image>();
        fillImage.color = new Color(0.35f, 0.7f, 1f, 1f);

        var handleArea = CreateUiObject("Handle Slide Area", go.transform);
        var handleAreaRt = handleArea.GetComponent<RectTransform>();
        handleAreaRt.anchorMin = Vector2.zero;
        handleAreaRt.anchorMax = Vector2.one;
        handleAreaRt.offsetMin = Vector2.zero;
        handleAreaRt.offsetMax = Vector2.zero;

        var handle = CreateUiObject("Handle", handleArea.transform);
        var handleRt = handle.GetComponent<RectTransform>();
        handleRt.sizeDelta = new Vector2(16f, 0f);
        var handleImage = handle.AddComponent<Image>();
        handleImage.color = Color.white;

        var slider = go.AddComponent<Slider>();
        slider.targetGraphic = handleImage;
        slider.fillRect = fillRt;
        slider.handleRect = handleRt;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0.25f;
        slider.maxValue = 8f;
        slider.value = 1f;
        return slider;
    }

    private static Toggle CreateToggle(Transform parent, string name, string label, float x, float y)
    {
        var go = CreateUiObject(name, parent);
        TopLeft(go.GetComponent<RectTransform>(), x, y, 140f, 24f);

        var bg = CreateUiObject("Background", go.transform);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0f, 0.5f);
        bgRt.anchorMax = new Vector2(0f, 0.5f);
        bgRt.pivot = new Vector2(0f, 0.5f);
        bgRt.anchoredPosition = Vector2.zero;
        bgRt.sizeDelta = new Vector2(22f, 22f);
        var bgImage = bg.AddComponent<Image>();
        bgImage.color = new Color(0.2f, 0.22f, 0.26f, 1f);

        var check = CreateUiObject("Checkmark", bg.transform);
        var checkRt = check.GetComponent<RectTransform>();
        checkRt.anchorMin = Vector2.zero;
        checkRt.anchorMax = Vector2.one;
        checkRt.offsetMin = new Vector2(3f, 3f);
        checkRt.offsetMax = new Vector2(-3f, -3f);
        var checkImage = check.AddComponent<Image>();
        checkImage.color = new Color(0.4f, 0.85f, 0.5f, 1f);

        var textGo = CreateUiObject("Label", go.transform);
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(28f, 0f);
        textRt.offsetMax = Vector2.zero;
        var text = textGo.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 14;
        text.alignment = TextAlignmentOptions.Left;
        text.color = Color.white;
        text.raycastTarget = false;

        var toggle = go.AddComponent<Toggle>();
        toggle.targetGraphic = bgImage;
        toggle.graphic = checkImage;
        toggle.isOn = false;
        return toggle;
    }
}
