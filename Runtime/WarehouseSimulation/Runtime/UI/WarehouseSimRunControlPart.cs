using NaughtyAttributes;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using NonsensicalKit.Simulation.WarehouseSimulation.Runtime.Runner;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime.UI
{
    /// <summary>
    /// 绑定 <see cref="WarehouseSimRunner"/>，提供运行仿真、导出报告与打开目录等 UI 操作。
    /// </summary>
    public sealed class WarehouseSimRunControlPart : MonoBehaviour
    {
        [SerializeField, Label("仿真运行器")]
        private WarehouseSimRunner m_runner;

        [Header("可为空的配置项")]
        [SerializeField] private Button m_btn_run;
        [SerializeField] private Button m_btn_export;
        [SerializeField] private Button m_btn_openReportDirectory;
        [SerializeField] private GameObject m_runningMask;
        [SerializeField] private GameObject m_exportingMask;
        [SerializeField] private TextMeshProUGUI m_statusText;

        [SerializeField] private UnityEvent<WarehouseSimRunState> m_runStateChanged;

        private readonly WarehouseSimRunState _state = new();
        private bool _lastIsRunning;
        private bool _lastIsExporting;
        private bool _lastHasResult;

        public UnityEvent<WarehouseSimRunState> OnRunStateChanged => m_runStateChanged;

        private void Awake()
        {
            m_btn_run?.onClick.AddListener(OnRunClicked);
            m_btn_export?.onClick.AddListener(OnExportClicked);
            m_btn_openReportDirectory?.onClick.AddListener(OnOpenReportDirectoryClicked);
        }

        private void OnDestroy()
        {
            m_btn_run?.onClick.RemoveListener(OnRunClicked);
            m_btn_export?.onClick.RemoveListener(OnExportClicked);
            m_btn_openReportDirectory?.onClick.RemoveListener(OnOpenReportDirectoryClicked);
        }

        private void Update()
        {
            if (m_runner == null)
            {
                return;
            }

            SyncState();
        }

        private void OnRunClicked()
        {
            m_runner?.RunSimulation();
        }

        private void OnExportClicked()
        {
            m_runner?.ExportLastRunResult();
        }

        private void OnOpenReportDirectoryClicked()
        {
            m_runner?.OpenReportDirectory();
        }

        private void SyncState()
        {
            _state.IsRunning = m_runner.IsRunning;
            _state.IsExporting = m_runner.IsExporting;
            _state.HasResult = m_runner.LastResult != null;

            m_runningMask?.SetActive(_state.IsRunning);
            m_exportingMask?.SetActive(_state.IsExporting);

            if (m_btn_run != null)
            {
                m_btn_run.interactable = !_state.IsRunning;
            }

            if (m_btn_export != null)
            {
                m_btn_export.interactable = _state.HasResult && !_state.IsExporting && !_state.IsRunning;
            }

            if (m_statusText != null)
            {
                if (_state.IsRunning)
                {
                    m_statusText.text = "仿真运行中…";
                }
                else if (_state.IsExporting)
                {
                    m_statusText.text = "报告导出中…";
                }
                else if (_state.HasResult)
                {
                    m_statusText.text = "仿真已完成，可回放或导出报告。";
                }
                else
                {
                    m_statusText.text = "尚未运行仿真。";
                }
            }

            if (_lastIsRunning == _state.IsRunning
                && _lastIsExporting == _state.IsExporting
                && _lastHasResult == _state.HasResult)
            {
                return;
            }

            _lastIsRunning = _state.IsRunning;
            _lastIsExporting = _state.IsExporting;
            _lastHasResult = _state.HasResult;
            m_runStateChanged?.Invoke(_state);
        }
    }
}
