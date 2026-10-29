using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback;
using NonsensicalKit.Simulation.WarehouseSimulation.Simulation;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
/// <summary>
/// 仓储仿真场景 UI：上方「仿真」窗口（配置/运行/结果/导出），下方「回放」窗口（信息/加载/播放）。
/// </summary>
[DisallowMultipleComponent]
public sealed class WarehouseSimUiController : MonoBehaviour
{
    private const float SpeedMin = 1f;
    private const float SpeedMax = 120f;
    private const string PrefSimExpanded = "WarehouseSim.UiSimExpanded";
    private const string PrefReplayExpanded = "WarehouseSim.UiReplayExpanded";

    [SerializeField] private WarehouseSimRunner m_runner;
    [SerializeField] private WarehouseSimPlaybackController m_playback;
    [SerializeField] private UIDocument m_document;
    [SerializeField] private Font m_font;

    private readonly List<(Button button, Action action)> _clicks = new();

    private Label _simStatusLabel;
    private Label _replayStatusLabel;
    private Label _timeLabel;
    private Label _speedLabel;
    private Label _summaryLabel;
    private Label _resultSourceLabel;
    private Label _loadedPathLabel;
    private Label _replayInfoLabel;
    private VisualElement _replayInfoCard;
    private TextField _presetNameField;
    private Button _runButton;
    private Button _playButton;
    private Button _stopButton;
    private Button _exportButton;
    private Button _loadReplayButton;
    private ProgressBar _runProgress;
    private Slider _timelineSlider;
    private Slider _speedSlider;
    private Button[] _speedPresets = Array.Empty<Button>();
    private ScrollView _simScroll;
    private ScrollView _replayScroll;
    private VisualElement _root;
    private VisualElement _simDock;
    private VisualElement _replayDock;
    private VisualElement _simPanels;
    private VisualElement _replayPanels;
    private VisualElement _simWindow;
    private VisualElement _replayWindow;
    private Button _simEdge;
    private Button _replayEdge;
    private Button _simCollapse;
    private Button _replayCollapse;
    private VisualElement _configPanel;
    private Button _configToggle;
    private WarehouseSimTaskConfigView _taskConfig;
    private VisualElement _playbackCard;
    private VisualElement _resultCard;
    private VisualElement _emptyCard;
    private VisualElement _replayEmptyCard;
    private int _visMask = -1;
    private string _runButtonText = string.Empty;
    private string _simStatusClass = string.Empty;
    private string _replayStatusClass = string.Empty;

    private bool _started;
    private bool _simExpanded = true;
    private bool _replayExpanded = true;
    private float _nextUiRefreshAt;
    private const float UiRefreshInterval = 0.1f;
    private bool _configOpen;
    private bool _fittingPanel;
    private bool _fitScheduled;
    private bool _bound;
    private bool _syncingUi;
    private bool _wasRunning;
    private bool _failed;
    private bool _loadedForCurrentResult;
    private string _loadedReplayPath;
    private string _replayMetaConfig = "—";
    private string _replayMetaSource = "—";
    private int _replayMetaCompleted = -1;
    private int _replayMetaTarget = -1;
    private double _replayMetaSeconds = double.NaN;
    private string _replayInfoText = string.Empty;
    private bool _uiBusyPoll;
    private float _runStartedAt = -1f;
    private string _summaryText = string.Empty;
    private string _simStatusText = string.Empty;
    private string _replayStatusText = string.Empty;
    private string _timeText = string.Empty;
    private string _speedText = string.Empty;
    private string _playButtonText = string.Empty;
    private string _progressTitle = string.Empty;
    private bool _progressVisible;
    private float _progressValue = float.NaN;
    private float _timelineValue = float.NaN;
    private float _speedValue = float.NaN;
    private bool _runEnabled = true;
    private bool _exportEnabled;
    private bool _playEnabled;
    private bool _stopEnabled;
    private int _speedChip = int.MinValue;

    private void OnEnable()
    {
        if (_started)
        {
            Bind();
        }
    }

    private void Start()
    {
        _started = true;
        Bind();
    }

    private void OnDisable()
    {
        Unbind();
    }

    private void Update()
    {
        if (!_bound)
        {
            return;
        }

        // Only poll UI while sim is calculating/exporting or playback is playing.
        // Idle (even with panel open) does no per-frame UI work — that was dirtying UITK.
        var busy = (m_runner != null && (m_runner.IsRunning || m_runner.IsExporting))
                   || (m_playback != null && m_playback.IsPlaying);
        if (!busy)
        {
            if (_uiBusyPoll)
            {
                _uiBusyPoll = false;
                ApplyState(forceVisual: true);
            }

            return;
        }

        _uiBusyPoll = true;
        ApplyState(forceVisual: false);
    }

