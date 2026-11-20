using Frame.BizKit.WMS;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ColorPicker 使用示例：挂到 Demo 场景 Canvas 下即可。
/// 展示 <see cref="ColorPickerButton"/> 与 <see cref="ColorPicker.Pick"/> 两种用法。
/// </summary>
public class ColorPickerDemo : MonoBehaviour
{
    [Header("色块按钮（组件方式）")]
    [SerializeField] private Image m_img_swatchA;
    [SerializeField] private Image m_img_swatchB;

    [Header("API 按钮")]
    [SerializeField] private Button m_btn_pickApi;
    [SerializeField] private Image m_img_apiTarget;
    [SerializeField] private TMP_Text m_txt_log;

    private void Awake()
    {
        EnsureSwatchButton(m_img_swatchA, new Color(0.2f, 0.75f, 0.9f, 1f));
        EnsureSwatchButton(m_img_swatchB, new Color(0.95f, 0.45f, 0.2f, 0.85f));

        if (m_btn_pickApi != null)
            m_btn_pickApi.onClick.AddListener(OnPickViaApi);

        ColorPicker.OnConfirmed += OnGlobalConfirmed;
        ColorPicker.OnCancelled += OnGlobalCancelled;
    }

    private void OnDestroy()
    {
        ColorPicker.OnConfirmed -= OnGlobalConfirmed;
        ColorPicker.OnCancelled -= OnGlobalCancelled;
    }

    private void OnGlobalConfirmed(Color c) => Log($"确认 #{ColorPickerMath.ToHex(c)}");
    private void OnGlobalCancelled() => Log("已取消");

    private void OnPickViaApi()
    {
        var target = m_img_apiTarget;
        if (target == null)
        {
            Log("未指定 API 目标 Image");
            return;
        }

        var backup = target.color;
        ColorPicker.Pick(
            backup,
            onConfirm: c =>
            {
                target.color = c;
                Log($"API 确认 #{ColorPickerMath.ToHex(c)}");
            },
            onChanged: c => target.color = c,
            onCancel: () =>
            {
                target.color = backup;
                Log("API 取消");
            });
    }

    private static void EnsureSwatchButton(Image image, Color initial)
    {
        if (image == null) return;
        image.color = initial;
        if (image.GetComponent<Button>() == null)
            image.gameObject.AddComponent<Button>().targetGraphic = image;
        if (image.GetComponent<ColorPickerButton>() == null)
            image.gameObject.AddComponent<ColorPickerButton>();
    }

    private void Log(string msg)
    {
        if (m_txt_log != null)
            m_txt_log.text = msg;
        Debug.Log($"[ColorPickerDemo] {msg}");
    }
}
