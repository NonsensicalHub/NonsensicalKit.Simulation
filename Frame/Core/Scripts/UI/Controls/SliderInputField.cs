using System;
using System.Globalization;
using NonsensicalKit.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


namespace Frame.Core.UI
{
public class SliderInputField : NonsensicalMono
{
    [SerializeField] private string m_signalID;
    [SerializeField] private Slider m_sld_slider;
    [SerializeField] private TMP_InputField m_ipf_num;
    [SerializeField] private Button m_btn_decrease;
    [SerializeField] private Button m_btn_increase;
    [SerializeField] private float m_minValue = 0f;
    [SerializeField] private float m_maxValue = 100f;
    [SerializeField] private bool m_wholeNumbers;
    [SerializeField] private string m_numberFormat = "0.##";
    [SerializeField] private float m_defaultValue;
    [SerializeField] private bool m_resetOnDisable;

    public event Action<float> ValueChanged;

    private float m_currentValue;

    public float CurrentValue => m_sld_slider == null ? m_currentValue : m_sld_slider.value;
    
    public string SignalID
    {
        get => m_signalID;
        set => m_signalID = value;
    }

    public float DefaultValue
    {
        get => m_defaultValue;
        set => m_defaultValue = value;
    }

    public float MinValue => m_minValue;
    public float MaxValue => m_maxValue;

    private void Awake()
    {
        if (m_sld_slider == null && m_ipf_num == null && m_btn_decrease == null && m_btn_increase == null)
        {
            Debug.LogWarning($"{nameof(SliderInputField)} 没有可用的 UI 组件引用。", this);
            return;
        }

        if (m_ipf_num != null)
        {
            m_ipf_num.contentType = m_wholeNumbers
                ? TMP_InputField.ContentType.IntegerNumber
                : TMP_InputField.ContentType.DecimalNumber;
            m_ipf_num.onEndEdit.AddListener(OnInputEndEdit);
        }

        if (m_sld_slider != null)
        {
            m_sld_slider.onValueChanged.AddListener(OnSliderValueChanged);
        }

        if (m_btn_decrease != null)
        {
            m_btn_decrease.onClick.AddListener(OnDecreaseClicked);
        }

        if (m_btn_increase != null)
        {
            m_btn_increase.onClick.AddListener(OnIncreaseClicked);
        }

        ApplySliderSettings();
        ResetToDefault();
        IOCC.Set<SliderInputField>(m_signalID,this);
        IOCC.Set<SliderInputField>(this);
    }

    private void OnDisable()
    {
        if (m_resetOnDisable)
        {
            ResetToDefault();
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        if (m_sld_slider != null)
        {
            m_sld_slider.onValueChanged.RemoveListener(OnSliderValueChanged);
        }

        if (m_ipf_num != null)
        {
            m_ipf_num.onEndEdit.RemoveListener(OnInputEndEdit);
        }

        if (m_btn_decrease != null)
        {
            m_btn_decrease.onClick.RemoveListener(OnDecreaseClicked);
        }

        if (m_btn_increase != null)
        {
            m_btn_increase.onClick.RemoveListener(OnIncreaseClicked);
        }
    }


    private void OnSliderValueChanged(float value)
    {
        SetValue(value, true);
    }

    private void OnInputEndEdit(string input)
    {
        if (m_ipf_num == null)
        {
            return;
        }

        if (!float.TryParse(input, out var parsedValue))
        {
            SyncInputFromValue(CurrentValue);
            return;
        }

        SetValue(parsedValue, true);
    }

    public void ResetToDefault()
    {
        SetValue(m_defaultValue, true,true);
    }

    public void SetRange(float minValue, float maxValue)
    {
        if (maxValue < minValue)
        {
            (minValue, maxValue) = (maxValue, minValue);
        }

        m_minValue = minValue;
        m_maxValue = maxValue;
        ApplySliderSettings();
    }

    public void SetWholeNumbers(bool wholeNumbers)
    {
        m_wholeNumbers = wholeNumbers;
        ApplySliderSettings();
    }

    private void ApplySliderSettings()
    {
        var minValue = Mathf.Min(m_minValue, m_maxValue);
        var maxValue = Mathf.Max(m_minValue, m_maxValue);
        m_minValue = minValue;
        m_maxValue = maxValue;

        if (m_sld_slider != null)
        {
            m_sld_slider.minValue = m_minValue;
            m_sld_slider.maxValue = m_maxValue;
            m_sld_slider.wholeNumbers = m_wholeNumbers;
        }

        m_currentValue = ClampValue(CurrentValue);
        SyncAllFromValue(m_currentValue);
    }

    private void OnDecreaseClicked()
    {
        SetValue(CurrentValue - 1f, true);
    }

    private void OnIncreaseClicked()
    {
        SetValue(CurrentValue + 1f, true);
    }

    public void SetValue(float value, bool notify,bool mandatory=false )
    {
        var clampedValue = ClampValue(value);
        var changed =mandatory|| !Mathf.Approximately(clampedValue, m_currentValue);
        m_currentValue = clampedValue;

        SyncAllFromValue(clampedValue);

        if (notify && changed)
        {
            ValueChanged?.Invoke(clampedValue);
            PublishWithID("SliderValueChanged", m_signalID, clampedValue);
        }
    }

    private float ClampValue(float value)
    {
        var clampedValue = Mathf.Clamp(value, m_minValue, m_maxValue);
        if (m_wholeNumbers)
        {
            clampedValue = Mathf.Round(clampedValue);
        }

        return clampedValue;
    }

    private void SyncAllFromValue(float value)
    {
        if (m_sld_slider != null)
        {
            m_sld_slider.SetValueWithoutNotify(value);
        }

        SyncInputFromValue(value);
        UpdateButtonStates(value);
    }

    private void SyncInputFromValue(float value)
    {
        if (m_ipf_num == null)
        {
            return;
        }

        var displayValue = m_wholeNumbers
            ? Mathf.RoundToInt(value).ToString()
            : value.ToString(m_numberFormat);

        m_ipf_num.SetTextWithoutNotify(displayValue);
    }

    private void UpdateButtonStates(float value)
    {
        const float tolerance = 0.0001f;

        if (m_btn_decrease != null)
        {
            m_btn_decrease.interactable = value > m_minValue + tolerance;
        }

        if (m_btn_increase != null)
        {
            m_btn_increase.interactable = value < m_maxValue - tolerance;
        }
    }
}
}
