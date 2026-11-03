using System;
using System.Collections;
using System.Collections.Generic;
using NaughtyAttributes;
using NonsensicalKit.Core;
using UnityEngine;
using Frame.Core.Config;
using Frame.Core.Network;

/// <summary>
/// 单条可回放录制文件（Inspector 预配，运行时下拉选择）。
/// </summary>

namespace Frame.Core.RecordPlayback
{
[Serializable]
public class RecordDataEntry
{
    [Tooltip("下拉显示名；留空则用文件名")]
    public string displayName;

    public TextAsset dataFile;

    public string ResolveDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName.Trim();
        }

        return dataFile != null ? dataFile.name : "(空)";
    }
}

/// <summary>
/// 通用录制数据回放：按 ticks 时间轴播放。
/// IOCC 事件名按类型固定（MQ→MQTTMessage，SR→SignalRMessage），与其它模块对齐。
/// 开始播放时自动切到离线数据源，结束/中断后恢复切换前模式。
/// </summary>
public class RecordDataPlayer : NonsensicalMono, IRecordPlaybackPlayer
{
    public const string MqttEventName = "MQTTMessage";
    public const string SignalREventName = "SignalRMessage";

    [SerializeField] private List<RecordDataEntry> m_dataFiles = new();
    [SerializeField] private int m_selectedIndex;
    [SerializeField] private TextAsset m_parseConfig;
    [SerializeField] private float m_timeScale = 1f;
    [SerializeField] private bool m_playOnStart;
    [SerializeField] private bool m_loop;
    [SerializeField] private bool m_log;
    [SerializeField] private bool m_autoSwitchOfflineData = true;

    [ShowNonSerializedField] private string _playTime = string.Empty;
    [ShowNonSerializedField] private int _playIndex;
    [ShowNonSerializedField] private bool _playing;
    [ShowNonSerializedField] private bool _paused;

    private RecordParseConfig _config;
    private Dictionary<string, RecordEventRule> _rulesByType;
    private Coroutine _playRoutine;
    private bool _switchedToOffline;
    private TwinDataSourceMode _modeBeforeOffline = TwinDataSourceMode.Online;
    private int _lineCount;

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
            {
                return null;
            }

