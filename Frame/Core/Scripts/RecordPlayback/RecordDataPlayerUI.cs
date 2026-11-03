using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 录制回放 UI：输送线 / AGV 共用一套控件，切换模式不打断另一路播放。
/// </summary>

namespace Frame.Core.RecordPlayback
{
public class RecordDataPlayerUI : MonoBehaviour
{
    public enum PlaybackMode
    {
        Conveyor = 0,
        Agv = 1
    }

    [SerializeField] private RecordDataPlayer m_player;
    [SerializeField] private AgvStatusTapePlayer m_agvPlayer;
    [SerializeField] private PlaybackMode m_mode = PlaybackMode.Conveyor;
    [SerializeField, Tooltip("关闭后隐藏 AGV 模式切换，仅保留输送线回放")]
    private bool m_showAgvPlayback = true;
    [SerializeField] private GameObject m_panel;
    [SerializeField] private Button m_openButton;
    [SerializeField] private Button m_closeButton;
    [SerializeField] private bool m_panelVisibleOnStart;
    [SerializeField] private Button m_modeConveyorButton;
    [SerializeField] private Button m_modeAgvButton;
    [SerializeField] private TMP_Text m_modeConveyorLabel;
    [SerializeField] private TMP_Text m_modeAgvLabel;
    [SerializeField] private TMP_Dropdown m_fileDropdown;
    [SerializeField] private Button m_playButton;
    [SerializeField] private Button m_stopButton;
    [SerializeField] private Button m_pauseButton;
    [SerializeField] private Toggle m_loopToggle;
    [SerializeField] private Slider m_speedSlider;
    [SerializeField] private TMP_Text m_playButtonLabel;
    [SerializeField] private TMP_Text m_pauseButtonLabel;
    [SerializeField] private TMP_Text m_statusText;
    [SerializeField] private TMP_Text m_timeText;
    [SerializeField] private TMP_Text m_progressText;
    [SerializeField] private TMP_Text m_speedText;
    [SerializeField] private float m_minSpeed = 0.25f;
    [SerializeField] private float m_maxSpeed = 8f;

    private bool _dropdownBuilt;
    private IRecordPlaybackPlayer _boundPlayer;

    public bool IsPanelVisible => m_panel != null && m_panel.activeSelf;

    private bool AgvPlaybackAvailable => m_showAgvPlayback && m_agvPlayer != null;

    private IRecordPlaybackPlayer ActivePlayer =>
        m_mode == PlaybackMode.Agv && AgvPlaybackAvailable
            ? (IRecordPlaybackPlayer)m_agvPlayer
            : m_player;

    private void Awake()
    {
        ResolvePlayers();
        ClampModeToAvailable();
        EnsureModeButtons();
        ApplyAgvVisibility();
        BindListeners();
        SetPanelVisible(m_panelVisibleOnStart);
    }

    private void OnEnable()
    {
        ResolvePlayers();
        ClampModeToAvailable();
        EnsureModeButtons();
        ApplyAgvVisibility();
        RebindActivePlayerEvents();
        RebuildFileDropdown(force: true);
        SyncControlsFromPlayer();
        RefreshModeButtons();
        Refresh();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying)
            return;
        ClampModeToAvailable();
        ApplyAgvVisibility();
        RefreshModeButtons();
    }
#endif

    private void OnDisable()
    {
        UnbindPlayerEvents();
    }

    private void Update()
    {
        var player = ActivePlayer;
        if (player != null && player.IsPlaying)
            RefreshProgress();
        else if (OtherPlayerPlaying())
            RefreshStatusOnly();
    }

    public void ShowPanel() => SetPanelVisible(true);

    public void HidePanel() => SetPanelVisible(false);

    public void TogglePanel() => SetPanelVisible(!IsPanelVisible);

    public void SetPanelVisible(bool visible)
    {
        if (m_panel != null)
            m_panel.SetActive(visible);
    }

