using System;
using System.Collections;
using System.Collections.Generic;
using Framework.Core;
using NaughtyAttributes;
using NonsensicalKit.Core;
using UnityEngine;

/// <summary>
/// AGV JSONL 录制回放：按 unixMs 时间轴播放。
/// 具体海康解析/发布由 <see cref="IAgvStatusTapePlaybackBackend"/> 提供（Samples 适配器注册）。
/// </summary>
public class AgvStatusTapePlayer : NonsensicalMono, IRecordPlaybackPlayer
{
    [SerializeField] private List<RecordDataEntry> m_dataFiles = new();
    [SerializeField] private int m_selectedIndex;
    [SerializeField] private float m_timeScale = 1f;
    [SerializeField] private bool m_playOnStart;
    [SerializeField] private bool m_loop;
    [SerializeField] private bool m_log;
    [SerializeField] private bool m_autoDisableStatusRequest = true;
    [SerializeField] private string m_fallbackMap = "AA";
    [Tooltip("相邻帧真实时间差超过该秒数时压缩等待，避免长时间静止空等。0=不压缩。")]
    [SerializeField] private float m_maxGapSeconds = 2f;
    [Tooltip("开始播放时跳过开头「位置未变」的空闲段，直接从首次位移前一帧起播。")]
    [SerializeField] private bool m_skipLeadingIdle = true;

    [Tooltip("可选：直接拖入 Samples 的 HikvisionAgvTapeBackend；为空则用 FrameOptionalServices 注册表。")]
    [SerializeField] private MonoBehaviour m_backendBehaviour;

    [ShowNonSerializedField] private string _playTime = string.Empty;
    [ShowNonSerializedField] private int _playIndex;
    [ShowNonSerializedField] private bool _playing;
    [ShowNonSerializedField] private bool _paused;

    private Coroutine _playRoutine;
    private int _lineCount;
    private IDisposable _liveSuspend;
    private static bool _missingBackendWarned;

    public bool IsPlaying => _playing;
    public bool IsPaused => _paused;
    public string PlayTime => _playTime;
    public int PlayIndex => _playIndex;
    public int LineCount => _lineCount;
    public float Progress => _lineCount <= 0 ? 0f : Mathf.Clamp01((float)_playIndex / _lineCount);
    public int DataFileCount => m_dataFiles?.Count ?? 0;

    public int SelectedIndex
    {
        get => m_selectedIndex;
        set => SetSelectedIndex(value);
    }

    public TextAsset CurrentDataFile
    {
        get
        {
            if (m_dataFiles == null || m_dataFiles.Count == 0)
                return null;
            var index = Mathf.Clamp(m_selectedIndex, 0, m_dataFiles.Count - 1);
            return m_dataFiles[index]?.dataFile;
        }
    }

    public float TimeScale
    {
        get => m_timeScale;
        set => m_timeScale = Mathf.Max(0.01f, value);
    }

    public bool Loop
    {
        get => m_loop;
        set => m_loop = value;
    }

    public event Action StateChanged;

    private void Start()
    {
        ClampSelectedIndex();
        if (m_playOnStart)
            Play();
    }

    private void OnDestroy()
    {
        RestoreLiveStatus();
    }

    private void OnValidate()
    {
        ClampSelectedIndex();
    }

    public List<string> GetDisplayNames()
    {
        var names = new List<string>();
        if (m_dataFiles == null)
            return names;

        for (var i = 0; i < m_dataFiles.Count; i++)
            names.Add(m_dataFiles[i] != null ? m_dataFiles[i].ResolveDisplayName() : "(空)");
        return names;
    }

    public void SetSelectedIndex(int index)
    {
        if (m_dataFiles == null || m_dataFiles.Count == 0)
        {
            m_selectedIndex = 0;
            return;
        }

        index = Mathf.Clamp(index, 0, m_dataFiles.Count - 1);
        if (m_selectedIndex == index)
            return;

        if (_playing)
            Stop();

        m_selectedIndex = index;
        NotifyStateChanged();
    }

    [Button("Play")]
    public void Play()
    {
        ClampSelectedIndex();
        var dataFile = CurrentDataFile;
        if (dataFile == null)
        {
            Debug.LogError("[AgvStatusTapePlayer] 请配置 dataFiles");
            return;
        }

        if (ResolveBackend() == null)
        {
            WarnMissingBackend();
            return;
        }

        if (_playRoutine != null)
        {
            StopCoroutine(_playRoutine);
            _playRoutine = null;
        }

        _paused = false;
        EnterOffline();
        _playRoutine = StartCoroutine(PlayData(dataFile));
        NotifyStateChanged();
    }

    [Button("Stop")]
    public void Stop()
    {
        if (_playRoutine != null)
        {
            StopCoroutine(_playRoutine);
            _playRoutine = null;
        }

        _playing = false;
        _paused = false;
        RestoreLiveStatus();
        NotifyStateChanged();
    }

    public void Pause()
    {
        if (!_playing || _paused)
            return;
        _paused = true;
        NotifyStateChanged();
    }

    public void Resume()
    {
        if (!_playing || !_paused)
            return;
        _paused = false;
        NotifyStateChanged();
    }