    private void Bind()
    {
        if (_bound)
        {
            return;
        }

        if (m_document == null)
        {
            m_document = GetComponent<UIDocument>();
        }

        if (m_playback == null)
        {
            // 优先在同一个仿真根物体下找，避免多场景叠加时找错。
            m_playback = m_runner != null
                ? m_runner.transform.root.GetComponentInChildren<WarehouseSimPlaybackController>(true)
                : null;
            if (m_playback == null)
            {
                m_playback = FindFirstObjectByType<WarehouseSimPlaybackController>();
            }
        }

        if (m_runner == null)
        {
            m_runner = FindFirstObjectByType<WarehouseSimRunner>();
        }

        var root = m_document != null ? m_document.rootVisualElement : null;
        if (root == null)
        {
            return;
        }

        if (m_font != null)
        {
            // Apply once on root (children inherit). Never set -unity-font url in USS:
            // that rebuilds dynamic font atlases every frame and can drop Play to ~3 FPS.
            root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(m_font));
        }

        _simStatusLabel = root.Q<Label>("sim-status");
        _replayStatusLabel = root.Q<Label>("replay-status");
        _timeLabel = root.Q<Label>("time");
        _speedLabel = root.Q<Label>("speed-label");
        _summaryLabel = root.Q<Label>("summary");
        _resultSourceLabel = root.Q<Label>("result-source");
        _loadedPathLabel = root.Q<Label>("loaded-path");
        _replayInfoLabel = root.Q<Label>("replay-info");
        _replayInfoCard = root.Q("replay-info-card");
        _presetNameField = root.Q<TextField>("preset-name");
        _runProgress = root.Q<ProgressBar>("run-progress");
        _timelineSlider = root.Q<Slider>("timeline");
        _speedSlider = root.Q<Slider>("speed");
        _runButton = root.Q<Button>("run");
        _playButton = root.Q<Button>("play");
        _stopButton = root.Q<Button>("stop");
        _exportButton = root.Q<Button>("export");
        _loadReplayButton = root.Q<Button>("load-replay");

        BindClick(_runButton, OnRunClicked);
        BindClick(_playButton, OnPlayClicked);
        BindClick(_stopButton, OnStopClicked);
        BindClick(_exportButton, OnExportClicked);
        BindClick(_loadReplayButton, OnLoadReplayClicked);
        BindClick(root.Q<Button>("open-report"), OnOpenReportDirectoryClicked);
        _configToggle = root.Q<Button>("toggle-config");
        BindClick(_configToggle, OnConfigToggleClicked);
        BindClick(root.Q<Button>("close-config"), () => SetConfigOpen(false));
        _simEdge = root.Q<Button>("sim-edge");
        _replayEdge = root.Q<Button>("replay-edge");
        _simCollapse = root.Q<Button>("sim-collapse");
        _replayCollapse = root.Q<Button>("replay-collapse");
        BindClick(_simEdge, () => SetSimExpanded(!_simExpanded));
        BindClick(_replayEdge, () => SetReplayExpanded(!_replayExpanded));
        BindClick(_simCollapse, () => SetSimExpanded(false));
        BindClick(_replayCollapse, () => SetReplayExpanded(false));

        _speedPresets = new[]
        {
            root.Q<Button>("speed-1"),
            root.Q<Button>("speed-10"),
            root.Q<Button>("speed-60"),
            root.Q<Button>("speed-120"),
        };
        BindSpeedPreset(_speedPresets[0], 1f);
        BindSpeedPreset(_speedPresets[1], 10f);
        BindSpeedPreset(_speedPresets[2], 60f);
        BindSpeedPreset(_speedPresets[3], 120f);

        if (_timelineSlider != null)
        {
            _timelineSlider.lowValue = 0f;
            _timelineSlider.highValue = 1f;
            _timelineSlider.RegisterValueChangedCallback(OnTimelineChanged);
        }

        if (_speedSlider != null)
        {
            _speedSlider.lowValue = SpeedMin;
            _speedSlider.highValue = SpeedMax;
            _speedSlider.RegisterValueChangedCallback(OnSpeedChanged);
        }

        _root = root;
        _simDock = root.Q("sim-dock");
        _replayDock = root.Q("replay-dock");
        _simPanels = root.Q("sim-panels");
        _replayPanels = root.Q("replay-panels");
        _simWindow = root.Q("sim-window");
        _replayWindow = root.Q("replay-window");
        _configPanel = root.Q("config-panel");
        _simScroll = root.Q<ScrollView>("sim-scroll");
        _replayScroll = root.Q<ScrollView>("replay-scroll");
        var configScroll = root.Q<ScrollView>("config-scroll");
        ConfigureScroll(_simScroll);
        ConfigureScroll(_replayScroll);
        ConfigureScroll(configScroll);
        // GeometryChanged->FitPanel disabled (layout thrash).
        _simExpanded = PlayerPrefs.GetInt(PrefSimExpanded, 1) != 0;
        _replayExpanded = PlayerPrefs.GetInt(PrefReplayExpanded, 1) != 0;
        ApplySimExpanded();
        ApplyReplayExpanded();
        SetConfigOpen(false);

        _playbackCard = root.Q("playback-card");
        _resultCard = root.Q("result-card");
        _emptyCard = root.Q("empty-card");
        _replayEmptyCard = root.Q("replay-empty-card");

        _taskConfig = new WarehouseSimTaskConfigView();
        _taskConfig.Bind(root, m_runner);