    public void SetMode(PlaybackMode mode)
    {
        if (mode == PlaybackMode.Agv && !AgvPlaybackAvailable)
            mode = PlaybackMode.Conveyor;

        if (m_mode == mode)
            return;

        m_mode = mode;
        _dropdownBuilt = false;
        RebindActivePlayerEvents();
        RebuildFileDropdown(force: true);
        SyncControlsFromPlayer();
        RefreshModeButtons();
        Refresh();
    }

    /// <summary>Inspector / 运行时切换是否显示 AGV 回放入口。</summary>
    public void SetShowAgvPlayback(bool show)
    {
        if (m_showAgvPlayback == show)
            return;

        m_showAgvPlayback = show;
        ClampModeToAvailable();
        ApplyAgvVisibility();
        RebindActivePlayerEvents();
        RebuildFileDropdown(force: true);
        SyncControlsFromPlayer();
        RefreshModeButtons();
        Refresh();
    }

    private void ClampModeToAvailable()
    {
        if (m_mode == PlaybackMode.Agv && !AgvPlaybackAvailable)
            m_mode = PlaybackMode.Conveyor;
    }

    private void ApplyAgvVisibility()
    {
        if (m_modeAgvButton != null)
            m_modeAgvButton.gameObject.SetActive(m_showAgvPlayback);

        var modeRow = m_modeConveyorButton != null
            ? m_modeConveyorButton.transform.parent
            : m_panel != null
                ? m_panel.transform.Find("ModeRow")
                : null;

        if (modeRow == null)
            return;

        // 仅输送线时隐藏整行模式切换，避免只剩一个按钮。
        modeRow.gameObject.SetActive(m_showAgvPlayback);

        if (m_modeConveyorButton == null)
            return;

        var rt = m_modeConveyorButton.transform as RectTransform;
        if (rt == null)
            return;

        if (m_showAgvPlayback)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, 0f);
            rt.offsetMax = new Vector2(-4f, 0f);
        }
        else
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }

    /// <summary>供运行时简易面板注入引用。</summary>
    public void Bind(
        RecordDataPlayer player,
        GameObject panel,
        Button openButton,
        Button closeButton,
        TMP_Dropdown fileDropdown,
        Button playButton,
        Button stopButton,
        Button pauseButton,
        Toggle loopToggle,
        Slider speedSlider,
        TMP_Text playButtonLabel,
        TMP_Text pauseButtonLabel,
        TMP_Text statusText,
        TMP_Text timeText,
        TMP_Text progressText,
        TMP_Text speedText,
        AgvStatusTapePlayer agvPlayer = null)
    {
        UnbindPlayerEvents();

        m_player = player;
        m_agvPlayer = agvPlayer;
        m_panel = panel;
        m_openButton = openButton;
        m_closeButton = closeButton;
        m_fileDropdown = fileDropdown;
        m_playButton = playButton;
        m_stopButton = stopButton;
        m_pauseButton = pauseButton;
        m_loopToggle = loopToggle;
        m_speedSlider = speedSlider;
        m_playButtonLabel = playButtonLabel;
        m_pauseButtonLabel = pauseButtonLabel;
        m_statusText = statusText;
        m_timeText = timeText;
        m_progressText = progressText;
        m_speedText = speedText;
        _dropdownBuilt = false;

        EnsureModeButtons();
        ClampModeToAvailable();
        ApplyAgvVisibility();
        BindListeners();

        if (isActiveAndEnabled)
            RebindActivePlayerEvents();

        SetPanelVisible(m_panelVisibleOnStart);
        RebuildFileDropdown(force: true);
        SyncControlsFromPlayer();
        RefreshModeButtons();
        Refresh();
    }

    private void ResolvePlayers()
    {
        if (m_player == null)
        {
            m_player = GetComponent<RecordDataPlayer>();
            if (m_player == null)
                m_player = FindFirstObjectByType<RecordDataPlayer>();
        }

        if (m_agvPlayer == null)
        {
            m_agvPlayer = GetComponent<AgvStatusTapePlayer>();
            if (m_agvPlayer == null)
                m_agvPlayer = FindFirstObjectByType<AgvStatusTapePlayer>();
        }
    }

    private void EnsureModeButtons()
    {
        if (m_panel == null)
            return;

        if (m_modeConveyorButton != null && m_modeAgvButton != null)
            return;

        var existing = m_panel.transform.Find("ModeRow");
        if (existing != null)
        {
            m_modeConveyorButton = existing.Find("ModeConveyor")?.GetComponent<Button>();
            m_modeAgvButton = existing.Find("ModeAgv")?.GetComponent<Button>();
            m_modeConveyorLabel = m_modeConveyorButton != null
                ? m_modeConveyorButton.GetComponentInChildren<TMP_Text>()
                : null;
            m_modeAgvLabel = m_modeAgvButton != null
                ? m_modeAgvButton.GetComponentInChildren<TMP_Text>()
                : null;
            return;
        }

        var row = new GameObject("ModeRow", typeof(RectTransform));
        row.transform.SetParent(m_panel.transform, false);
        row.transform.SetSiblingIndex(1);

        var rowRt = row.GetComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0f, 1f);
        rowRt.anchorMax = new Vector2(1f, 1f);
        rowRt.pivot = new Vector2(0.5f, 1f);
        rowRt.anchoredPosition = new Vector2(0f, -40f);
        rowRt.sizeDelta = new Vector2(-28f, 28f);

        m_modeConveyorButton = CreateModeButton(row.transform, "ModeConveyor", "输送线", 0f);
        m_modeAgvButton = CreateModeButton(row.transform, "ModeAgv", "AGV", 0.5f);
        m_modeConveyorLabel = m_modeConveyorButton.GetComponentInChildren<TMP_Text>();
        m_modeAgvLabel = m_modeAgvButton.GetComponentInChildren<TMP_Text>();

        ShiftPanelContentForModeRow();
    }

    private static Button CreateModeButton(Transform parent, string name, string label, float anchorMinX)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(anchorMinX, 0f);
        rt.anchorMax = new Vector2(anchorMinX + 0.5f, 1f);
        rt.offsetMin = new Vector2(anchorMinX < 0.1f ? 0f : 4f, 0f);
        rt.offsetMax = new Vector2(anchorMinX < 0.1f ? -4f : 0f, 0f);

        var image = go.GetComponent<Image>();
        image.color = new Color(0.16f, 0.2f, 0.26f, 1f);

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        var text = labelGo.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 14;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;

        return go.GetComponent<Button>();
    }

    private void ShiftPanelContentForModeRow()
    {
        // 给 ModeRow 腾出空间：把状态/时间/进度/文件等整体下移约 32。
        string[] names =
        {
            "Status", "Time", "Progress", "FileLabel", "FileDropdown",
            "PlayButton", "PauseButton", "StopButton", "SpeedLabel", "SpeedValue",
            "SpeedSlider", "LoopToggle"
        };

        foreach (var name in names)
        {
            var t = m_panel.transform.Find(name);
            if (t == null)
                continue;
            var rt = t as RectTransform;
            if (rt == null)
                continue;
            var pos = rt.anchoredPosition;
            pos.y -= 32f;
            rt.anchoredPosition = pos;
        }

        var panelRt = m_panel.transform as RectTransform;
        if (panelRt != null)
            panelRt.sizeDelta = new Vector2(panelRt.sizeDelta.x, panelRt.sizeDelta.y + 36f);
    }

    private void BindListeners()
    {
        if (m_openButton != null)
        {
            m_openButton.onClick.RemoveListener(ShowPanel);
            m_openButton.onClick.AddListener(ShowPanel);
        }

        if (m_closeButton != null)
        {
            m_closeButton.onClick.RemoveListener(HidePanel);
            m_closeButton.onClick.AddListener(HidePanel);
        }

        if (m_modeConveyorButton != null)
        {
            m_modeConveyorButton.onClick.RemoveListener(OnModeConveyorClicked);
            m_modeConveyorButton.onClick.AddListener(OnModeConveyorClicked);
        }

        if (m_modeAgvButton != null)
        {
            m_modeAgvButton.onClick.RemoveListener(OnModeAgvClicked);
            m_modeAgvButton.onClick.AddListener(OnModeAgvClicked);
        }

        if (m_fileDropdown != null)
        {
            m_fileDropdown.onValueChanged.RemoveListener(OnFileChanged);
            m_fileDropdown.onValueChanged.AddListener(OnFileChanged);
        }

        if (m_playButton != null)
        {
            m_playButton.onClick.RemoveListener(OnPlayClicked);
            m_playButton.onClick.AddListener(OnPlayClicked);
        }

        if (m_stopButton != null)
        {
            m_stopButton.onClick.RemoveListener(OnStopClicked);
            m_stopButton.onClick.AddListener(OnStopClicked);
        }

        if (m_pauseButton != null)
        {
            m_pauseButton.onClick.RemoveListener(OnPauseClicked);
            m_pauseButton.onClick.AddListener(OnPauseClicked);
        }

        if (m_loopToggle != null)
        {
            m_loopToggle.onValueChanged.RemoveListener(OnLoopChanged);
            m_loopToggle.onValueChanged.AddListener(OnLoopChanged);
        }

        if (m_speedSlider != null)
        {
            m_speedSlider.minValue = m_minSpeed;
            m_speedSlider.maxValue = m_maxSpeed;
            m_speedSlider.onValueChanged.RemoveListener(OnSpeedChanged);
            m_speedSlider.onValueChanged.AddListener(OnSpeedChanged);
        }
    }

    private void OnModeConveyorClicked() => SetMode(PlaybackMode.Conveyor);

    private void OnModeAgvClicked() => SetMode(PlaybackMode.Agv);

    private void OnFileChanged(int index) => ActivePlayer?.SetSelectedIndex(index);

    private void OnPlayClicked()
    {
        var player = ActivePlayer;
        if (player == null)
            return;

        if (player.IsPlaying)
            player.Stop();
        else
            player.Play();
    }

    private void OnStopClicked() => ActivePlayer?.Stop();

    private void OnPauseClicked() => ActivePlayer?.TogglePause();

    private void OnLoopChanged(bool value)
    {
        var player = ActivePlayer;
        if (player != null)
            player.Loop = value;
    }

    private void OnSpeedChanged(float value)
    {
        var player = ActivePlayer;
        if (player != null)
            player.TimeScale = value;

        if (m_speedText != null)
            m_speedText.text = $"x{value:0.##}";
    }

    private void RebindActivePlayerEvents()
    {
        UnbindPlayerEvents();
        _boundPlayer = ActivePlayer;
        if (_boundPlayer != null)
            _boundPlayer.StateChanged += Refresh;
    }

    private void UnbindPlayerEvents()
    {
        if (_boundPlayer != null)
            _boundPlayer.StateChanged -= Refresh;
        _boundPlayer = null;
    }

    private void RebuildFileDropdown(bool force = false)
    {
        var player = ActivePlayer;
        if (m_fileDropdown == null || player == null)
            return;

        var names = player.GetDisplayNames();
        if (!force && _dropdownBuilt && m_fileDropdown.options.Count == names.Count)
        {
            m_fileDropdown.SetValueWithoutNotify(player.SelectedIndex);
            return;
        }

        m_fileDropdown.ClearOptions();
        m_fileDropdown.AddOptions(names);
        if (names.Count > 0)
            m_fileDropdown.SetValueWithoutNotify(Mathf.Clamp(player.SelectedIndex, 0, names.Count - 1));

        m_fileDropdown.RefreshShownValue();
        _dropdownBuilt = true;
    }

    private void SyncControlsFromPlayer()
    {
        var player = ActivePlayer;
        if (player == null)
            return;

        if (m_fileDropdown != null && player.DataFileCount > 0)
            m_fileDropdown.SetValueWithoutNotify(player.SelectedIndex);

        if (m_loopToggle != null)
            m_loopToggle.SetIsOnWithoutNotify(player.Loop);

        if (m_speedSlider != null)
        {
            var speed = Mathf.Clamp(player.TimeScale, m_minSpeed, m_maxSpeed);
            m_speedSlider.minValue = m_minSpeed;
            m_speedSlider.maxValue = m_maxSpeed;
            m_speedSlider.SetValueWithoutNotify(speed);
        }

        if (m_speedText != null)
            m_speedText.text = $"x{player.TimeScale:0.##}";
    }

    private void RefreshModeButtons()
    {
        SetModeButtonVisual(m_modeConveyorButton, m_modeConveyorLabel, m_mode == PlaybackMode.Conveyor);
        SetModeButtonVisual(m_modeAgvButton, m_modeAgvLabel, m_mode == PlaybackMode.Agv);
        if (m_modeAgvButton != null)
            m_modeAgvButton.interactable = AgvPlaybackAvailable;
    }

    private static void SetModeButtonVisual(Button button, TMP_Text label, bool active)
    {
        if (button == null)
            return;
        var image = button.GetComponent<Image>();
        if (image != null)
            image.color = active
                ? new Color(0.22f, 0.45f, 0.75f, 1f)
                : new Color(0.16f, 0.2f, 0.26f, 1f);
        if (label != null)
            label.fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
    }

    private void Refresh()
    {
        var player = ActivePlayer;
        if (player == null)
            return;

        if (m_playButtonLabel != null)
            m_playButtonLabel.text = player.IsPlaying ? "停止" : "播放";

        if (m_pauseButtonLabel != null)
            m_pauseButtonLabel.text = player.IsPaused ? "继续" : "暂停";

        if (m_pauseButton != null)
            m_pauseButton.interactable = player.IsPlaying;

        if (m_fileDropdown != null)
        {
            m_fileDropdown.interactable = !player.IsPlaying && player.DataFileCount > 0;
            if (player.DataFileCount > 0)
                m_fileDropdown.SetValueWithoutNotify(player.SelectedIndex);
        }

        RefreshStatusOnly();
        RefreshProgress();
    }

    private void RefreshStatusOnly()
    {
        if (m_statusText == null)
            return;

        var player = ActivePlayer;
        string modeName = m_mode == PlaybackMode.Agv ? "AGV" : "输送线";
        string otherHint = OtherPlayerPlaying()
            ? (m_mode == PlaybackMode.Agv ? " · 输送线也在播" : " · AGV也在播")
            : string.Empty;

        if (player == null)
        {
            m_statusText.text = $"状态：无播放器（{modeName}）";
            return;
        }

        if (!player.IsPlaying)
            m_statusText.text = $"状态：空闲（{modeName}）{otherHint}";
        else if (player.IsPaused)
            m_statusText.text = $"状态：暂停（{modeName} 离线）{otherHint}";
        else
            m_statusText.text = $"状态：播放中（{modeName} 离线）{otherHint}";
    }

    private bool OtherPlayerPlaying()
    {
        if (m_mode == PlaybackMode.Agv)
            return m_player != null && m_player.IsPlaying;
        return AgvPlaybackAvailable && m_agvPlayer.IsPlaying;
    }

    private void RefreshProgress()
    {
        var player = ActivePlayer;
        if (player == null)
            return;

        if (m_timeText != null)
        {
            m_timeText.text = string.IsNullOrEmpty(player.PlayTime)
                ? "时间：-"
                : $"时间：{player.PlayTime}";
        }

        if (m_progressText != null)
        {
            m_progressText.text =
                $"进度：{player.PlayIndex}/{player.LineCount}  ({player.Progress * 100f:0.0}%)";
        }
    }
}
}
