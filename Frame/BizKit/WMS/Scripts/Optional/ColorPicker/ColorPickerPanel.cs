using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Frame.BizKit.WMS
{
/// <summary>
/// 颜色选择器面板（预制体驱动）。请通过 <see cref="ColorPicker"/> 全局 API 唤起。
/// </summary>
[DisallowMultipleComponent]
public class ColorPickerPanel : MonoBehaviour
{
    public const string ResourcesPrefabName = "ColorPickerPanel";

    [Header("根")]
    [SerializeField] private CanvasGroup m_rootGroup;

    [SerializeField] private Button m_btn_overlay;
    [SerializeField] private Button m_btn_close;
    [SerializeField] private Button m_btn_cancel;
    [SerializeField] private Button m_btn_confirm;

    [Header("取色区")]
    [SerializeField] private RawImage m_svImage;

    [SerializeField] private RawImage m_hueImage;
    [SerializeField] private RawImage m_alphaImage;
    [SerializeField] private Image m_img_alphaChecker;
    [SerializeField] private Image m_img_previewChecker;
    [SerializeField] private RectTransform m_svCursor;
    [SerializeField] private RectTransform m_hueCursor;
    [SerializeField] private RectTransform m_alphaCursor;

    [Header("预览")]
    [SerializeField] private Image m_img_newPreview;

    [SerializeField] private Image m_img_oldPreview;

    [Header("通道")]
    [SerializeField] private TMP_InputField m_ipf_hex;

    [SerializeField] private TMP_InputField m_ipf_r;
    [SerializeField] private TMP_InputField m_ipf_g;
    [SerializeField] private TMP_InputField m_ipf_b;
    [SerializeField] private TMP_InputField m_ipf_a;
    [SerializeField] private Slider m_sld_r;
    [SerializeField] private Slider m_sld_g;
    [SerializeField] private Slider m_sld_b;

    [Header("预设")]
    [SerializeField] private Button[] m_btn_presets;

    private static ColorPickerPanel _instance;

    private Texture2D _svTex;
    private Texture2D _hueTex;
    private Texture2D _alphaTex;
    private Texture2D _checkerTex;
    private Sprite _checkerSprite;
    private readonly RawImage[] _rgbGradientImages = new RawImage[3];
    private readonly Texture2D[] _rgbGradientTextures = new Texture2D[3];
    private readonly Color32[][] _rgbGradientPixels = new Color32[3][];
    private readonly Color32[] _rgbGradientStarts = new Color32[3];
    private readonly Color32[] _rgbGradientEnds = new Color32[3];
    private readonly bool[] _rgbGradientInitialized = new bool[3];
    private Texture2D _svCursorTex;
    private Sprite _svCursorSprite;

    private float _h, _s, _v, _a = 1f;
    private float _lastSvHue = -1f;
    private Color _lastAlphaRgb = new Color(-1, -1, -1, -1);
    private Color _oldColor = Color.white;
    private int _lastUiR = -1, _lastUiG = -1, _lastUiB = -1, _lastUiA = -1;
    private string _lastUiHex;
    private bool _suppressUi;
    private bool _wired;

    private Action<Color> _onChanged;
    private Action<Color> _onConfirm;
    private Action _onCancel;

    public static ColorPickerPanel Instance => EnsureInstance();

    public Color CurrentColor => ColorPickerMath.HsvToColor(_h, _s, _v, _a);
    public bool IsOpen => isActiveAndEnabled && m_rootGroup != null && m_rootGroup.blocksRaycasts;

    public void Show(Color initial, Action<Color> onConfirm = null, Action<Color> onChanged = null, Action onCancel = null)
    {
        var panel = EnsureInstance();
        if (panel == null)
        {
            Debug.LogError($"[ColorPicker] 找不到预制体 Resources/{ResourcesPrefabName}");
            return;
        }

        panel.Open(initial, onConfirm, onChanged, onCancel);
    }

    public void Hide(bool confirmed = false)
    {
        if (_instance != null)
            _instance.Close(confirmed);
    }

    private static ColorPickerPanel EnsureInstance()
    {
        if (_instance != null) return _instance;

        _instance = FindFirstObjectByType<ColorPickerPanel>(FindObjectsInactive.Include);
        if (_instance != null)
        {
            _instance.WireIfNeeded();
            return _instance;
        }

        var prefab = Resources.Load<GameObject>(ResourcesPrefabName);
        if (prefab == null) return null;

        var canvas = GameObject.FindGameObjectWithTag("MainCanvas")?.GetComponent<Canvas>() ?? FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            var root = new GameObject("ColorPickerCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
        }

        var go = Instantiate(prefab, canvas.transform, false);
        go.name = ResourcesPrefabName;
        _instance = go.GetComponent<ColorPickerPanel>();
        _instance.WireIfNeeded();
        _instance.SetVisible(false);
        return _instance;
    }

    private void Awake()
    {
        _instance = this;
        WireIfNeeded();
        if (!IsOpen) SetVisible(false);
    }

    private void OnDestroy()
    {
        DestroyTex(ref _svTex);
        DestroyTex(ref _hueTex);
        DestroyTex(ref _alphaTex);
        DestroyTex(ref _checkerTex);
        for (int i = 0; i < _rgbGradientTextures.Length; i++)
            DestroyTex(ref _rgbGradientTextures[i]);

        DestroyTex(ref _svCursorTex);
        if (_checkerSprite != null)
        {
            if (Application.isPlaying) Destroy(_checkerSprite);
            else DestroyImmediate(_checkerSprite);
            _checkerSprite = null;
        }

        if (_svCursorSprite != null)
        {
            if (Application.isPlaying) Destroy(_svCursorSprite);
            else DestroyImmediate(_svCursorSprite);
            _svCursorSprite = null;
        }

        if (_instance == this) _instance = null;
    }

    public void Open(Color initial, Action<Color> onConfirm, Action<Color> onChanged, Action onCancel)
    {
        WireIfNeeded();
        _onConfirm = onConfirm;
        _onChanged = onChanged;
        _onCancel = onCancel;
        _oldColor = initial;
        ColorPickerMath.ColorToHsv(initial, out _h, out _s, out _v);
        _a = initial.a;

        RefreshSvTexture(force: true);
        RefreshAlphaTexture(force: true);
        RefreshUi();

        transform.SetAsLastSibling();
        SetVisible(true);
        ColorPicker.RaiseOpened();
    }

    public void Close(bool confirmed)
    {
        var color = CurrentColor;
        if (confirmed)
        {
            _onConfirm?.Invoke(color);
            ColorPicker.RaiseConfirmed(color);
        }
        else
        {
            _onCancel?.Invoke();
            ColorPicker.RaiseCancelled();
        }

        _onConfirm = null;
        _onChanged = null;
        _onCancel = null;
        SetVisible(false);
        ColorPicker.RaiseClosed();
    }

    private void SetVisible(bool visible)
    {
        if (m_rootGroup == null) return;
        m_rootGroup.alpha = visible ? 1f : 0f;
        m_rootGroup.blocksRaycasts = visible;
        m_rootGroup.interactable = visible;
        if (visible) gameObject.SetActive(true);
    }

    /// <summary>
    /// 绑定事件
    /// </summary>
    private void WireIfNeeded()
    {
        if (_wired) return;
        _wired = true;

        if (m_rootGroup == null)
            m_rootGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

        BindBtn(m_btn_overlay, () => Close(false));
        BindBtn(m_btn_close, () => Close(false));
        BindBtn(m_btn_cancel, () => Close(false));
        BindBtn(m_btn_confirm, () => Close(true));

        BindDrag(m_svImage, OnSv);
        BindDrag(m_hueImage, OnHue);
        BindDrag(m_alphaImage, OnAlpha);

        BindChannel(m_sld_r, m_ipf_r, v =>
        {
            var c = CurrentColor;
            c.r = v;
            ApplyRgb(c);
        });
        BindChannel(m_sld_g, m_ipf_g, v =>
        {
            var c = CurrentColor;
            c.g = v;
            ApplyRgb(c);
        });
        BindChannel(m_sld_b, m_ipf_b, v =>
        {
            var c = CurrentColor;
            c.b = v;
            ApplyRgb(c);
        });
        _rgbGradientImages[0] = EnsureSliderGradient(m_sld_r);
        _rgbGradientImages[1] = EnsureSliderGradient(m_sld_g);
        _rgbGradientImages[2] = EnsureSliderGradient(m_sld_b);

        if (m_ipf_a != null)
        {
            m_ipf_a.onEndEdit.RemoveAllListeners();
            m_ipf_a.onEndEdit.AddListener(_ =>
            {
                if (_suppressUi) return;
                if (!int.TryParse(m_ipf_a.text, out int a)) return;
                _a = Mathf.Clamp01(a / 255f);
                RefreshUi();
                NotifyChanged();
            });
        }

        if (m_ipf_hex != null)
        {
            m_ipf_hex.onEndEdit.RemoveAllListeners();
            m_ipf_hex.onEndEdit.AddListener(OnHexEndEdit);
        }

        if (m_btn_presets != null)
        {
            foreach (var btn in m_btn_presets)
            {
                if (btn == null) continue;
                var img = btn.targetGraphic as Image ?? btn.GetComponent<Image>();
                if (img == null) continue;
                var captured = img.color;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => ApplyRgb(new Color(captured.r, captured.g, captured.b, _a)));
            }
        }

        EnsureStaticTextures();
        EnsureSvCursor();
    }

    #region UI事件

    private void EnsureStaticTextures()
    {
        if (_hueTex == null)
        {
            _hueTex = ColorPickerMath.CreateHueTexture(8, 256);
            if (m_hueImage != null) m_hueImage.texture = _hueTex;
        }

        if (_checkerTex != null) return;
        _checkerTex = ColorPickerMath.CreateCheckerTexture();
        _checkerSprite = Sprite.Create(_checkerTex, new Rect(0, 0, _checkerTex.width, _checkerTex.height), new Vector2(0.5f, 0.5f), 100f);
        ApplyChecker(m_img_alphaChecker);
        ApplyChecker(m_img_previewChecker);
    }

    private void ApplyChecker(Image image)
    {
        if (image == null || _checkerSprite == null) return;
        image.sprite = _checkerSprite;
        image.type = Image.Type.Tiled;
        image.pixelsPerUnitMultiplier = 2f;
    }

    private void EnsureSvCursor()
    {
        if (m_svCursor == null) return;

        var image = m_svCursor.GetComponent<Image>();
        if (image == null) image = m_svCursor.gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.color = Color.white;

        if (_svCursorTex == null)
        {
            const int size = 32;
            float center = (size - 1) * 0.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    Color32 color = new Color32(0, 0, 0, 0);
                    if (distance is >= 10f and <= 15f)
                        color = distance is < 12f or > 14f
                            ? new Color32(0, 0, 0, 230)
                            : new Color32(255, 255, 255, 255);
                    pixels[y * size + x] = color;
                }
            }

            _svCursorTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "ColorPicker SV Cursor",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            _svCursorTex.SetPixels32(pixels);
            _svCursorTex.Apply(false, true);
            _svCursorSprite = Sprite.Create(
                _svCursorTex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                size);
            _svCursorSprite.hideFlags = HideFlags.HideAndDontSave;
        }

        m_svCursor.sizeDelta = new Vector2(18f, 18f);
        image.sprite = _svCursorSprite;
    }

    private RawImage EnsureSliderGradient(Slider slider)
    {
        if (slider == null) return null;

        var background = slider.transform.Find("Background") as RectTransform;
        if (background == null) return null;

        var backgroundImage = background.GetComponent<Image>();
        if (backgroundImage != null) backgroundImage.enabled = false;

        const string gradientName = "Gradient";
        var gradientTransform = background.Find(gradientName) as RectTransform;
        RawImage gradient;
        if (gradientTransform == null)
        {
            gradient = new GameObject(
                gradientName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RawImage)).GetComponent<RawImage>();
            gradient.transform.SetParent(background, false);
            gradientTransform = gradient.rectTransform;
            gradientTransform.anchorMin = Vector2.zero;
            gradientTransform.anchorMax = Vector2.one;
            gradientTransform.offsetMin = Vector2.zero;
            gradientTransform.offsetMax = Vector2.zero;
        }
        else
        {
            gradient = gradientTransform.GetComponent<RawImage>();
        }

        gradient.raycastTarget = false;

        if (slider.fillRect != null)
        {
            var fillGraphic = slider.fillRect.GetComponent<Graphic>();
            if (fillGraphic != null) fillGraphic.enabled = false;
        }

        return gradient;
    }

    private void BindBtn(Button button, UnityAction action)
    {
        if (button == null) return;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    private void BindDrag(RawImage image, Action<Vector2, RectTransform> handler)
    {
        if (image == null) return;
        var area = image.GetComponent<ColorPickerDragArea>() ?? image.gameObject.AddComponent<ColorPickerDragArea>();
        area.OnDragLocal = handler;
    }

    private void BindChannel(Slider slider, TMP_InputField input, Action<float> onValue)
    {
        if (slider != null)
        {
            var hitArea = slider.GetComponent<Graphic>();
            if (hitArea != null) hitArea.raycastTarget = true;

            slider.minValue = 0;
            slider.maxValue = 255;
            slider.wholeNumbers = true;
            slider.onValueChanged.RemoveAllListeners();
            slider.onValueChanged.AddListener(v =>
            {
                if (!_suppressUi) onValue(v / 255f);
            });
        }

        if (input == null) return;
        input.contentType = TMP_InputField.ContentType.IntegerNumber;
        input.onEndEdit.RemoveAllListeners();
        input.onEndEdit.AddListener(_ =>
        {
            if (_suppressUi) return;
            if (!int.TryParse(input.text, out int iv)) return;
            iv = Mathf.Clamp(iv, 0, 255);
            if (slider != null) slider.SetValueWithoutNotify(iv);
            onValue(iv / 255f);
        });
    }

    private void OnHue(Vector2 local, RectTransform rt)
    {
        var size = rt.rect.size;
        float next = 1f - Mathf.Clamp01(local.y / Mathf.Max(size.y, 1e-4f));
        if (Mathf.Abs(next - _h) < 1e-5f) return;
        _h = next;
        RefreshSvTexture();
        RefreshAlphaTexture();
        RefreshUi();
        NotifyChanged();
    }

    private void OnAlpha(Vector2 local, RectTransform rt)
    {
        var size = rt.rect.size;
        float next = Mathf.Clamp01(local.x / Mathf.Max(size.x, 1e-4f));
        if (Mathf.Abs(next - _a) < 1e-5f) return;
        _a = next;
        RefreshUi();
        NotifyChanged();
    }

    private void OnSv(Vector2 local, RectTransform rt)
    {
        var size = rt.rect.size;
        float nextS = Mathf.Clamp01(local.x / Mathf.Max(size.x, 1e-4f));
        float nextV = Mathf.Clamp01(local.y / Mathf.Max(size.y, 1e-4f));
        if (Mathf.Abs(nextS - _s) < 1e-5f && Mathf.Abs(nextV - _v) < 1e-5f) return;
        _s = nextS;
        _v = nextV;
        RefreshAlphaTexture();
        RefreshUi();
        NotifyChanged();
    }

    private void ApplyRgb(Color c)
    {
        ColorPickerMath.ColorToHsv(c, out _h, out _s, out _v);
        _a = c.a;
        RefreshSvTexture();
        RefreshAlphaTexture();
        RefreshUi();
        NotifyChanged();
    }

    private void OnHexEndEdit(string text)
    {
        if (_suppressUi) return;
        if (ColorPickerMath.TryParseHex(text, out var c))
        {
            c.a = text.Replace("#", "").Length >= 8 ? c.a : _a;
            ApplyRgb(c);
        }
        else RefreshUi();
    }

    private void RefreshSvTexture(bool force = false)
    {
        if (!force && Mathf.Abs(_lastSvHue - _h) < 1e-5f && _svTex != null) return;
        _lastSvHue = _h;
        if (_svTex == null)
        {
            _svTex = ColorPickerMath.CreateSvTexture(_h);
            if (m_svImage != null) m_svImage.texture = _svTex;
        }
        else ColorPickerMath.FillSvTexture(_svTex, _h);
    }

    private void RefreshAlphaTexture(bool force = false)
    {
        var rgb = ColorPickerMath.HsvToColor(_h, _s, _v);
        if (!force && _lastAlphaRgb == rgb && _alphaTex != null) return;
        _lastAlphaRgb = rgb;
        if (_alphaTex == null)
        {
            _alphaTex = ColorPickerMath.CreateAlphaGradientTexture(rgb);
            if (m_alphaImage != null) m_alphaImage.texture = _alphaTex;
        }
        else ColorPickerMath.FillAlphaGradientTexture(_alphaTex, rgb);
    }

    private void RefreshUi()
    {
        _suppressUi = true;
        var c = CurrentColor;

        if (m_img_newPreview != null) m_img_newPreview.color = c;
        if (m_img_oldPreview != null) m_img_oldPreview.color = _oldColor;

        if (m_svCursor != null && m_svImage != null)
        {
            var size = m_svImage.rectTransform.rect.size;
            m_svCursor.anchoredPosition = new Vector2(_s * size.x, _v * size.y);
        }

        if (m_hueCursor != null && m_hueImage != null)
        {
            var size = m_hueImage.rectTransform.rect.size;
            m_hueCursor.anchoredPosition = new Vector2(0, (1f - _h) * size.y);
        }

        if (m_alphaCursor != null && m_alphaImage != null)
        {
            var size = m_alphaImage.rectTransform.rect.size;
            m_alphaCursor.anchoredPosition = new Vector2(_a * size.x, 0);
        }

        int r = Mathf.RoundToInt(c.r * 255f);
        int g = Mathf.RoundToInt(c.g * 255f);
        int b = Mathf.RoundToInt(c.b * 255f);
        int a = Mathf.RoundToInt(c.a * 255f);
        bool channelsChanged = r != _lastUiR || g != _lastUiG || b != _lastUiB || a != _lastUiA;

        SetChannel(m_sld_r, m_ipf_r, r, ref _lastUiR);
        SetChannel(m_sld_g, m_ipf_g, g, ref _lastUiG);
        SetChannel(m_sld_b, m_ipf_b, b, ref _lastUiB);
        RefreshRgbGradients(c);

        if (m_ipf_a != null && a != _lastUiA)
        {
            _lastUiA = a;
            m_ipf_a.SetTextWithoutNotify(a.ToString());
        }
        else
            _lastUiA = a;

        if (m_ipf_hex != null && channelsChanged)
        {
            var hex = "#" + ColorPickerMath.ToHex(c);
            _lastUiHex = hex;
            m_ipf_hex.SetTextWithoutNotify(hex);
        }

        _suppressUi = false;
    }

    #endregion

    private void RefreshRgbGradients(Color color)
    {
        RefreshRgbGradient(0, new Color(0f, color.g, color.b), new Color(1f, color.g, color.b));
        RefreshRgbGradient(1, new Color(color.r, 0f, color.b), new Color(color.r, 1f, color.b));
        RefreshRgbGradient(2, new Color(color.r, color.g, 0f), new Color(color.r, color.g, 1f));
    }

    private void RefreshRgbGradient(int index, Color start, Color end)
    {
        var image = _rgbGradientImages[index];
        if (image == null) return;

        Color32 start32 = start;
        Color32 end32 = end;
        if (_rgbGradientInitialized[index]
            && _rgbGradientStarts[index].Equals(start32)
            && _rgbGradientEnds[index].Equals(end32))
            return;

        _rgbGradientStarts[index] = start32;
        _rgbGradientEnds[index] = end32;
        _rgbGradientInitialized[index] = true;

        var texture = _rgbGradientTextures[index];
        if (texture == null)
        {
            const int width = 128;
            texture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
            {
                name = $"ColorPicker RGB Gradient {index}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            _rgbGradientTextures[index] = texture;
            _rgbGradientPixels[index] = new Color32[width];
            image.texture = texture;
        }

        var pixels = _rgbGradientPixels[index];
        float denominator = pixels.Length - 1f;
        for (int x = 0; x < pixels.Length; x++)
            pixels[x] = Color32.Lerp(start32, end32, x / denominator);

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
    }

    private static void SetChannel(Slider slider, TMP_InputField input, int value, ref int last)
    {
        if (value == last) return;
        last = value;
        if (slider != null) slider.SetValueWithoutNotify(value);
        if (input != null) input.SetTextWithoutNotify(value.ToString());
    }

    private void NotifyChanged()
    {
        var c = CurrentColor;
        _onChanged?.Invoke(c);
        ColorPicker.RaiseChanged(c);
    }

    private void DestroyTex(ref Texture2D tex)
    {
        if (tex == null) return;
        if (Application.isPlaying) Destroy(tex);
        else DestroyImmediate(tex);
        tex = null;
    }
}
}