        _bound = true;
        ApplyState(forceVisual: true);
    }

    private void Unbind()
    {
        if (!_bound)
        {
            return;
        }

        foreach (var (button, action) in _clicks)
        {
            if (button != null)
            {
                button.clicked -= action;
            }
        }

        _clicks.Clear();
        _timelineSlider?.UnregisterValueChangedCallback(OnTimelineChanged);
        _speedSlider?.UnregisterValueChangedCallback(OnSpeedChanged);
        _fitScheduled = false;
        _taskConfig?.Unbind();
        _taskConfig = null;
        _simScroll = null;
        _replayScroll = null;
        _playbackCard = null;
        _resultCard = null;
        _emptyCard = null;
        _replayEmptyCard = null;
        _visMask = -1;
        _runButtonText = string.Empty;
        _simStatusClass = string.Empty;
        _replayStatusClass = string.Empty;
        _configPanel = null;
        _configToggle = null;
        _simCollapse = null;
        _replayCollapse = null;
        _simEdge = null;
        _replayEdge = null;
        _simDock = null;
        _replayDock = null;
        _simPanels = null;
        _replayPanels = null;
        _simWindow = null;
        _replayWindow = null;
        _root = null;

        _summaryText = string.Empty;
        _simStatusText = string.Empty;
        _replayStatusText = string.Empty;
        _replayInfoText = string.Empty;
        ClearReplayMeta();
        _timeText = string.Empty;
        _speedText = string.Empty;
        _playButtonText = string.Empty;
        _progressTitle = string.Empty;
        _progressVisible = false;
        _progressValue = float.NaN;
        _timelineValue = float.NaN;
        _speedValue = float.NaN;
        _runEnabled = true;
        _exportEnabled = false;
        _playEnabled = false;
        _stopEnabled = false;
        _speedChip = int.MinValue;
        _simStatusLabel = null;
        _replayStatusLabel = null;
        _timeLabel = null;
        _speedLabel = null;
        _summaryLabel = null;
        _resultSourceLabel = null;
        _loadedPathLabel = null;
        _replayInfoLabel = null;
        _replayInfoCard = null;
        _presetNameField = null;
        _runButton = null;
        _playButton = null;
        _stopButton = null;
        _exportButton = null;
        _loadReplayButton = null;
        _runProgress = null;
        _timelineSlider = null;
        _speedSlider = null;
        _speedPresets = Array.Empty<Button>();
        _uiBusyPoll = false;
        _bound = false;
    }

    private void BindClick(Button button, Action action)
    {
        if (button == null)
        {
            return;
        }

        button.clicked += action;
        _clicks.Add((button, action));
    }

    private void BindSpeedPreset(Button button, float speed)
    {
        if (button == null)
        {
            return;
        }

        button.userData = speed;
        BindClick(button, () => SetSpeed(speed));
    }

    private void OnRunClicked()
    {
        _taskConfig?.Persist();
        m_runner?.RunSimulation();
        _uiBusyPoll = true;
        ApplyState(forceVisual: true);
    }

    private void OnPlayClicked()
    {
        if (m_playback == null)
        {
            return;
        }

        if (m_playback.IsPlaying)
        {
            m_playback.Pause();
        }
        else
        {
            m_playback.Play();
        }

        ApplyState(forceVisual: true);
    }

    private void OnStopClicked()
    {
        m_playback?.Stop();
        ApplyState(forceVisual: true);
    }

    private void OnExportClicked()
    {
        m_runner?.ExportLastRunResult();
        _uiBusyPoll = true;
        ApplyState(forceVisual: true);
    }

    private void OnLoadReplayClicked()
    {
        if (m_runner == null)
        {
            Debug.LogError("[WarehouseSimUi] 未找到仿真 Runner，无法加载回放。", this);
            return;
        }

        if (m_runner.IsRunning || m_runner.IsExporting)
        {
            Debug.LogWarning("[WarehouseSimUi] 仿真或导出进行中，请稍后再加载回放。", this);
            return;
        }

        var path = PickReplayHtmlPath();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        m_playback?.Stop();
        if (!m_runner.TryLoadReplayHtml(path, out var error, out var payload))
        {
            Debug.LogError($"[WarehouseSimUi] 加载回放失败：{error}", this);
            _replayStatusText = string.Empty;
            SetLabelText(_replayStatusLabel, $"加载失败：{error}", ref _replayStatusText);
            SetReplayStatusClass("is-error");
            return;
        }

        _loadedReplayPath = path;
        _failed = false;
        _loadedForCurrentResult = false;
        ApplyReplayMetaFromPayload(payload, path);
        if (m_playback != null)
        {
            m_playback.LoadFromEventSource();
            _loadedForCurrentResult = true;
        }

        SetReplayExpanded(true);
        ApplyState(forceVisual: true);
    }

    private static string PickReplayHtmlPath()
    {
        var startDir = SimRunExporter.DefaultExportDirectory;
        try
        {
            if (!Directory.Exists(startDir))
            {
                startDir = Application.dataPath;
            }
        }
        catch
        {
            startDir = Application.dataPath;
        }

#if UNITY_EDITOR
        return UnityEditor.EditorUtility.OpenFilePanel("加载可回放报告", startDir, "html");
#elif UNITY_STANDALONE_WIN
        return WinOpenHtmlFileDialog(startDir);
#elif UNITY_WEBGL && !UNITY_EDITOR
        Debug.LogWarning("[WarehouseSimUi] WebGL 暂不支持从本地选择回放 HTML，请在 PC 编辑器或 Windows 包体中加载。");
        return null;
#else
        Debug.LogWarning("[WarehouseSimUi] 当前平台未实现文件选择对话框。");
        return null;
#endif
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    private static string WinOpenHtmlFileDialog(string startDir)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments =
                    "-NoProfile -STA -Command \"" +
                    "Add-Type -AssemblyName System.Windows.Forms; " +
                    "$d = New-Object System.Windows.Forms.OpenFileDialog; " +
                    "$d.Filter = '可回放报告 (*.html)|*.html|所有文件 (*.*)|*.*'; " +
                    "$d.Title = '加载可回放报告'; " +
                    "$d.InitialDirectory = '" + startDir.Replace("'", "''") + "'; " +
                    "if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { $d.FileName }\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(120000);
            return string.IsNullOrWhiteSpace(output) ? null : output.Split('\n')[^1].Trim();
        }
        catch (Exception ex)
        {
            Debug.LogError("[WarehouseSimUi] 打开文件对话框失败：" + ex.Message);
            return null;
        }
    }
