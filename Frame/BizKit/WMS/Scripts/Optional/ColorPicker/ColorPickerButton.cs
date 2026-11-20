using System;
using UnityEngine;
using UnityEngine.UI;

namespace Frame.BizKit.WMS
{
/// <summary>
/// 挂到任意按钮：点击即通过全局 <see cref="ColorPicker"/> 打开拾色器。
/// </summary>
[RequireComponent(typeof(Button))]
public class ColorPickerButton : MonoBehaviour
{
    [SerializeField] private Image m_img_colorPanel;
    [SerializeField] private bool m_includeAlpha = true;
    [SerializeField] private bool m_livePreview = true;

    private Button _button;
    private Color _color = Color.white;
    private Color _backup;

    public Color Color
    {
        get => _color;
        set
        {
            _color = value;
            ApplyToPanel(_color);
        }
    }

    public event Action<Color> OnColorConfirmed;
    public event Action<Color> OnColorPreview;

    private void Awake()
    {
        _button = GetComponent<Button>();
        if (m_img_colorPanel == null)
            m_img_colorPanel = GetComponent<Image>();
        if (m_img_colorPanel != null)
            _color = m_img_colorPanel.color;
        _button.onClick.AddListener(OpenPicker);
    }

    private void OnDestroy()
    {
        if (_button != null)
            _button.onClick.RemoveListener(OpenPicker);
    }

    public void OpenPicker()
    {
        _backup = _color;
        ColorPicker.Pick(
            _color,
            onConfirm: c =>
            {
                _color = m_includeAlpha ? c : new Color(c.r, c.g, c.b, _color.a);
                ApplyToPanel(_color);
                OnColorConfirmed?.Invoke(_color);
            },
            onChanged: m_livePreview
                ? c =>
                {
                    var preview = m_includeAlpha ? c : new Color(c.r, c.g, c.b, _color.a);
                    ApplyToPanel(preview);
                    OnColorPreview?.Invoke(preview);
                }
                : null,
            onCancel: () =>
            {
                _color = _backup;
                ApplyToPanel(_color);
            });
    }

    private void ApplyToPanel(Color c)
    {
        if (m_img_colorPanel != null)
            m_img_colorPanel.color = c;
    }
}
}