            var index = Mathf.Clamp(m_selectedIndex, 0, m_dataFiles.Count - 1);
            return m_dataFiles[index]?.dataFile;
        }
    }

    public string DataFileName
    {
        get
        {
            var file = CurrentDataFile;
            return file != null ? file.name : string.Empty;
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
        {
            Play();
        }
    }

    private void OnDestroy()
    {
        RestoreOnlineDataSource();
    }

    private void OnValidate()
    {
        ClampSelectedIndex();
    }

    /// <summary>下拉选项显示名（与 m_dataFiles 一一对应）。</summary>
    public List<string> GetDisplayNames()
    {
        var names = new List<string>();
        if (m_dataFiles == null)
        {
            return names;
        }

        for (var i = 0; i < m_dataFiles.Count; i++)
        {
            names.Add(m_dataFiles[i] != null ? m_dataFiles[i].ResolveDisplayName() : "(空)");
        }

        return names;
    }

    /// <summary>切换预配文件；播放中会先停止。</summary>
    public void SetSelectedIndex(int index)
    {
        if (m_dataFiles == null || m_dataFiles.Count == 0)
        {
            m_selectedIndex = 0;
            return;
        }

        index = Mathf.Clamp(index, 0, m_dataFiles.Count - 1);
        if (m_selectedIndex == index)
        {
            return;
        }

        if (_playing)
        {
            Stop();
        }

        m_selectedIndex = index;
        NotifyStateChanged();
    }

    [Button("Play")]
    public void Play()
    {
        ClampSelectedIndex();
        var dataFile = CurrentDataFile;
        if (dataFile == null || m_parseConfig == null)
        {
            Debug.LogError("[RecordDataPlayer] 请配置 dataFiles 与 parseConfig");
            return;
        }

        try
        {
            _config = RecordPayloadParser.LoadConfig(m_parseConfig);
            _rulesByType = BuildRuleMap(_config);
        }
        catch (Exception e)
        {
            Debug.LogError($"[RecordDataPlayer] 加载解析配置失败: {e.Message}");
            return;
        }

        if (_playRoutine != null)
        {
            StopCoroutine(_playRoutine);
            _playRoutine = null;
        }

        _paused = false;
        EnterOfflineDataSource();
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
        RestoreOnlineDataSource();
        NotifyStateChanged();
    }

    public void TogglePlayStop()
    {
        if (_playing)
        {
            Stop();
        }
        else
        {
            Play();
        }
    }

    public void Pause()
    {
        if (!_playing || _paused)
        {
            return;
        }

        _paused = true;
        NotifyStateChanged();
    }

    public void Resume()
    {
        if (!_playing || !_paused)
        {
            return;
        }

        _paused = false;
        NotifyStateChanged();
    }

    public void TogglePause()
    {
        if (!_playing)
        {
            return;
        }

        if (_paused)
        {
            Resume();
        }
        else
        {
            Pause();
        }
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
        var lines = dataFile.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        _lineCount = lines.Length;
        NotifyStateChanged();

        try
        {
            do
            {
                _playIndex = 0;
                _playTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                long crtTicks = -1;

                while (_playIndex < lines.Length)
                {
                    while (_paused && _playing)
                    {
                        yield return null;
                    }

                    if (!_playing)
                    {
                        yield break;
                    }

                    var line = lines[_playIndex];
                    if (!RecordPayloadParser.TryParseLine(line, out var parsed))
                    {
                        _playIndex++;
                        continue;
                    }

                    _playTime = parsed.Time;

                    if (crtTicks < 0)
                    {
                        crtTicks = parsed.Ticks;
                    }

                    if (crtTicks >= parsed.Ticks)
                    {
                        Dispatch(parsed);
                        _playIndex++;
                    }

                    while (crtTicks < parsed.Ticks)
                    {
                        while (_paused && _playing)
                        {
                            yield return null;
                        }

                        if (!_playing)
                        {
                            yield break;
                        }

                        yield return null;
                        crtTicks += (long)(Time.deltaTime * 10000000d * m_timeScale);
                    }
                }
            } while (m_loop && _playing);
        }
        finally
        {
            _playing = false;
            _paused = false;
            _playRoutine = null;
            RestoreOnlineDataSource();
            NotifyStateChanged();
        }
    }

    private void EnterOfflineDataSource()
    {
        if (!m_autoSwitchOfflineData || _switchedToOffline)
        {
            return;
        }

        _modeBeforeOffline = TwinDataSource.IsReady
            ? TwinDataSource.Mode
            : TwinDataSourceMode.Online;
        TwinDataSource.SetMode(TwinDataSourceMode.Offline);
        IOCC.Publish(FrameMqttEvents.UseOfflineData, true);
        _switchedToOffline = true;
        if (m_log)
        {
            Debug.Log("[RecordDataPlayer] 已切换为离线数据源");
        }
    }

    private void RestoreOnlineDataSource()
    {
        if (!_switchedToOffline)
        {
            return;
        }

        TwinDataSource.SetMode(_modeBeforeOffline);
        IOCC.Publish(FrameMqttEvents.UseOfflineData, false);
        _switchedToOffline = false;
        if (m_log)
        {
            Debug.Log($"[RecordDataPlayer] 已恢复数据源 → {_modeBeforeOffline}");
        }
    }

    private void Dispatch(RecordPayloadParser.ParsedLine parsed)
    {
        if (!_rulesByType.TryGetValue(parsed.EventType, out var rule))
        {
            if (m_log)
            {
                Debug.Log($"[RecordDataPlayer] 未配置事件规则，跳过: {parsed.EventType}");
            }

            return;
        }

        if (rule.ignore)
        {
            return;
        }

        if (!RecordPayloadParser.TryParsePayload(parsed, rule))
        {
            if (m_log)
            {
                Debug.LogWarning($"[RecordDataPlayer] payload 解析失败 @ {_playIndex}: {parsed.EventType}");
            }

            return;
        }

        var body = ResolveBody(parsed);

        switch (parsed.EventType.ToUpperInvariant())
        {
            case "MQ":
                PublishMqtt(parsed.Key, parsed.Channel, body);
                break;
            case "SR":
                PublishSignalR(parsed.Key, body);
                break;
            default:
                if (m_log)
                {
                    Debug.Log($"[RecordDataPlayer] 无固定对接事件，跳过类型: {parsed.EventType}");
                }

                break;
        }
    }

    private void PublishMqtt(string key, string topic, string message)
    {
        IOCC.Publish(MqttEventName, topic, message);
        PublishWithID(MqttEventName, key, topic, message);

        if (m_log)
        {
            Debug.Log($"[RecordDataPlayer] {MqttEventName} key={key} topic={topic}\n{message}");
        }
    }

    private void PublishSignalR(string key, string message)
    {
        PublishWithID(SignalREventName, key, message);

        if (m_log)
        {
            Debug.Log($"[RecordDataPlayer] {SignalREventName} key={key}\n{message}");
        }
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke();
    }

    private static string ResolveBody(RecordPayloadParser.ParsedLine parsed)
    {
        if (parsed.Parts is { Length: > 0 })
        {
            return parsed.Parts.Length == 1
                ? parsed.Parts[0]
                : string.Join(RecordPayloadParser.FieldSeparator.ToString(), parsed.Parts);
        }

        return parsed.Payload ?? string.Empty;
    }

    private static Dictionary<string, RecordEventRule> BuildRuleMap(RecordParseConfig config)
    {
        var map = new Dictionary<string, RecordEventRule>(StringComparer.OrdinalIgnoreCase);
        if (config?.events == null)
        {
            return map;
        }

        foreach (var rule in config.events)
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.type))
            {
                continue;
            }

            map[rule.type.Trim()] = rule;
        }

        return map;
    }
}
}