#endif

    private void OnOpenReportDirectoryClicked()
    {
        m_runner?.OpenReportDirectory();
    }

    private void OnTimelineChanged(ChangeEvent<float> evt)
    {
        if (_syncingUi || m_playback == null)
        {
            return;
        }

        m_playback.SeekNormalized(evt.newValue);
        ApplyState(forceVisual: true);
    }

    private void OnSpeedChanged(ChangeEvent<float> evt)
    {
        if (_syncingUi)
        {
            return;
        }

        SetSpeed(evt.newValue);
    }

    private void SetSpeed(float speed)
    {
        if (m_playback == null)
        {
            return;
        }

        m_playback.SetPlaybackSpeed(Mathf.Clamp(speed, SpeedMin, SpeedMax));
        ApplyState(forceVisual: true);
    }

    private void ApplyState(bool forceVisual)
    {
        var running = m_runner != null && m_runner.IsRunning;
        var exporting = m_runner != null && m_runner.IsExporting;
        var result = m_runner != null ? m_runner.LastResult : null;
        var hasResult = result != null;
        var hasTimeline = m_playback != null && m_playback.SubTasks != null && m_playback.SubTasks.Count > 0;
        var playbackBusy = running || exporting;

        if (running && !_wasRunning)
        {
            _failed = false;
            _loadedForCurrentResult = false;
            _loadedReplayPath = null;
            ClearReplayMeta();
            _runStartedAt = Time.realtimeSinceStartup;
            m_playback?.Stop();
        }
        else if (_wasRunning && !running && !hasResult)
        {
            _failed = true;
        }

        if (!running && hasResult && !_loadedForCurrentResult && m_playback != null)
        {
            m_playback.LoadFromEventSource();
            _loadedForCurrentResult = true;
            hasTimeline = m_playback.SubTasks != null && m_playback.SubTasks.Count > 0;
            if (hasTimeline && string.IsNullOrEmpty(_loadedReplayPath))
            {
                ApplyReplayMetaFromResult(result);
            }
        }

        var runEdge = running != _wasRunning;
        _wasRunning = running;

        var now = Time.unscaledTime;
        var refreshVisual = forceVisual || runEdge || now >= _nextUiRefreshAt;
        if (!refreshVisual)
        {
            return;
        }

        _nextUiRefreshAt = now + UiRefreshInterval;

        var hasScenario = m_runner != null && m_runner.Scenario != null;
        var playing = m_playback != null && m_playback.IsPlaying;
        var atStart = m_playback == null || m_playback.NormalizedTime <= 0.0001f;

        // 显示规则：仿真窗管结果，回放窗管播放；没数据就藏控件。
        var fromFile = !string.IsNullOrEmpty(_loadedReplayPath)
                       && (m_runner == null || m_runner.LastSimulator == null);
        var showPlayback = hasTimeline && !playbackBusy;
        var showResult = hasResult && !running;
        var showEmpty = !hasResult && !running;
        var showReplayEmpty = !hasTimeline && !playbackBusy;
        var mask = (showPlayback ? 1 : 0) | (showResult ? 2 : 0) | (showEmpty ? 4 : 0) | (showReplayEmpty ? 8 : 0)
                   | (fromFile ? 16 : 0);
        if (mask != _visMask)
        {
            _visMask = mask;
            SetVisible(_playbackCard, showPlayback);
            SetVisible(_resultCard, showResult);
            SetVisible(_emptyCard, showEmpty);
            SetVisible(_replayEmptyCard, showReplayEmpty);
        }

        if (running && _configOpen)
        {
            SetConfigOpen(false);
        }

        SetButtonEnabled(_runButton, !playbackBusy && hasScenario, ref _runEnabled);
        SetLabelText(
            _runButton,
            running ? "计算中…" : !hasScenario ? "未指定配置档" : hasResult && !fromFile ? "重新运行仿真" : "运行仿真",
            ref _runButtonText);
        var canExport = hasResult && !playbackBusy && m_runner != null && m_runner.LastSimulator != null;
        SetButtonEnabled(_exportButton, canExport, ref _exportEnabled);
        if (_loadReplayButton != null)
        {
            _loadReplayButton.SetEnabled(!playbackBusy);
        }
        SetButtonEnabled(_playButton, showPlayback, ref _playEnabled);
        // “回到开头”只在播放中或已离开起点时可点。
        SetButtonEnabled(_stopButton, showPlayback && (playing || !atStart), ref _stopEnabled);
        if (_playButton != null)
        {
            SetLabelText(_playButton, playing ? "暂停" : atStart ? "播放" : "继续", ref _playButtonText);
        }

        ApplyRunProgress(running, exporting);
        ApplyTimeline(hasTimeline, playbackBusy);
        ApplySpeed();
        ApplySummary(result);
        ApplyResultSource(fromFile, hasResult);
        ApplyLoadedPath();
        // 跑完仿真但 meta 尚未写入时补一次（例如刚进场景已有结果）。
        if (hasTimeline && !fromFile && string.IsNullOrEmpty(_loadedReplayPath)
            && (_replayMetaCompleted < 0 || double.IsNaN(_replayMetaSeconds)))
        {
            ApplyReplayMetaFromResult(result);
        }
        ApplyReplayInfo(hasTimeline || fromFile || (hasResult && !running));
        ApplySimStatus(running, exporting, hasResult, fromFile);
        ApplyReplayStatus(running, exporting, hasTimeline, fromFile, playing);
        SetSimStatusClass(running || exporting ? "is-busy" : _failed && !hasResult ? "is-error" : fromFile ? "is-loaded" : string.Empty);
        SetReplayStatusClass(running || exporting ? "is-busy" : fromFile && hasTimeline ? "is-loaded" : string.Empty);
        _configToggle?.SetEnabled(!running);
        _taskConfig?.SetInteractable(!playbackBusy);
    }

    private static void SetVisible(VisualElement element, bool visible)
    {
        element?.EnableInClassList("is-hidden", !visible);
    }

    private void SetSimStatusClass(string cls)
    {
        ApplyStatusClass(_simStatusLabel, cls, ref _simStatusClass);
    }

    private void SetReplayStatusClass(string cls)
    {
        ApplyStatusClass(_replayStatusLabel, cls, ref _replayStatusClass);
    }

    private static void ApplyStatusClass(Label label, string cls, ref string cached)
    {
        if (label == null || cached == cls)
        {
            return;
        }

        if (!string.IsNullOrEmpty(cached))
        {
            label.RemoveFromClassList(cached);
        }

        if (!string.IsNullOrEmpty(cls))
        {
            label.AddToClassList(cls);
        }

        cached = cls;
    }

    private void OnPanelGeometryChanged(GeometryChangedEvent _)
    {
        // no-op
    }

    private void OnConfigToggleClicked()
    {
        SetConfigOpen(!_configOpen);
    }

    private void SetConfigOpen(bool open)
    {
        _configOpen = open;
        _configPanel?.EnableInClassList("is-open", open);
        _configToggle?.EnableInClassList("is-on", open);
        ScheduleFitPanel();
    }


    private void SetSimExpanded(bool expanded)
    {
        if (_simExpanded == expanded)
        {
            ApplySimExpanded();
            return;
        }

        _simExpanded = expanded;
        PlayerPrefs.SetInt(PrefSimExpanded, expanded ? 1 : 0);
        PlayerPrefs.Save();
        if (!expanded)
        {
            SetConfigOpen(false);
        }

        ApplySimExpanded();
    }

    private void SetReplayExpanded(bool expanded)
    {
        if (_replayExpanded == expanded)
        {
            ApplyReplayExpanded();
            return;
        }

        _replayExpanded = expanded;
        PlayerPrefs.SetInt(PrefReplayExpanded, expanded ? 1 : 0);
        PlayerPrefs.Save();
        ApplyReplayExpanded();
    }

    private void ApplySimExpanded()
    {
        _simPanels?.EnableInClassList("is-open", _simExpanded);
        _simEdge?.EnableInClassList("is-open", _simExpanded);
        if (_simEdge != null)
        {
            _simEdge.text = _simExpanded ? "◀" : "仿真";
            _simEdge.tooltip = _simExpanded ? "收起仿真窗口" : "展开仿真窗口";
        }

        ApplyDockLayoutClasses();
        ClearManualHeights();
    }

    private void ApplyReplayExpanded()
    {
        _replayPanels?.EnableInClassList("is-open", _replayExpanded);
        _replayEdge?.EnableInClassList("is-open", _replayExpanded);
        if (_replayEdge != null)
        {
            _replayEdge.text = _replayExpanded ? "◀" : "回放";
            _replayEdge.tooltip = _replayExpanded ? "收起回放窗口" : "展开回放窗口";
        }

        ApplyDockLayoutClasses();
        ClearManualHeights();
    }

    private void ApplyDockLayoutClasses()
    {
        // 展开时各占约一半；收起缩成侧边条；只剩一个展开时可占满。
        _simDock?.EnableInClassList("is-collapsed", !_simExpanded);
        _replayDock?.EnableInClassList("is-collapsed", !_replayExpanded);
        _simDock?.EnableInClassList("is-solo", _simExpanded && !_replayExpanded);
        _replayDock?.EnableInClassList("is-solo", _replayExpanded && !_simExpanded);
    }

    private void ClearManualHeights()
    {
        if (_simScroll != null)
        {
            _simScroll.style.height = StyleKeyword.Null;
        }

        if (_replayScroll != null)
        {
            _replayScroll.style.height = StyleKeyword.Null;
        }

        if (_configPanel != null)
        {
            _configPanel.style.height = StyleKeyword.Null;
        }
    }

    private void ScheduleFitPanel()
    {
        // no-op: height via USS flex
    }

    private void FitPanel()
    {
        if (_fittingPanel || _root == null)
        {
            return;
        }

        return; // flex layout owns height

        if (!_simExpanded)
        {
            return;
        }

        var available = _root.contentRect.height;
        if (available <= 1f)
        {
            return;
        }

        _fittingPanel = true;
        FitMainScroll(available);
        FitConfigPanel(available);
        _fittingPanel = false;
    }

    private void FitMainScroll(float available)
    {
        if (_simScroll == null || _simWindow == null)
        {
            return;
        }

        var contentHeight = MeasureContentHeight(_simScroll.contentContainer);
        if (contentHeight <= 1f)
        {
            return;
        }

        var room = Mathf.Max(160f, available - PanelPadding(_simWindow));
        var target = contentHeight + 2f <= room ? contentHeight + 2f : room;
        SetHeightIfChanged(_simScroll, target);
    }

    private void FitConfigPanel(float available)
    {
        if (_configPanel == null || !_configOpen)
        {
            return;
        }

        SetHeightIfChanged(_configPanel, available);
    }

    private static float PanelPadding(VisualElement panel)
    {
        var padTop = panel.resolvedStyle.paddingTop;
        var padBottom = panel.resolvedStyle.paddingBottom;
        if (float.IsNaN(padTop))
        {
            padTop = 14f;
        }

        if (float.IsNaN(padBottom))
        {
            padBottom = 14f;
        }

        return padTop + padBottom;
    }

    private static void SetHeightIfChanged(VisualElement element, float target)
    {
        var current = element.resolvedStyle.height;
        if (!float.IsNaN(current) && Mathf.Abs(current - target) <= 1f)
        {
            return;
        }

        element.style.height = target;
    }

    private static void ConfigureScroll(ScrollView scroll)
    {
        if (scroll == null)
        {
            return;
        }

        scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
    }

    private static void ApplyFont(VisualElement element, StyleFontDefinition font)
    {
        if (element != null)
        {
            element.style.unityFontDefinition = font;
        }
    }

    private static float MeasureContentHeight(VisualElement container)
    {
        var height = 0f;
        var any = false;
        foreach (var child in container.Children())
        {
            if (child.resolvedStyle.display == DisplayStyle.None)
            {
                continue;
            }

            var bottom = child.layout.yMax;
            if (float.IsNaN(bottom))
            {
                continue;
            }

            var margin = child.resolvedStyle.marginBottom;
            if (!float.IsNaN(margin))
            {
                bottom += margin;
            }

            if (bottom > height)
            {
                height = bottom;
            }

            any = true;
        }

        if (!any)
        {
            var layoutHeight = container.layout.height;
            return float.IsNaN(layoutHeight) ? 0f : layoutHeight;
        }

        var pad = container.resolvedStyle.paddingBottom;
        if (!float.IsNaN(pad))
        {
            height += pad;
        }

        return height;
    }

    private void ApplyRunProgress(bool running, bool exporting)
    {
        if (_runProgress == null)
        {
            return;
        }

        var busy = running || exporting;
        if (_progressVisible != busy)
        {
            _progressVisible = busy;
            _runProgress.style.display = busy ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (!busy)
        {
            _runStartedAt = -1f;
            return;
        }

        if (_runStartedAt < 0f)
        {
            _runStartedAt = Time.realtimeSinceStartup;
        }

        if (exporting)
        {
            var elapsed = Time.realtimeSinceStartup - _runStartedAt;
            SetProgress($"导出中 {elapsed:0}s", Mathf.PingPong(elapsed * 40f, 100f));
            return;
        }

        var completed = m_runner != null ? m_runner.LiveInboundCompleted : 0;
        var target = m_runner != null ? m_runner.LiveInboundTarget : 0;
        var value = target > 0
            ? 100f * Mathf.Clamp01((float)completed / target)
            : 0f;
        SetProgress($"{completed}/{target}", value);
    }

    private void ApplyTimeline(bool hasTimeline, bool playbackBusy)
    {
        if (_timelineSlider != null)
        {
            _timelineSlider.SetEnabled(hasTimeline && !playbackBusy);
            if (hasTimeline && !IsDragging(_timelineSlider))
            {
                SetSliderValue(_timelineSlider, m_playback.NormalizedTime, ref _timelineValue);
            }
        }

        if (_timeLabel == null)
        {
            return;
        }

        if (!hasTimeline || m_playback == null)
        {
            SetLabelText(_timeLabel, "无回放时间轴。仿真完成后会自动加载。", ref _timeText);
            return;
        }

        var cur = System.Math.Floor(m_playback.CurrentSimTime);
        var end = System.Math.Floor(m_playback.EndSimTime);
        var percent = Mathf.RoundToInt(m_playback.NormalizedTime * 100f);
        SetLabelText(
            _timeLabel,
            $"{FormatSim(cur)} / {FormatSim(end)}    {percent}%",
            ref _timeText);
    }

    private void ApplySpeed()
    {
        var speed = m_playback != null ? Mathf.Clamp(m_playback.PlaybackSpeed, SpeedMin, SpeedMax) : 60f;
        if (_speedLabel != null)
        {
            SetLabelText(_speedLabel, $"倍速 {speed:0.#}x", ref _speedText);
        }

        if (_speedSlider != null && !IsDragging(_speedSlider))
        {
            SetSliderValue(_speedSlider, speed, ref _speedValue);
        }

        var chip = -1;
        for (var i = 0; i < _speedPresets.Length; i++)
        {
            var button = _speedPresets[i];
            if (button?.userData is float value && Mathf.Approximately(value, speed))
            {
                chip = i;
                break;
            }
        }

        if (chip == _speedChip)
        {
            return;
        }

        _speedChip = chip;
        for (var i = 0; i < _speedPresets.Length; i++)
        {
            var button = _speedPresets[i];
            if (button == null)
            {
                continue;
            }

            button.EnableInClassList("chip-on", i == chip);
            button.SetEnabled(m_playback != null);
        }
    }

    private void ApplySummary(NonsensicalKit.Simulation.WarehouseSimulation.Model.SimRunResult result)
    {
        if (_summaryLabel == null)
        {
            return;
        }

        var text = BuildSummary(result);
        if (text == _summaryText)
        {
            return;
        }

        _summaryText = text;
        _summaryLabel.text = text;
    }

    private void ApplyResultSource(bool fromFile, bool hasResult)
    {
        if (_resultSourceLabel == null)
        {
            return;
        }

        if (!hasResult)
        {
            _resultSourceLabel.text = string.Empty;
            SetVisible(_resultSourceLabel, false);
            return;
        }

        SetVisible(_resultSourceLabel, true);
        _resultSourceLabel.text = fromFile
            ? "来源：加载的回放文件（不是本次计算）"
            : "来源：本次仿真";
    }

    private void ApplyLoadedPath()
    {
        // 文件名已并入回放信息条；保留空实现以兼容旧绑定。
        if (_loadedPathLabel == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(_loadedReplayPath))
        {
            _loadedPathLabel.text = string.Empty;
            SetVisible(_loadedPathLabel, false);
            return;
        }

        SetVisible(_loadedPathLabel, true);
        _loadedPathLabel.text = Path.GetFileName(_loadedReplayPath);
        _loadedPathLabel.tooltip = _loadedReplayPath;
    }

    private void ClearReplayMeta()
    {
        _replayMetaConfig = "—";
        _replayMetaSource = "—";
        _replayMetaCompleted = -1;
        _replayMetaTarget = -1;
        _replayMetaSeconds = double.NaN;
    }

    private void ApplyReplayMetaFromPayload(SimReplayHtmlCodec.Payload payload, string path)
    {
        var config = payload != null ? payload.ScenarioName : null;
        if (string.IsNullOrWhiteSpace(config))
        {
            config = GuessConfigFromFileName(path);
        }

        _replayMetaConfig = string.IsNullOrWhiteSpace(config) ? "—" : config.Trim();
        _replayMetaSource = string.IsNullOrEmpty(path) ? "加载的文件" : Path.GetFileName(path);
        _replayMetaCompleted = payload?.CompletedJobCount ?? -1;
        _replayMetaTarget = payload?.TargetJobCount ?? -1;
        _replayMetaSeconds = payload != null ? payload.TotalSimSeconds : double.NaN;
    }

    private void ApplyReplayMetaFromResult(
        NonsensicalKit.Simulation.WarehouseSimulation.Model.SimRunResult result)
    {
        _replayMetaConfig = ResolveCurrentConfigName();
        _replayMetaSource = "本次仿真";
        if (result != null)
        {
            _replayMetaCompleted = result.CompletedJobCount;
            _replayMetaTarget = result.TargetJobCount;
            _replayMetaSeconds = result.TotalSimSeconds;
        }
        else
        {
            _replayMetaCompleted = -1;
            _replayMetaTarget = -1;
            _replayMetaSeconds = double.NaN;
        }
    }

    private string ResolveCurrentConfigName()
    {
        if (_presetNameField != null && !string.IsNullOrWhiteSpace(_presetNameField.value))
        {
            return _presetNameField.value.Trim();
        }

        if (m_runner != null && !string.IsNullOrWhiteSpace(m_runner.ScenarioJsonId))
        {
            return m_runner.ScenarioJsonId.Trim();
        }

        if (m_runner?.Scenario != null && !string.IsNullOrWhiteSpace(m_runner.Scenario.name))
        {
            return m_runner.Scenario.name.Trim();
        }

        return "—";
    }

    private static string GuessConfigFromFileName(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        // 默认导出名类似：场景名_report_时间戳
        const string marker = "_report_";
        var idx = name.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx > 0)
        {
            return name.Substring(0, idx);
        }

        return name;
    }

    private void ApplyReplayInfo(bool show)
    {
        SetVisible(_replayInfoCard, show);
        if (!show || _replayInfoLabel == null)
        {
            return;
        }

        var jobs = _replayMetaCompleted >= 0 || _replayMetaTarget >= 0
            ? $"{Math.Max(0, _replayMetaCompleted)}/{Math.Max(0, _replayMetaTarget)}"
            : "—";
        var duration = !double.IsNaN(_replayMetaSeconds) && _replayMetaSeconds >= 0d
            ? FormatSim(_replayMetaSeconds)
            : "—";
        var text =
            $"配置：{_replayMetaConfig}\n" +
            $"来源：{_replayMetaSource}\n" +
            $"任务：{jobs}\n" +
            $"时长：{duration}";
        SetLabelText(_replayInfoLabel, text, ref _replayInfoText);
    }

    private void ApplySimStatus(bool running, bool exporting, bool hasResult, bool fromFile)
    {
        if (_simStatusLabel == null)
        {
            return;
        }

        string text;
        if (running)
        {
            text = "仿真正在后台计算…";
        }
        else if (exporting)
        {
            text = "报告导出中…";
        }
        else if (_failed)
        {
            text = "仿真失败，请查看控制台。";
        }
        else if (fromFile && hasResult)
        {
            text = "当前结果来自回放文件。上方可重新运行仿真；下方可播放。";
        }
        else if (hasResult)
        {
            text = "仿真已完成。可导出报告，或到下方窗口播放。";
        }
        else
        {
            text = m_runner != null && m_runner.Scenario != null
                ? "就绪：选好配置档后点「运行仿真」。"
                : "未指定仿真配置档。";
        }

        SetLabelText(_simStatusLabel, text, ref _simStatusText);
    }

    private void ApplyReplayStatus(bool running, bool exporting, bool hasTimeline, bool fromFile, bool playing)
    {
        if (_replayStatusLabel == null)
        {
            return;
        }

        string text;
        if (running)
        {
            text = "等待仿真完成，完成后会自动载入回放。";
        }
        else if (exporting)
        {
            text = "报告导出中，请稍候。";
        }
        else if (playing)
        {
            text = "播放中。拖动进度条可跳转。";
        }
        else if (hasTimeline && fromFile)
        {
            text = "已从文件载入回放，点「播放」开始。";
        }
        else if (hasTimeline)
        {
            text = "回放已就绪，点「播放」开始。";
        }
        else
        {
            text = "暂无回放。可加载以前导出的可回放 HTML。";
        }

        SetLabelText(_replayStatusLabel, text, ref _replayStatusText);
    }

    private static string BuildSummary(NonsensicalKit.Simulation.WarehouseSimulation.Model.SimRunResult result)
    {
        if (result == null)
        {
            return "尚无仿真结果。";
        }

        var conflicts = result.OccupancyConflicts?.Count ?? 0;
        var segmentOverlaps = result.SegmentCoOccupancies?.Count ?? 0;
        var issues = result.SubTaskTimelineIssues?.Count ?? 0;
        return
            $"完成 {result.CompletedJobCount}/{result.TargetJobCount}\n" +
            $"仿真时长 {FormatSim(result.TotalSimSeconds)}    吞吐 {result.ThroughputPerHour:0.#} 箱/小时\n" +
            $"耗时 均值 {FormatSim(result.DurationMeanSeconds)}    P95 {FormatSim(result.DurationP95Seconds)}\n" +
            $"占用冲突 {conflicts}    路段重叠 {segmentOverlaps}    时间轴问题 {issues}";
    }

    private static string FormatSim(double seconds)
    {
        if (seconds < 0d || double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            seconds = 0d;
        }

        var total = (int)seconds;
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var secs = total % 60;
        return hours > 0
            ? $"{hours}:{minutes:00}:{secs:00}"
            : $"{minutes:00}:{secs:00}";
    }

    private void SetProgress(string title, float value)
    {
        if (_progressTitle != title)
        {
            _progressTitle = title;
            _runProgress.title = title;
        }

        if (float.IsNaN(_progressValue) || Mathf.Abs(_progressValue - value) > 0.05f)
        {
            _progressValue = value;
            _runProgress.value = value;
        }
    }

    private void SetSliderValue(Slider slider, float value, ref float cached)
    {
        if (!float.IsNaN(cached) && Mathf.Abs(cached - value) <= 0.0001f)
        {
            return;
        }

        cached = value;
        _syncingUi = true;
        slider.SetValueWithoutNotify(value);
        _syncingUi = false;
    }

    private static void SetLabelText(TextElement element, string text, ref string cached)
    {
        if (element == null || cached == text)
        {
            return;
        }

        cached = text;
        element.text = text;
    }

    private static void SetButtonEnabled(Button button, bool enabled, ref bool cached)
    {
        if (button == null || cached == enabled)
        {
            return;
        }

        cached = enabled;
        button.SetEnabled(enabled);
    }

    private static bool IsDragging(VisualElement element)
    {
        return element != null && element.HasPointerCapture(PointerId.mousePointerId);
    }
}
}
