using System.Collections;
using NaughtyAttributes;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback;
using NonsensicalKit.Simulation.WarehouseSimulation.Simulation;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>场景入口：运行仓库仿真（入库/出库等）并写入回放数据；场景可视由回放控制器单独驱动。</summary>
    public class WarehouseSimRunner : MonoBehaviour
    {
        [SerializeField, Label("仿真场景")]
        private WarehouseSimScenario m_scenario;

        [SerializeField, Label("网格配置")]
        [Tooltip("规则网格尺寸与排除区。")]
        private WarehouseGridConfig m_grid = new();

        [SerializeField, Label("回放事件源（可选）")]
        private SimPlaybackEventSource m_playbackEventSource;

        [SerializeField, Label("输出摘要到控制台")]
        private bool m_logSummaryToConsole = true;

        [SerializeField, Label("输出关键步骤日志")]
        private bool m_logKeySteps = true;

        [SerializeField, Label("输出详细流水日志")]
        private bool m_logVerboseSteps;

        [SerializeField, Label("记录回放与子任务")]
        [Tooltip("关闭后仅保留汇总统计，大批量仿真时可显著提速。")]
        private bool m_recordPlaybackAndSubTasks = true;

        [SerializeField, Label("收集墙钟性能剖析")]
        [Tooltip("开启后按阶段统计 CPU 墙钟耗时，用于定位仿真热点；结果写入 SimRunResult 并在控制台摘要输出。")]
        private bool m_collectWallClockProfile;

        [SerializeField, Label("导出目录（留空=StreamingAssets/SimulationExports）")]
        private string m_exportDirectory;

        private SimRunResult _lastResult;
        private Coroutine _runCoroutine;
        private Coroutine _exportCoroutine;
        private bool _isRunning;
        private bool _isExporting;

        public SimRunResult LastResult => _lastResult;
        public StackerWarehouseSimulator LastSimulator { get; private set; }
        public bool IsRunning => _isRunning;
        public bool IsExporting => _isExporting;

        [Button("运行仿真")]
        public void RunSimulation()
        {
            if (_isRunning)
            {
                Debug.LogWarning("[WarehouseSimulation] 上一次仿真仍在后台执行。", this);
                return;
            }

            if (m_scenario == null)
            {
                Debug.LogError("[WarehouseSimulation] 请指定 WarehouseSimScenario。", this);
                return;
            }

            if (_runCoroutine != null)
            {
                StopCoroutine(_runCoroutine);
            }

            _runCoroutine = StartCoroutine(RunSimulationCoroutine());
        }

        private IEnumerator RunSimulationCoroutine()
        {
            _isRunning = true;
            SimRunBackgroundRun.Outcome outcome = null;
            try
            {
                yield return SimRunBackgroundRun.Run(
                    this,
                    m_scenario,
                    m_grid,
                    BuildRunOptions(),
                    m_logKeySteps,
                    m_logVerboseSteps,
                    onComplete: o => outcome = o);

                if (!SimRunBackgroundRun.TryApplyOutcome(outcome, out var simulator, out var result))
                {
                    if (outcome?.Error != null)
                    {
                        Debug.LogError($"[WarehouseSimulation] 仿真失败：{outcome.Error.Message}", this);
                    }
                    else
                    {
                        Debug.LogError("[WarehouseSimulation] 仿真失败：未收到有效结果。", this);
                    }

                    LastSimulator = null;
                    _lastResult = null;
                    yield break;
                }

                LastSimulator = simulator;
                _lastResult = result;

                if (_lastResult.OccupancyConflicts != null && _lastResult.OccupancyConflicts.Count > 0)
                {
                    Debug.LogWarning(
                        $"[WarehouseSimulation] 节点占用自检 {_lastResult.OccupancyConflicts.Count} 处冲突，" +
                        "请导出报告后在调试 Markdown 中查看明细。",
                        this);
                }

                if (_lastResult.SubTaskTimelineIssues != null && _lastResult.SubTaskTimelineIssues.Count > 0)
                {
                    Debug.LogWarning(
                        $"[WarehouseSimulation] 子任务时间轴自检 {_lastResult.SubTaskTimelineIssues.Count} 处问题，" +
                        "请导出报告后在调试 Markdown 中查看明细。",
                        this);
                }

                if (m_logSummaryToConsole)
                {
                    WarehouseSimulationService.LogSummary(_lastResult);
                }

                if (m_playbackEventSource != null)
                {
                    var initialSlots = SlotAllocatorFactory.BuildInitialOccupiedSlots(m_scenario, m_grid);
                    m_playbackEventSource.AttachRunTimeline(
                        LastSimulator.LastPlaybackEvents,
                        LastSimulator.LastSubTasks,
                        initialSlots,
                        m_grid,
                        m_scenario.ResolvedHardwareBindings,
                        _lastResult.TotalSimSeconds);
                }

            }
            finally
            {
                _isRunning = false;
                _runCoroutine = null;
            }
        }

        [Button("导出 HTML 报告")]
        public void ExportLastRunResult()
        {
            if (_isExporting)
            {
                Debug.LogWarning("[WarehouseSimulation] 上一次报告导出仍在后台执行。", this);
                return;
            }

            if (LastSimulator == null || _lastResult == null)
            {
                Debug.LogError("[WarehouseSimulation] 导出中止：请先运行仿真。", this);
                return;
            }

            if (_exportCoroutine != null)
            {
                StopCoroutine(_exportCoroutine);
            }

            _exportCoroutine = StartCoroutine(ExportLastRunResultCoroutine());
        }

        private IEnumerator ExportLastRunResultCoroutine()
        {
            _isExporting = true;
            SimRunBackgroundExport.Outcome outcome = null;
            try
            {
                yield return SimRunBackgroundExport.Run(
                    this,
                    m_scenario,
                    LastSimulator,
                    _lastResult,
                    string.IsNullOrWhiteSpace(m_exportDirectory) ? null : m_exportDirectory,
                    onComplete: o => outcome = o);

                if (outcome?.Error != null)
                {
                    Debug.LogError($"[WarehouseSimulation] 导出失败：{outcome.Error.Message}", this);
                    yield break;
                }

                _lastResult.DebugReportPath = outcome.DebugReportPath;
                Debug.Log(
                    $"[WarehouseSimulation] 已导出仿真报告：{outcome.HtmlPath}\n" +
                    $"  调试信息报告：{outcome.DebugReportPath}",
                    this);
            }
            finally
            {
                _isExporting = false;
                _exportCoroutine = null;
            }
        }

        private SimRunOptions? BuildRunOptions() => new()
        {
            RecordPlaybackAndSubTasks = m_recordPlaybackAndSubTasks,
            CollectWallClockProfile = m_collectWallClockProfile,
        };

        [Button("打开报告目录")]
        public void OpenReportDirectory()
        {
            try
            {
                WarehouseSimReportDirectory.Open(
                    string.IsNullOrWhiteSpace(m_exportDirectory) ? null : m_exportDirectory);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[WarehouseSimulation] 打开报告目录失败：{ex.Message}", this);
            }
        }
    }
}