    public void TogglePause()
    {
        if (!_playing)
            return;
        if (_paused)
            Resume();
        else
            Pause();
    }

    private IAgvStatusTapePlaybackBackend ResolveBackend()
    {
        if (m_backendBehaviour is IAgvStatusTapePlaybackBackend injected)
            return injected;
        return FrameOptionalServices.AgvTape;
    }

    private void ClampSelectedIndex()
    {
        if (m_dataFiles == null || m_dataFiles.Count == 0)
        {
            m_selectedIndex = 0;
            return;
        }

        m_selectedIndex = Mathf.Clamp(m_selectedIndex, 0, m_dataFiles.Count - 1);
    }

    private IEnumerator PlayData(TextAsset dataFile)
    {
        _playing = true;
        var backend = ResolveBackend();
        string error = null;
        if (backend == null ||
            !backend.TryLoadFrames(dataFile.text, out var frames, out error) ||
            frames == null)
        {
            Debug.LogError("[AgvStatusTapePlayer] 加载失败: " + (error ?? "无后端"));
            _playing = false;
            _playRoutine = null;
            RestoreLiveStatus();
            NotifyStateChanged();
            yield break;
        }

        if (!string.IsNullOrEmpty(error) && frames.Count == 0)
        {
            Debug.LogError("[AgvStatusTapePlayer] 加载失败: " + error);
            _playing = false;
            _playRoutine = null;
            RestoreLiveStatus();
            NotifyStateChanged();
            yield break;
        }

        _lineCount = frames.Count;
        NotifyStateChanged();

        try
        {
            do
            {
                _playIndex = m_skipLeadingIdle ? FindLeadingIdleEnd(frames) : 0;
                _playTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                long crtMs = -1;
                long maxGapMs = m_maxGapSeconds > 0f ? (long)(m_maxGapSeconds * 1000d) : 0L;

                while (_playIndex < frames.Count)
                {
                    while (_paused && _playing)
                        yield return null;

                    if (!_playing)
                        yield break;

                    var frame = frames[_playIndex];
                    if (frame == null)
                    {
                        _playIndex++;
                        continue;
                    }

                    _playTime = string.IsNullOrEmpty(frame.Time)
                        ? FormatUnixMs(frame.UnixMs)
                        : frame.Time;

                    if (crtMs < 0)
                        crtMs = frame.UnixMs;

                    if (crtMs >= frame.UnixMs)
                    {
                        PublishFrame(backend, frame);
                        _playIndex++;
                        NotifyStateChanged();
                        continue;
                    }

                    if (maxGapMs > 0)
                    {
                        long remain = frame.UnixMs - crtMs;
                        if (remain > maxGapMs)
                            crtMs = frame.UnixMs - maxGapMs;
                    }

                    while (crtMs < frame.UnixMs)
                    {
                        while (_paused && _playing)
                            yield return null;

                        if (!_playing)
                            yield break;

                        yield return null;
                        crtMs += (long)(Time.deltaTime * 1000d * m_timeScale);
                    }
                }
            } while (m_loop && _playing);
        }
        finally
        {
            _playing = false;
            _paused = false;
            _playRoutine = null;
            RestoreLiveStatus();
            NotifyStateChanged();
        }
    }

    private void PublishFrame(IAgvStatusTapePlaybackBackend backend, IAgvStatusTapeFrame frame)
    {
        if (frame == null || !frame.Ok || frame.DataCount <= 0)
            return;

        backend.PublishFrame(frame, m_fallbackMap);

        if (m_log)
        {
            var map = string.IsNullOrEmpty(frame.Map) ? m_fallbackMap : frame.Map;
            Debug.Log($"[AgvStatusTapePlayer] {map}  {frame.DataCount} 台  {_playTime}", this);
        }
    }

    private void EnterOffline()
    {
        if (!m_autoDisableStatusRequest)
            return;

        RestoreLiveStatus();
        var backend = ResolveBackend();
        _liveSuspend = backend?.SuspendLiveStatusRequest();
    }

    private void RestoreLiveStatus()
    {
        _liveSuspend?.Dispose();
        _liveSuspend = null;
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke();
    }

    private static void WarnMissingBackend()
    {
        if (_missingBackendWarned)
            return;
        _missingBackendWarned = true;
        Debug.LogWarning(
            "[AgvStatusTapePlayer] 未找到 IAgvStatusTapePlaybackBackend。" +
            "请在场景挂载 FrameSample/Adapters/HikvisionAgv/HikvisionAgvTapeBackend，或拖到 m_backendBehaviour。播放已跳过。");
    }

    private static int FindLeadingIdleEnd(List<IAgvStatusTapeFrame> frames)
    {
        if (frames == null || frames.Count <= 1)
            return 0;

        string baseSig = frames[0]?.PositionSignature;
        if (string.IsNullOrEmpty(baseSig))
            return 0;

        for (var i = 1; i < frames.Count; i++)
        {
            var sig = frames[i]?.PositionSignature;
            if (!string.IsNullOrEmpty(sig) && !string.Equals(sig, baseSig, StringComparison.Ordinal))
                return Mathf.Max(0, i - 1);
        }

        return 0;
    }

    private static string FormatUnixMs(long unixMs)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }
        catch
        {
            return unixMs.ToString();
        }
    }
}
