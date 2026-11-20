using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TemperatureVisualization.Demo
{
    /// <summary>
    /// Demo 专用控制面板：可视化参数、传感器手动调温、切片开关。
    /// </summary>
    public class TemperatureDemoControlPanel : MonoBehaviour
    {
        [SerializeField] private TemperatureVisualizationController m_Controller;
        [SerializeField] private TMP_Dropdown m_ModeDropdown;
        [SerializeField] private Slider m_TempMinSlider;
        [SerializeField] private Slider m_TempMaxSlider;
        [SerializeField] private Slider m_OpacitySlider;
        [SerializeField] private TMP_Text m_TempMinValueText;
        [SerializeField] private TMP_Text m_TempMaxValueText;
        [SerializeField] private TMP_Text m_OpacityValueText;
        [SerializeField] private Slider m_SliceXYSlider;
        [SerializeField] private Slider m_SliceXZSlider;
        [SerializeField] private Slider m_SliceYZSlider;
        [SerializeField] private TMP_Text m_SliceXYValueText;
        [SerializeField] private TMP_Text m_SliceXZValueText;
        [SerializeField] private TMP_Text m_SliceYZValueText;
        [SerializeField] private Toggle m_ShowSliceXYToggle;
        [SerializeField] private Toggle m_ShowSliceXZToggle;
        [SerializeField] private Toggle m_ShowSliceYZToggle;
        [SerializeField] private Toggle m_SimulationToggle;
        [SerializeField] private TMP_Dropdown m_SensorDropdown;
        [SerializeField] private Slider m_SensorTemperatureSlider;
        [SerializeField] private Slider m_IsoTemperatureSlider;
        [SerializeField] private TMP_Text m_SensorTemperatureValueText;
        [SerializeField] private TMP_Text m_IsoTemperatureValueText;
        [SerializeField] private TMP_Dropdown m_ColorPresetDropdown;
        [SerializeField] private TMP_Text m_SensorListText;
        [SerializeField] private TMP_Text m_StatusText;
        [SerializeField] private float m_SensorListRefreshInterval = 0.5f;

        private int m_SelectedSensorIndex;
        private float m_RefreshTimer;
        private bool m_EventsBound;
        private bool m_UpdatingSensorUi;

        public void BindController(TemperatureVisualizationController controller)
        {
            m_Controller = controller;
            EnsureUiEventsBound();
            InitializeUiFromController();
        }

        private void Start()
        {
            if (m_Controller == null)
            {
                m_Controller = FindObjectOfType<TemperatureVisualizationController>();
            }

            EnsureUiEventsBound();
            InitializeUiFromController();
        }

        private void Update()
        {
            m_RefreshTimer += Time.deltaTime;
            if (m_RefreshTimer >= m_SensorListRefreshInterval)
            {
                m_RefreshTimer = 0f;
                RefreshSensorList();
                RefreshStatus();
            }
        }

        private void EnsureUiEventsBound()
        {
            if (m_EventsBound) return;
            BindUiEvents();
            m_EventsBound = true;
        }

        private void BindUiEvents()
        {
            m_ModeDropdown?.onValueChanged.AddListener(OnModeChanged);
            m_TempMinSlider?.onValueChanged.AddListener(OnTempMinChanged);
            m_TempMaxSlider?.onValueChanged.AddListener(OnTempMaxChanged);
            m_OpacitySlider?.onValueChanged.AddListener(OnOpacityChanged);
            m_SliceXYSlider?.onValueChanged.AddListener(OnSliceXYChanged);
            m_SliceXZSlider?.onValueChanged.AddListener(OnSliceXZChanged);
            m_SliceYZSlider?.onValueChanged.AddListener(OnSliceYZChanged);
            m_ShowSliceXYToggle?.onValueChanged.AddListener(v =>
            {
                if (m_Controller?.SliceRenderer == null) return;
                m_Controller.SliceRenderer.ShowSliceXY = v;
                m_Controller.MarkRenderDirty();
            });
            m_ShowSliceXZToggle?.onValueChanged.AddListener(v =>
            {
                if (m_Controller?.SliceRenderer == null) return;
                m_Controller.SliceRenderer.ShowSliceXZ = v;
                m_Controller.MarkRenderDirty();
            });
            m_ShowSliceYZToggle?.onValueChanged.AddListener(v =>
            {
                if (m_Controller?.SliceRenderer == null) return;
                m_Controller.SliceRenderer.ShowSliceYZ = v;
                m_Controller.MarkRenderDirty();
            });
            m_SimulationToggle?.onValueChanged.AddListener(OnSimulationToggleChanged);
            m_SensorDropdown?.onValueChanged.AddListener(OnSensorSelectionChanged);
            m_SensorTemperatureSlider?.onValueChanged.AddListener(OnSensorTemperatureChanged);
            m_IsoTemperatureSlider?.onValueChanged.AddListener(OnIsoTemperatureChanged);
            m_ColorPresetDropdown?.onValueChanged.AddListener(OnColorPresetChanged);

            BindSliderValueLabel(m_TempMinSlider, m_TempMinValueText, FormatTemperature);
            BindSliderValueLabel(m_TempMaxSlider, m_TempMaxValueText, FormatTemperature);
            BindSliderValueLabel(m_OpacitySlider, m_OpacityValueText, FormatOpacity);
            BindSliderValueLabel(m_SliceXYSlider, m_SliceXYValueText, FormatNormalized);
            BindSliderValueLabel(m_SliceXZSlider, m_SliceXZValueText, FormatNormalized);
            BindSliderValueLabel(m_SliceYZSlider, m_SliceYZValueText, FormatNormalized);
            BindSliderValueLabel(m_SensorTemperatureSlider, m_SensorTemperatureValueText, FormatTemperature);
            BindSliderValueLabel(m_IsoTemperatureSlider, m_IsoTemperatureValueText, FormatTemperature);
        }

        private static void BindSliderValueLabel(Slider slider, TMP_Text label, Func<float, string> formatter)
        {
            if (slider == null || label == null || formatter == null) return;

            void UpdateLabel(float value) => label.text = formatter(value);
            slider.onValueChanged.AddListener(UpdateLabel);
            UpdateLabel(slider.value);
        }

        private static string FormatTemperature(float value) => $"{value:F1}℃";

        private static string FormatOpacity(float value) => $"{value:P0}";

        private static string FormatNormalized(float value) => $"{value:P0}";

        private void InitializeUiFromController()
        {
            if (m_Controller == null) return;

            m_ModeDropdown?.SetValueWithoutNotify((int)m_Controller.Mode);
            m_TempMinSlider?.SetValueWithoutNotify(m_Controller.TempMin);
            m_TempMaxSlider?.SetValueWithoutNotify(m_Controller.TempMax);
            m_OpacitySlider?.SetValueWithoutNotify(m_Controller.Opacity);
            RefreshSliderValueLabels();

            if (m_Controller.SliceRenderer != null)
            {
                m_SliceXYSlider?.SetValueWithoutNotify(m_Controller.SliceRenderer.PositionXY);
                m_SliceXZSlider?.SetValueWithoutNotify(m_Controller.SliceRenderer.PositionXZ);
                m_SliceYZSlider?.SetValueWithoutNotify(m_Controller.SliceRenderer.PositionYZ);
                m_ShowSliceXYToggle?.SetIsOnWithoutNotify(m_Controller.SliceRenderer.ShowSliceXY);
                m_ShowSliceXZToggle?.SetIsOnWithoutNotify(m_Controller.SliceRenderer.ShowSliceXZ);
                m_ShowSliceYZToggle?.SetIsOnWithoutNotify(m_Controller.SliceRenderer.ShowSliceYZ);
            }

            if (m_Controller.SensorManager != null)
            {
                m_SimulationToggle?.SetIsOnWithoutNotify(m_Controller.SensorManager.EnableSimulation);
            }

            if (m_Controller.IsosurfaceRenderer != null)
            {
                m_IsoTemperatureSlider?.SetValueWithoutNotify(m_Controller.IsosurfaceRenderer.IsoTemperature);
            }

            RebuildSensorDropdown();
            RefreshSensorList();
            RefreshStatus();
        }

        private void RefreshSliderValueLabels()
        {
            if (m_TempMinSlider != null && m_TempMinValueText != null)
                m_TempMinValueText.text = FormatTemperature(m_TempMinSlider.value);
            if (m_TempMaxSlider != null && m_TempMaxValueText != null)
                m_TempMaxValueText.text = FormatTemperature(m_TempMaxSlider.value);
            if (m_OpacitySlider != null && m_OpacityValueText != null)
                m_OpacityValueText.text = FormatOpacity(m_OpacitySlider.value);
            if (m_SliceXYSlider != null && m_SliceXYValueText != null)
                m_SliceXYValueText.text = FormatNormalized(m_SliceXYSlider.value);
            if (m_SliceXZSlider != null && m_SliceXZValueText != null)
                m_SliceXZValueText.text = FormatNormalized(m_SliceXZSlider.value);
            if (m_SliceYZSlider != null && m_SliceYZValueText != null)
                m_SliceYZValueText.text = FormatNormalized(m_SliceYZSlider.value);
            if (m_SensorTemperatureSlider != null && m_SensorTemperatureValueText != null)
                m_SensorTemperatureValueText.text = FormatTemperature(m_SensorTemperatureSlider.value);
            if (m_IsoTemperatureSlider != null && m_IsoTemperatureValueText != null)
                m_IsoTemperatureValueText.text = FormatTemperature(m_IsoTemperatureSlider.value);
        }

        private void OnModeChanged(int index)
        {
            if (m_Controller == null) return;
            m_Controller.Mode = (VisualizationMode)index;

            if (m_Controller.SliceRenderer != null && (VisualizationMode)index == VisualizationMode.Slice)
            {
                if (!m_Controller.SliceRenderer.ShowSliceXY &&
                    !m_Controller.SliceRenderer.ShowSliceXZ &&
                    !m_Controller.SliceRenderer.ShowSliceYZ)
                {
                    m_Controller.SliceRenderer.ShowSliceXY = true;
                    m_ShowSliceXYToggle?.SetIsOnWithoutNotify(true);
                }
            }

            m_Controller.MarkRenderDirty();
        }

        private void OnTempMinChanged(float value)
        {
            if (m_Controller == null) return;
            m_Controller.TempMin = value;
            if (m_Controller.TempMax < value)
            {
                m_Controller.TempMax = value + 0.1f;
                m_TempMaxSlider?.SetValueWithoutNotify(m_Controller.TempMax);
                if (m_TempMaxValueText != null)
                    m_TempMaxValueText.text = FormatTemperature(m_Controller.TempMax);
            }

            m_Controller.MarkRenderDirty();
        }

        private void OnTempMaxChanged(float value)
        {
            if (m_Controller == null) return;
            m_Controller.TempMax = value;
            if (m_Controller.TempMin > value)
            {
                m_Controller.TempMin = value - 0.1f;
                m_TempMinSlider?.SetValueWithoutNotify(m_Controller.TempMin);
                if (m_TempMinValueText != null)
                    m_TempMinValueText.text = FormatTemperature(m_Controller.TempMin);
            }

            m_Controller.MarkRenderDirty();
        }

        private void OnColorPresetChanged(int index)
        {
            m_Controller?.ApplyColorPreset(index);
        }

        private void OnOpacityChanged(float value)
        {
            if (m_Controller != null) m_Controller.Opacity = value;
        }

        private void OnSliceXYChanged(float value)
        {
            if (m_Controller?.SliceRenderer != null) m_Controller.SliceRenderer.PositionXY = value;
        }

        private void OnSliceXZChanged(float value)
        {
            if (m_Controller?.SliceRenderer != null) m_Controller.SliceRenderer.PositionXZ = value;
        }

        private void OnSliceYZChanged(float value)
        {
            if (m_Controller?.SliceRenderer != null) m_Controller.SliceRenderer.PositionYZ = value;
        }

        private void OnSimulationToggleChanged(bool enabled)
        {
            if (m_Controller?.SensorManager == null) return;
            m_Controller.SensorManager.EnableSimulation = enabled;
        }

        private void OnSensorSelectionChanged(int index)
        {
            m_SelectedSensorIndex = index;
            UpdateSelectedSensorSlider();
        }

        private void OnSensorTemperatureChanged(float value)
        {
            if (m_UpdatingSensorUi || m_Controller?.SensorManager == null) return;

            var sensors = m_Controller.SensorManager.Sensors;
            if (m_SelectedSensorIndex < 0 || m_SelectedSensorIndex >= sensors.Count) return;

            TemperatureSensor sensor = sensors[m_SelectedSensorIndex];
            m_Controller.SensorManager.SetTemperatureManual(sensor.Id, value);
            m_Controller.RebuildField();
        }

        private void OnIsoTemperatureChanged(float value)
        {
            if (m_Controller?.IsosurfaceRenderer == null) return;
            m_Controller.IsosurfaceRenderer.IsoTemperature = value;
            m_Controller.RebuildIsosurface();
        }

        private void RebuildSensorDropdown()
        {
            if (m_SensorDropdown == null || m_Controller?.SensorManager == null) return;

            m_SensorDropdown.ClearOptions();
            var sensors = m_Controller.SensorManager.Sensors;
            var options = new System.Collections.Generic.List<string>();
            for (int i = 0; i < sensors.Count; i++)
            {
                options.Add($"传感器 {i + 1}");
            }

            m_SensorDropdown.AddOptions(options);
            m_SelectedSensorIndex = Mathf.Clamp(m_SelectedSensorIndex, 0, Mathf.Max(0, sensors.Count - 1));
            m_SensorDropdown.SetValueWithoutNotify(m_SelectedSensorIndex);
            UpdateSelectedSensorSlider();
        }

        private void UpdateSelectedSensorSlider()
        {
            if (m_SensorTemperatureSlider == null || m_Controller?.SensorManager == null) return;

            var sensors = m_Controller.SensorManager.Sensors;
            if (sensors.Count == 0) return;

            m_UpdatingSensorUi = true;
            m_SensorTemperatureSlider.SetValueWithoutNotify(sensors[m_SelectedSensorIndex].Temperature);
            m_UpdatingSensorUi = false;
            if (m_SensorTemperatureValueText != null)
            {
                m_SensorTemperatureValueText.text = FormatTemperature(m_SensorTemperatureSlider.value);
            }
        }

        private void RefreshSensorList()
        {
            if (m_SensorListText == null || m_Controller?.SensorManager == null) return;

            var sensors = m_Controller.SensorManager.Sensors;
            if (m_SensorDropdown != null && m_SensorDropdown.options.Count != sensors.Count)
            {
                RebuildSensorDropdown();
            }

            var sb = new StringBuilder();
            sb.AppendLine("传感器列表");
            for (int i = 0; i < sensors.Count; i++)
            {
                TemperatureSensor sensor = sensors[i];
                string manualTag = m_Controller.SensorManager.IsManualOverride(sensor.Id) ? " [手动]" : string.Empty;
                sb.AppendLine($"{i + 1}. {sensor.Temperature:F1}℃{manualTag}");
            }

            m_SensorListText.text = sb.ToString();
            UpdateSelectedSensorSlider();
        }

        private void RefreshStatus()
        {
            if (m_StatusText == null || m_Controller?.Interpolator == null) return;
            m_StatusText.text = m_Controller.Interpolator.IsComputing
                ? "状态：温度场重建中..."
                : $"状态：运行中 | 模式 {m_Controller.Mode} | 分辨率 {m_Controller.Interpolator.Resolution}³";
        }
    }
}
