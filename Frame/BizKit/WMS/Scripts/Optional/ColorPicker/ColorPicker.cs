using System;
using UnityEngine;
using UnityEngine.UI;

namespace Frame.BizKit.WMS
{
/// <summary>
/// 全局颜色拾取入口。
/// <code>
/// ColorPicker.Pick(image.color, onConfirm: c => image.color = c);
/// ColorPicker.PickFor(image);
/// </code>
/// </summary>
public static class ColorPicker
{
    public static event Action<Color> OnChanged;
    public static event Action<Color> OnConfirmed;
    public static event Action OnCancelled;
    public static event Action OnOpened;
    public static event Action OnClosed;

    public static bool IsPicking => ColorPickerPanel.Instance != null && ColorPickerPanel.Instance.IsOpen;

    public static Color CurrentColor =>
        ColorPickerPanel.Instance != null ? ColorPickerPanel.Instance.CurrentColor : Color.white;

    public static void Pick(
        Color initial,
        Action<Color> onConfirm = null,
        Action<Color> onChanged = null,
        Action onCancel = null) =>
        ColorPickerPanel.Instance.Show(initial, onConfirm, onChanged, onCancel);

    public static void PickFor(Image target, bool livePreview = true, bool includeAlpha = true)
    {
        if (target == null)
        {
            Debug.LogWarning("[ColorPicker] PickFor 目标 Image 为空");
            return;
        }

        var backup = target.color;
        Pick(
            backup,
            onConfirm: c => target.color = includeAlpha ? c : new Color(c.r, c.g, c.b, backup.a),
            onChanged: livePreview
                ? c => target.color = includeAlpha ? c : new Color(c.r, c.g, c.b, backup.a)
                : null,
            onCancel: () => target.color = backup);
    }

    public static void Cancel() => ColorPickerPanel.Instance.Hide(false);
    public static void Confirm() => ColorPickerPanel.Instance.Hide(true);

    internal static void RaiseChanged(Color c) => OnChanged?.Invoke(c);
    internal static void RaiseConfirmed(Color c) => OnConfirmed?.Invoke(c);
    internal static void RaiseCancelled() => OnCancelled?.Invoke();
    internal static void RaiseOpened() => OnOpened?.Invoke();
    internal static void RaiseClosed() => OnClosed?.Invoke();
}
}
