using UnityEngine;
using UnityEngine.UI;

namespace Frame.Core.UI
{
public class AutoFontColorByTime : MonoBehaviour
{
    [Header("目标字体(支持 Text / TMP_Text)")]
    [SerializeField] private Graphic targetGraphic;

    [Header("色相循环")]
    [Min(0.01f)]
    [SerializeField] private float cycleDuration = 6f;
    [Range(0f, 1f)]
    [SerializeField] private float startHue = 0.58f;

    [Header("饱和度与明度范围")]
    [Range(0f, 1f)]
    [SerializeField] private float saturationMin = 0.55f;
    [Range(0f, 1f)]
    [SerializeField] private float saturationMax = 0.85f;
    [Range(0f, 1f)]
    [SerializeField] private float valueMin = 0.9f;
    [Range(0f, 1f)]
    [SerializeField] private float valueMax = 1f;

    [Header("过渡曲线")]
    [SerializeField] private AnimationCurve saturationCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private AnimationCurve valueCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("透明度")]
    [SerializeField] private bool keepSourceAlpha = true;
    [Range(0f, 1f)]
    [SerializeField] private float overrideAlpha = 1f;

    private float sourceAlpha = 1f;

    private void Reset()
    {
        targetGraphic = GetComponent<Graphic>();
    }

    private void Awake()
    {
        if (targetGraphic == null)
        {
            targetGraphic = GetComponent<Graphic>();
        }
    }

    private void OnEnable()
    {
        if (targetGraphic == null)
        {
            return;
        }

        sourceAlpha = targetGraphic.color.a;
        ApplyColorByTime(0f);
    }

    private void Update()
    {
        if (targetGraphic == null)
        {
            return;
        }

        float t = Mathf.Repeat(Time.time / Mathf.Max(0.01f, cycleDuration), 1f);
        ApplyColorByTime(t);
    }

    private void ApplyColorByTime(float t)
    {
        float hue = Mathf.Repeat(startHue + t, 1f);
        float satLerp = Mathf.Clamp01(saturationCurve.Evaluate(t));
        float valLerp = Mathf.Clamp01(valueCurve.Evaluate(t));
        float saturation = Mathf.Lerp(saturationMin, saturationMax, satLerp);
        float value = Mathf.Lerp(valueMin, valueMax, valLerp);
        Color rgb = Color.HSVToRGB(hue, saturation, value);
        float alpha = keepSourceAlpha ? sourceAlpha : overrideAlpha;

        targetGraphic.color = new Color(rgb.r, rgb.g, rgb.b, alpha);
    }
}
}
