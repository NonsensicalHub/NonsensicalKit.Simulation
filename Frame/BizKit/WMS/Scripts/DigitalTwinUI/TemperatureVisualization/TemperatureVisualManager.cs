using System;
using NonsensicalKit.Core;
using NonsensicalKit.Core.DagLogicNode;
using NonsensicalKit.UGUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Frame.BizKit.WMS
{
/// <summary>
/// 温度可视化 UI：只负责交互采集，通过 IOCC.PublishWithID 下发命令，
/// 由对应库区的 TemperatureVisualizationController 订阅处理。
/// </summary>
public class TemperatureVisualManager : NonsensicalUI
{
    // 与 TemperatureVisualization.TemperatureVisualEvents 保持同名，避免 Frame 直接依赖 Tools 类型。
    private const string EvtSetVisible = "TemperatureVisual.SetVisible";
    private const string EvtSetMode = "TemperatureVisual.SetMode";
    private const string EvtSetSlicePositionXY = "TemperatureVisual.SetSlicePositionXY";
    private const string EvtSetSlicePositionXZ = "TemperatureVisual.SetSlicePositionXZ";
    private const string EvtSetSlicePositionYZ = "TemperatureVisual.SetSlicePositionYZ";
    private const string EvtSetShowSliceXY = "TemperatureVisual.SetShowSliceXY";
    private const string EvtSetShowSliceXZ = "TemperatureVisual.SetShowSliceXZ";
    private const string EvtSetShowSliceYZ = "TemperatureVisual.SetShowSliceYZ";
    private const string EvtSetIsoTemperature = "TemperatureVisual.SetIsoTemperature";

    /// <summary>切片模式，对应 VisualizationMode.Slice = 0。</summary>
    private const int ModeSlice = 0;

    [SerializeField] private string m_managerID;
    [SerializeField] private TMP_Dropdown m_modeDropdown;
    [SerializeField] private Slider m_isoTemperatureSlider;
    [SerializeField] private Slider m_sliceXYSlider;
    [SerializeField] private Slider m_sliceXZSlider;
    [SerializeField] private Slider m_sliceYZSlider;
    [SerializeField] private Toggle m_showSliceXYToggle;
    [SerializeField] private Toggle m_showSliceXZToggle;
    [SerializeField] private Toggle m_showSliceYZToggle;

    [SerializeField] private TMP_Text m_sliceXYValueText;
    [SerializeField] private TMP_Text m_sliceXZValueText;
    [SerializeField] private TMP_Text m_sliceYZValueText;
    [SerializeField] private TMP_Text m_isoTemperatureValueText;

    protected override void Awake()
    {
        base.Awake();

        m_modeDropdown.onValueChanged.AddListener(OnModeChange);

        m_sliceXYSlider?.onValueChanged.AddListener(OnSliceXYChanged);
        m_sliceXZSlider?.onValueChanged.AddListener(OnSliceXZChanged);
        m_sliceYZSlider?.onValueChanged.AddListener(OnSliceYZChanged);

        m_isoTemperatureSlider?.onValueChanged.AddListener(OnIsoTemperatureChanged);

        m_showSliceXYToggle?.onValueChanged.AddListener(v => PublishCmd(EvtSetShowSliceXY, v));
        m_showSliceXZToggle?.onValueChanged.AddListener(v => PublishCmd(EvtSetShowSliceXZ, v));
        m_showSliceYZToggle?.onValueChanged.AddListener(v => PublishCmd(EvtSetShowSliceYZ, v));

        BindSliderValueLabel(m_sliceXYSlider, m_sliceXYValueText, FormatNormalized);
        BindSliderValueLabel(m_sliceXZSlider, m_sliceXZValueText, FormatNormalized);
        BindSliderValueLabel(m_sliceYZSlider, m_sliceYZValueText, FormatNormalized);
        BindSliderValueLabel(m_isoTemperatureSlider, m_isoTemperatureValueText, FormatTemperature);

        Subscribe((int)DagLogicNodeEnum.NodeExit, "库存管理", OnNodeExit);
    }

    /// <summary>
    /// 当存在多个库的时候,必须设置此ID
    /// </summary>
    /// <param name="id">库区ID</param>
    public void Open(string libID)
    {
        m_managerID = libID;
        OpenVisual();
    }

    public void Open()
    {
        m_managerID = Execute<string>("getCurrentLibraryName");
        OpenVisual();
    }

    public void Close()
    {
        CloseVisual();
    }

    public void SwitchHandle(bool isOn)
    {
        if (isOn)
        {
            Open();
        }
        else
        {
            Close();
        }
    }

    private void OpenVisual()
    {
        OpenSelf();
        if (string.IsNullOrEmpty(m_managerID))
        {
            Debug.LogWarning($"[{nameof(TemperatureVisualManager)}] 库区 ID 为空，温度可视化命令将无法路由到 Controller。");
        }

        PublishCmd(EvtSetVisible, true);
        IOCC.Set("EnableGpuPicking", false);
    }

    private void CloseVisual()
    {
        PublishCmd(EvtSetVisible, false);
        CloseSelf();
        m_managerID = string.Empty;
        IOCC.Set("EnableGpuPicking", true);
    }

    private void OnNodeExit() => Close();

    private void OnModeChange(int index)
    {
        PublishCmd(EvtSetMode, index);

        // 纯 UI：切入切片且三个开关都关时，默认打开 XY（Controller 内也会 EnsureDefaultSliceVisible）
        if (index == ModeSlice
            && m_showSliceXYToggle != null
            && m_showSliceXZToggle != null
            && m_showSliceYZToggle != null
            && !m_showSliceXYToggle.isOn
            && !m_showSliceXZToggle.isOn
            && !m_showSliceYZToggle.isOn)
        {
            m_showSliceXYToggle.SetIsOnWithoutNotify(true);
            PublishCmd(EvtSetShowSliceXY, true);
        }
    }

    private void OnSliceXYChanged(float value) => PublishCmd(EvtSetSlicePositionXY, value);

    private void OnSliceXZChanged(float value) => PublishCmd(EvtSetSlicePositionXZ, value);

    private void OnSliceYZChanged(float value) => PublishCmd(EvtSetSlicePositionYZ, value);

    private void OnIsoTemperatureChanged(float value) => PublishCmd(EvtSetIsoTemperature, value);

    private void PublishCmd(string eventName, bool value)
    {
        if (string.IsNullOrEmpty(m_managerID)) return;
        PublishWithID(eventName, m_managerID, value);
    }

    private void PublishCmd(string eventName, int value)
    {
        if (string.IsNullOrEmpty(m_managerID)) return;
        PublishWithID(eventName, m_managerID, value);
    }

    private void PublishCmd(string eventName, float value)
    {
        if (string.IsNullOrEmpty(m_managerID)) return;
        PublishWithID(eventName, m_managerID, value);
    }

    private static void BindSliderValueLabel(Slider slider, TMP_Text label, Func<float, string> formatter)
    {
        if (slider == null || label == null || formatter == null) return;

        void UpdateLabel(float value) => label.text = formatter(value);
        slider.onValueChanged.AddListener(UpdateLabel);
        UpdateLabel(slider.value);
    }

    private static string FormatTemperature(float value) => $"{value:F1}℃";

    private static string FormatNormalized(float value) => $"{value:P0}";
}
}
