#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Frame.BizKit.WMS;
using NaughtyAttributes;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Demo 工具：生成 / 重绑 ColorPicker 预制体（不污染运行时功能代码）。</summary>
public class ColorPickerPrefabBuilder : MonoBehaviour
{
    [SerializeField] private string m_prefabDir = "Assets/Resources/ColorPicker";
    [SerializeField] private string m_prefabPath = "/ColorPickerPanel.prefab";

    [Button("生成颜色选择器预制体")]
    public void Build()
    {
        if (!Directory.Exists(m_prefabDir))
            Directory.CreateDirectory(m_prefabDir);
        m_prefabPath += m_prefabDir;

        var root = BuildHierarchy();
        PrefabUtility.SaveAsPrefabAsset(root, m_prefabPath);
        DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[ColorPicker] 预制体已生成：{m_prefabPath}");
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(m_prefabPath));
    }

    /// <summary>保留手工布局，只按节点路径重新绑定脚本引用。</summary>
    [Button("重新绑定颜色选择器预制体引用")]
    public void RebindExistingPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(m_prefabPath) == null)
        {
            Debug.LogError($"[ColorPicker] 未找到预制体：{m_prefabPath}，请先生成。");
            return;
        }

        var root = PrefabUtility.LoadPrefabContents(m_prefabPath);
        try
        {
            BindPanel(root);
            PrefabUtility.SaveAsPrefabAsset(root, m_prefabPath);
            Debug.Log($"[ColorPicker] 已重新绑定引用（布局未改动）：{m_prefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    /// <summary>通过 SerializedObject 写入私有序列化字段，运行时面板无需 EditorBind。</summary>
    private static void BindPanel(GameObject root)
    {
        var panel = root.GetComponent<ColorPickerPanel>();
        if (panel == null)
            panel = root.AddComponent<ColorPickerPanel>();

        T Find<T>(string path) where T : Component
        {
            var t = root.transform.Find(path);
            return t != null ? t.GetComponent<T>() : null;
        }

        var presets = new List<Button>();
        var presetsRoot = root.transform.Find("Panel/Body/Presets");
        if (presetsRoot != null)
        {
            for (int i = 0; i < presetsRoot.childCount; i++)
            {
                var child = presetsRoot.GetChild(i);
                if (!child.name.StartsWith("Preset_")) continue;
                var btn = child.GetComponent<Button>();
                if (btn != null) presets.Add(btn);
            }
        }

        var so = new SerializedObject(panel);
        void Set(string prop, Object value) => so.FindProperty(prop).objectReferenceValue = value;

        Set("m_rootGroup", root.GetComponent<CanvasGroup>());
        Set("m_btn_overlay", Find<Button>("Overlay"));
        Set("m_btn_close", Find<Button>("Panel/Header/Close"));
        Set("m_btn_cancel", Find<Button>("Panel/Footer/Cancel"));
        Set("m_btn_confirm", Find<Button>("Panel/Footer/OK"));
        Set("m_svImage", Find<RawImage>("Panel/Body/TopRow/SV"));
        Set("m_hueImage", Find<RawImage>("Panel/Body/TopRow/Hue"));
        Set("m_alphaImage", Find<RawImage>("Panel/Body/AlphaRow/AlphaTrack/AlphaGradient"));
        Set("m_img_alphaChecker", Find<Image>("Panel/Body/AlphaRow/AlphaTrack/AlphaChecker"));
        Set("m_img_previewChecker", Find<Image>("Panel/Body/TopRow/Preview/PreviewFrame/Checker"));
        Set("m_svCursor", Find<RectTransform>("Panel/Body/TopRow/SV/SVCursor"));
        Set("m_hueCursor", Find<RectTransform>("Panel/Body/TopRow/Hue/HueCursor"));
        Set("m_alphaCursor", Find<RectTransform>("Panel/Body/AlphaRow/AlphaTrack/AlphaCursor"));
        Set("m_img_newPreview", Find<Image>("Panel/Body/TopRow/Preview/PreviewFrame/New"));
        Set("m_img_oldPreview", Find<Image>("Panel/Body/TopRow/Preview/PreviewFrame/Old"));
        Set("m_ipf_hex", Find<TMP_InputField>("Panel/Body/Channels/HexInput"));
        Set("m_ipf_r", Find<TMP_InputField>("Panel/Body/Channels/RInput"));
        Set("m_ipf_g", Find<TMP_InputField>("Panel/Body/Channels/GInput"));
        Set("m_ipf_b", Find<TMP_InputField>("Panel/Body/Channels/BInput"));
        Set("m_ipf_a", Find<TMP_InputField>("Panel/Body/AInput"));
        Set("m_sld_r", Find<Slider>("Panel/Body/Channels/RSlider"));
        Set("m_sld_g", Find<Slider>("Panel/Body/Channels/GSlider"));
        Set("m_sld_b", Find<Slider>("Panel/Body/Channels/BSlider"));

        var arr = so.FindProperty("m_btn_presets");
        arr.arraySize = presets.Count;
        for (int i = 0; i < presets.Count; i++)
            arr.GetArrayElementAtIndex(i).objectReferenceValue = presets[i];

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    public static GameObject BuildHierarchy()
    {
        var root = new GameObject("ColorPickerPanel", typeof(RectTransform), typeof(CanvasGroup), typeof(ColorPickerPanel));
        var rootRt = (RectTransform)root.transform;
        Stretch(rootRt);

        var rootGroup = root.GetComponent<CanvasGroup>();

        var overlay = Img("Overlay", rootRt, ColorPickerStyles.Overlay);
        Stretch(overlay.rectTransform);
        var overlayBtn = overlay.gameObject.AddComponent<Button>();
        overlayBtn.transition = Selectable.Transition.None;

        var panel = Img("Panel", rootRt, ColorPickerStyles.PanelBg);
        var panelRt = panel.rectTransform;
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(ColorPickerStyles.PanelWidth, ColorPickerStyles.PanelHeight);

        var border = Img("Border", panelRt, Color.clear);
        Stretch(border.rectTransform);
        var outline = border.gameObject.AddComponent<Outline>();
        outline.effectColor = ColorPickerStyles.PanelBorder;
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        var header = Img("Header", panelRt, ColorPickerStyles.HeaderBg);
        var headerRt = header.rectTransform;
        headerRt.anchorMin = new Vector2(0, 1);
        headerRt.anchorMax = new Vector2(1, 1);
        headerRt.pivot = new Vector2(0.5f, 1);
        headerRt.sizeDelta = new Vector2(0, 48);

        var accent = Img("AccentBar", headerRt, ColorPickerStyles.Accent);
        var art = accent.rectTransform;
        art.anchorMin = new Vector2(0, 0);
        art.anchorMax = new Vector2(0, 1);
        art.pivot = new Vector2(0, 0.5f);
        art.sizeDelta = new Vector2(4, 0);

        Label("Title", headerRt, "颜色选择器", 18, FontStyles.Bold, TextAlignmentOptions.MidlineLeft,
            new Vector2(18, -9), new Vector2(220, 30), ColorPickerStyles.TextPrimary);

        var closeRt = TextBtn("Close", headerRt, "✕", 16, ColorPickerStyles.TextSecondary, Color.clear)
            .GetComponent<RectTransform>();
        closeRt.anchorMin = closeRt.anchorMax = new Vector2(1, 0.5f);
        closeRt.pivot = new Vector2(1, 0.5f);
        closeRt.sizeDelta = new Vector2(40, 36);
        closeRt.anchoredPosition = new Vector2(-8, 0);

        var body = new GameObject("Body", typeof(RectTransform)).GetComponent<RectTransform>();
        body.SetParent(panelRt, false);
        body.anchorMin = Vector2.zero;
        body.anchorMax = Vector2.one;
        body.offsetMin = new Vector2(18, 64);
        body.offsetMax = new Vector2(-18, -56);

        var top = new GameObject("TopRow", typeof(RectTransform)).GetComponent<RectTransform>();
        top.SetParent(body, false);
        top.anchorMin = new Vector2(0, 1);
        top.anchorMax = new Vector2(1, 1);
        top.pivot = new Vector2(0.5f, 1);
        top.sizeDelta = new Vector2(0, ColorPickerStyles.SvSize);

        var sv = Raw("SV", top);
        var svRt = sv.rectTransform;
        svRt.anchorMin = svRt.anchorMax = new Vector2(0, 0);
        svRt.pivot = new Vector2(0, 0);
        svRt.sizeDelta = new Vector2(ColorPickerStyles.SvSize, ColorPickerStyles.SvSize);
        CursorRing("SVCursor", svRt, 14f);

        var hue = Raw("Hue", top);
        var hueRt = hue.rectTransform;
        hueRt.anchorMin = hueRt.anchorMax = new Vector2(0, 0);
        hueRt.pivot = new Vector2(0, 0);
        hueRt.sizeDelta = new Vector2(ColorPickerStyles.HueWidth, ColorPickerStyles.SvSize);
        hueRt.anchoredPosition = new Vector2(ColorPickerStyles.SvSize + 14f, 0);
        HueMarker("HueCursor", hueRt);

        var preview = new GameObject("Preview", typeof(RectTransform)).GetComponent<RectTransform>();
        preview.SetParent(top, false);
        preview.anchorMin = preview.anchorMax = new Vector2(1, 1);
        preview.pivot = new Vector2(1, 1);
        preview.sizeDelta = new Vector2(110, 110);

        Label("PreviewLabel", preview, "预览", 13, FontStyles.Normal, TextAlignmentOptions.BottomLeft,
            new Vector2(0, -10), new Vector2(110, 20), ColorPickerStyles.TextSecondary);

        var frame = Img("PreviewFrame", preview, ColorPickerStyles.FieldBorder);
        var frt = frame.rectTransform;
        frt.anchorMin = new Vector2(0, 0);
        frt.anchorMax = new Vector2(1, 0);
        frt.pivot = new Vector2(0.5f, 0);
        frt.sizeDelta = new Vector2(0, 78);

        Stretch(Img("Checker", frt, Color.white).rectTransform, 2);

        var newPreview = Img("New", frt, Color.cyan);
        var nrt = newPreview.rectTransform;
        nrt.anchorMin = new Vector2(0, 0);
        nrt.anchorMax = new Vector2(1, 0.5f);
        nrt.offsetMin = new Vector2(2, 2);
        nrt.offsetMax = new Vector2(-2, 0);

        var oldPreview = Img("Old", frt, Color.white);
        var ort = oldPreview.rectTransform;
        ort.anchorMin = new Vector2(0, 0.5f);
        ort.anchorMax = new Vector2(1, 1);
        ort.offsetMin = new Vector2(2, 0);
        ort.offsetMax = new Vector2(-2, -2);

        // Alpha 行留出右侧空间给 A 输入框（与手工调好的预制体一致）
        float alphaTop = -(ColorPickerStyles.SvSize + 14f);
        var alphaRow = new GameObject("AlphaRow", typeof(RectTransform)).GetComponent<RectTransform>();
        alphaRow.SetParent(body, false);
        alphaRow.anchorMin = new Vector2(0, 1);
        alphaRow.anchorMax = new Vector2(1, 1);
        alphaRow.pivot = new Vector2(0.5f, 1);
        alphaRow.sizeDelta = new Vector2(-56.79f, 28f);
        alphaRow.anchoredPosition = new Vector2(-28.4f, alphaTop);

        Label("ALabel", alphaRow, "A", 14, FontStyles.Bold, TextAlignmentOptions.MidlineLeft,
            new Vector2(0, 0), new Vector2(20, 28), ColorPickerStyles.TextSecondary);

        var track = Img("AlphaTrack", alphaRow, ColorPickerStyles.FieldBg);
        var trt = track.rectTransform;
        trt.anchorMin = new Vector2(0, 0.5f);
        trt.anchorMax = new Vector2(1, 0.5f);
        trt.pivot = new Vector2(0.5f, 0.5f);
        trt.anchoredPosition = new Vector2(12f, 0f);
        trt.sizeDelta = new Vector2(-24f, 20f);

        Stretch(Img("AlphaChecker", trt, Color.white).rectTransform, 1);

        var alpha = Raw("AlphaGradient", trt);
        Stretch(alpha.rectTransform, 1);
        alpha.rectTransform.pivot = new Vector2(0f, 0.5f);
        AlphaMarker("AlphaCursor", trt);

        // A 输入框与 Alpha 行对齐，放在 Body 右侧
        var aInput = InputField("AInput", body, "255");
        var airt = aInput.GetComponent<RectTransform>();
        airt.anchorMin = airt.anchorMax = new Vector2(1, 1);
        airt.pivot = new Vector2(1, 1);
        airt.sizeDelta = new Vector2(52f, 28f);
        airt.anchoredPosition = new Vector2(0f, alphaTop + 0.4f);

        var channels = new GameObject("Channels", typeof(RectTransform)).GetComponent<RectTransform>();
        channels.SetParent(body, false);
        channels.anchorMin = new Vector2(0, 1);
        channels.anchorMax = new Vector2(1, 1);
        channels.pivot = new Vector2(0.5f, 1);
        channels.sizeDelta = new Vector2(0, 130);
        channels.anchoredPosition = new Vector2(0, -(ColorPickerStyles.SvSize + 52));

        float y = 0;
        Channel(channels, "R", ref y, out _);
        Channel(channels, "G", ref y, out _);
        Channel(channels, "B", ref y, out _);

        Label("HexLabel", channels, "Hex", 13, FontStyles.Normal, TextAlignmentOptions.MidlineLeft,
            new Vector2(0.8f, -85.4f), new Vector2(36f, 26f), ColorPickerStyles.TextSecondary);
        var hex = InputField("HexInput", channels, "#FFFFFF");
        var hrt = hex.GetComponent<RectTransform>();
        hrt.anchorMin = new Vector2(0, 1);
        hrt.anchorMax = new Vector2(1, 1);
        hrt.pivot = new Vector2(0, 1);
        hrt.sizeDelta = new Vector2(-102.1f, 26f);
        hrt.anchoredPosition = new Vector2(46.1f, -86.5f);

        var presetsRow = new GameObject("Presets", typeof(RectTransform)).GetComponent<RectTransform>();
        presetsRow.SetParent(body, false);
        presetsRow.anchorMin = new Vector2(0, 0);
        presetsRow.anchorMax = new Vector2(1, 0);
        presetsRow.pivot = new Vector2(0.5f, 0);
        presetsRow.sizeDelta = new Vector2(0, 36);
        presetsRow.anchoredPosition = new Vector2(0, -21.7f);

        Label("PresetLabel", presetsRow, "预设", 12, FontStyles.Normal, TextAlignmentOptions.MidlineLeft,
            new Vector2(0, 0), new Vector2(36, 36), ColorPickerStyles.TextSecondary);

        Color[] presets =
        {
            new Color(1f, 0.25f, 0.25f), new Color(1f, 0.55f, 0.15f), new Color(1f, 0.85f, 0.2f),
            new Color(0.35f, 0.85f, 0.4f), new Color(0.2f, 0.78f, 0.9f), new Color(0.35f, 0.5f, 1f),
            new Color(0.7f, 0.4f, 0.95f), Color.white, new Color(0.55f, 0.55f, 0.55f),
            new Color(0.12f, 0.14f, 0.18f),
        };
        float px = 40;
        for (int i = 0; i < presets.Length; i++)
        {
            var img = Img($"Preset_{i}", presetsRow, presets[i]);
            var prt = img.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0, 0.5f);
            prt.pivot = new Vector2(0, 0.5f);
            prt.sizeDelta = new Vector2(26, 26);
            prt.anchoredPosition = new Vector2(px, 0);
            var ol = img.gameObject.AddComponent<Outline>();
            ol.effectColor = ColorPickerStyles.FieldBorder;
            ol.effectDistance = new Vector2(1, -1);
            img.gameObject.AddComponent<Button>().targetGraphic = img;
            px += 32;
        }

        var footer = new GameObject("Footer", typeof(RectTransform)).GetComponent<RectTransform>();
        footer.SetParent(panelRt, false);
        footer.anchorMin = new Vector2(0, 0);
        footer.anchorMax = new Vector2(1, 0);
        footer.pivot = new Vector2(0.5f, 0);
        footer.sizeDelta = new Vector2(0, 48f);

        var crt = TextBtn("Cancel", footer, "取消", 15, ColorPickerStyles.TextPrimary, ColorPickerStyles.ButtonBg)
            .GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(1, 0.5f);
        crt.pivot = new Vector2(1, 0.5f);
        crt.sizeDelta = new Vector2(96, 34);
        crt.anchoredPosition = new Vector2(-122, 0);

        var okrt = TextBtn("OK", footer, "确定", 15, Color.white, ColorPickerStyles.ButtonConfirm)
            .GetComponent<RectTransform>();
        okrt.anchorMin = okrt.anchorMax = new Vector2(1, 0.5f);
        okrt.pivot = new Vector2(1, 0.5f);
        okrt.sizeDelta = new Vector2(96, 34);
        okrt.anchoredPosition = new Vector2(-18, 0);

        BindPanel(root);

        rootGroup.alpha = 0f;
        rootGroup.blocksRaycasts = false;
        rootGroup.interactable = false;
        return root;
    }

    private static Slider Channel(RectTransform parent, string name, ref float y, out TMP_InputField input)
    {
        Label(name + "Label", parent, name, 13, FontStyles.Bold, TextAlignmentOptions.MidlineLeft,
            new Vector2(0, -y), new Vector2(20, 24), ColorPickerStyles.Accent);

        var slider = MakeSlider(name + "Slider", parent);
        var srt = slider.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(0, 1);
        srt.anchorMax = new Vector2(1, 1);
        srt.pivot = new Vector2(0, 0.5f);
        srt.sizeDelta = new Vector2(-80f, 20f);
        srt.anchoredPosition = new Vector2(24f, -y - 12f);

        input = InputField(name + "Input", parent, "255");
        var irt = input.GetComponent<RectTransform>();
        irt.anchorMin = irt.anchorMax = new Vector2(1, 1);
        irt.pivot = new Vector2(1, 1);
        irt.sizeDelta = new Vector2(52, 24);
        irt.anchoredPosition = new Vector2(0, -y);
        y += 30;
        return slider;
    }

    private static Image Img(string name, Transform parent, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = c;
        return img;
    }

    private static RawImage Raw(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RawImage>();
    }

    private static TMP_Text Label(string name, Transform parent, string text, float size, FontStyles style,
        TextAlignmentOptions align, Vector2 pos, Vector2 sizeDelta, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color = color;
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = false;
        var rt = tmp.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        return tmp;
    }

    private static Button TextBtn(string name, Transform parent, string text, float fontSize, Color textColor, Color bg)
    {
        var img = Img(name, parent, bg);
        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var label = Label("Label", img.transform, text, fontSize, FontStyles.Normal,
            TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, textColor);
        Stretch(label.rectTransform);
        label.alignment = TextAlignmentOptions.Center;
        return btn;
    }

    private static TMP_InputField InputField(string name, Transform parent, string placeholder)
    {
        var bg = Img(name, parent, ColorPickerStyles.FieldBg);
        var ol = bg.gameObject.AddComponent<Outline>();
        ol.effectColor = ColorPickerStyles.FieldBorder;
        ol.effectDistance = new Vector2(1, -1);

        var textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        textArea.transform.SetParent(bg.transform, false);
        Stretch((RectTransform)textArea.transform, 6);

        var text = Label("Text", textArea.transform, "", 13, FontStyles.Normal,
            TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero, ColorPickerStyles.TextPrimary);
        Stretch(text.rectTransform);

        var ph = Label("Placeholder", textArea.transform, placeholder, 13, FontStyles.Italic,
            TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero,
            new Color(ColorPickerStyles.TextSecondary.r, ColorPickerStyles.TextSecondary.g,
                ColorPickerStyles.TextSecondary.b, 0.45f));
        Stretch(ph.rectTransform);

        var input = bg.gameObject.AddComponent<TMP_InputField>();
        input.textViewport = (RectTransform)textArea.transform;
        input.textComponent = text;
        input.placeholder = ph;
        input.caretColor = ColorPickerStyles.Accent;
        input.selectionColor = ColorPickerStyles.AccentSoft;
        return input;
    }

    private static Slider MakeSlider(string name, Transform parent)
    {
        var root = Img(name, parent, Color.clear);
        root.raycastTarget = true;
        var bg = Img("Background", root.transform, ColorPickerStyles.FieldBg);
        Stretch(bg.rectTransform);
        bg.rectTransform.offsetMin = new Vector2(0, 4);
        bg.rectTransform.offsetMax = new Vector2(0, -4);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform)).GetComponent<RectTransform>();
        fillArea.SetParent(root.transform, false);
        Stretch(fillArea);
        fillArea.offsetMin = new Vector2(0, 4);
        fillArea.offsetMax = new Vector2(0, -4);
        var fill = Img("Fill", fillArea, ColorPickerStyles.AccentSoft);
        Stretch(fill.rectTransform);

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform)).GetComponent<RectTransform>();
        handleArea.SetParent(root.transform, false);
        Stretch(handleArea);
        var handle = Img("Handle", handleArea, ColorPickerStyles.Accent);
        handle.rectTransform.sizeDelta = new Vector2(12, 18);

        var slider = root.gameObject.AddComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        return slider;
    }

    private static RectTransform CursorRing(string name, Transform parent, float size)
    {
        var img = Img(name, parent, Color.clear);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        var ring = Img("Ring", rt, Color.clear);
        Stretch(ring.rectTransform);
        var o1 = ring.gameObject.AddComponent<Outline>();
        o1.effectColor = ColorPickerStyles.CursorRing;
        o1.effectDistance = new Vector2(1.5f, -1.5f);
        var o2 = ring.gameObject.AddComponent<Outline>();
        o2.effectColor = new Color(0, 0, 0, 0.65f);
        o2.effectDistance = new Vector2(3f, -3f);
        img.raycastTarget = false;
        ring.raycastTarget = false;
        return rt;
    }

    private static RectTransform HueMarker(string name, Transform parent)
    {
        var img = Img(name, parent, ColorPickerStyles.CursorRing);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(ColorPickerStyles.HueWidth + 8, 4);
        img.raycastTarget = false;
        return rt;
    }

    private static RectTransform AlphaMarker(string name, Transform parent)
    {
        var img = Img(name, parent, ColorPickerStyles.CursorRing);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(4, 22);
        img.raycastTarget = false;
        return rt;
    }

    private static void Stretch(RectTransform rt, float inset = 0)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }
}

#endif
