#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Adapter;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Frame.Core.Config;
using Frame.Core.Mqtt;

/// <summary>
/// 编辑器 MQTT 测试客户端：连接 Broker、订阅主题、展示报文并校验是否能被运行时解析器正确处理。
/// </summary>

namespace Frame.Core.Network.Editor
{
public class MqttTestClientWindow : EditorWindow
{
    private const string PrefPrefix = "MqttTestClient.";
    private const string PasswordControlName = "MqttTestClient.Password";
    private const string UrlBaseConfigPath = "Assets/Resources/Configs/URLBaseConfig.asset";
    private const int MaxLogCount = 300;

    private static readonly string[] FallbackTopics =
    {
        "CSJ/CC_RC/+", "CSJ/LR/+", "CSJ/TCC/+", "DDJ/+", "TSJ/+"
    };

    private string _prefix = "ws://";
    private string _host = "localhost";
    private int _port = 8083;
    private string _path = "/mqtt";
    private string _user = "";
    private string _password = "";
    private bool _useWebSocket = true;
    private string _customTopic = "";
    private string _filter = "";
    private bool _onlyInvalid;
    private string _pubTopic = "CSJ/CC_RC/CSJ_CC_1001";
    private string _pubPayload = "{\n  \"DeviceCode\": \"CSJ_CC_1001\",\n  \"DeviceName\": \"CSJ_CC_1001#一层传输机\",\n  \"TopicType\": \"CC/RC\",\n  \"ReadTime\": \"2026-09-02T10:00:00.000\",\n  \"Points\": [\n    {\"PointCode\": \"ConvFwdStatus\", \"PointName\": \"输送正转状态\", \"Value\": \"true\", \"DataType\": \"Bool\"},\n    {\"PointCode\": \"ConvRevStatus\", \"PointName\": \"输送反转状态\", \"Value\": \"false\", \"DataType\": \"Bool\"},\n    {\"PointCode\": \"NoPallet\", \"PointName\": \"输送无托盘状态\", \"Value\": \"false\", \"DataType\": \"Bool\"}\n  ]\n}";

    private bool _showSettings = true;
    private bool _showTopicManager = true;
    private bool _showPublish;
    private bool _showDetail = true;
    private readonly List<string> _managedTopics = new();
    private Vector2 _topicManagerScroll;

    private IMqttClient _client;
    private CancellationTokenSource _cts;
    private volatile ClientState _state = ClientState.Disconnected;
    private string _statusText = "未连接";
    private readonly ConcurrentQueue<LogEntry> _incoming = new();
    private readonly List<LogEntry> _logs = new();
    private readonly Dictionary<string, DeviceSnapshot> _devices = new();
    private readonly List<string> _subscribed = new();
    private readonly object _subscribeLock = new object();
    private LogEntry _selected;
    private LogEntry _prettyPayloadEntry;
    private string _prettyPayload;
    private Vector2 _windowScroll;
    private Vector2 _logScroll;
    private Vector2 _deviceScroll;
    private Vector2 _detailScroll;
    private Vector2 _payloadScroll;
    private int _okCount;
    private int _failCount;
    private bool _dirty;
    private double _lastRepaint;

    private GUIStyle _okStyle;
    private GUIStyle _failStyle;
    private GUIStyle _warnStyle;
    private GUIStyle _mutedStyle;
    private GUIStyle _headerStyle;

    private enum ClientState
    {
        Disconnected,
        Connecting,
        Connected,
        Failed
    }

    [MenuItem("Tools/Frame/MQTT 测试客户端")]
    public static void Open()
    {
        var window = GetWindow<MqttTestClientWindow>("MQTT 测试客户端");
        window.minSize = new Vector2(640, 480);
        window.Show();
    }

    private void OnEnable()
    {
        LoadPrefs();
        TryLoadFromConfig(false);
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        SavePrefs();
        Disconnect();
    }

    private void OnEditorUpdate()
    {
        DrainIncoming();
        if (!_dirty || EditorApplication.timeSinceStartup - _lastRepaint < 0.08)
        {
            return;
        }

        _dirty = false;
        _lastRepaint = EditorApplication.timeSinceStartup;
        Repaint();
    }

    private void DrainIncoming()
    {
        while (_incoming.TryDequeue(out var entry))
        {
            _logs.Add(entry);
            if (entry.ParseOk)
            {
                _okCount++;
            }
            else
            {
                _failCount++;
            }

            string key = !string.IsNullOrEmpty(entry.DeviceCode) ? entry.DeviceCode : entry.Topic;
            _devices[key] = new DeviceSnapshot
            {
                Key = key,
                Topic = entry.Topic,
                DeviceCode = entry.DeviceCode,
                DeviceName = entry.DeviceName,
                TopicType = entry.TopicType,
                ReadTime = entry.ReadTime,
                PointCount = entry.PointCount,
                PointSummary = entry.PointSummary,
                ParseOk = entry.ParseOk,
                Warning = entry.Warning,
                ReceivedAt = entry.ReceivedAt
            };

            while (_logs.Count > MaxLogCount)
            {
                _logs.RemoveAt(0);
            }

            _dirty = true;
        }
    }

    private void OnGUI()
    {
        EnsureStyles();
        DrawToolbar();
        _windowScroll = EditorGUILayout.BeginScrollView(_windowScroll);
        DrawSettings();
        DrawTopicManager();
        DrawStats();
        DrawMainSplit();
        DrawDetail();
        DrawPublish();
        EditorGUILayout.EndScrollView();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUI.enabled = _state != ClientState.Connecting && _state != ClientState.Connected;
        if (GUILayout.Button("连接", EditorStyles.toolbarButton, GUILayout.Width(64)))
        {
            Connect();
        }

        GUI.enabled = _state == ClientState.Connected || _state == ClientState.Connecting;
        if (GUILayout.Button("断开", EditorStyles.toolbarButton, GUILayout.Width(64)))
        {
            Disconnect();
        }

        int managedCount;
        int subscribedCount;
        managedCount = _managedTopics.Count;
        lock (_subscribeLock)
        {
            subscribedCount = _subscribed.Count;
        }

        GUI.enabled = _state == ClientState.Connected && managedCount > 0;
        if (GUILayout.Button("全部订阅", EditorStyles.toolbarButton, GUILayout.Width(72)))
        {
            SubscribeTopics(SnapshotManagedTopics());
        }

        GUI.enabled = _state == ClientState.Connected && subscribedCount > 0;
        if (GUILayout.Button("全部取消订阅", EditorStyles.toolbarButton, GUILayout.Width(96)))
        {
            UnsubscribeTopics(SnapshotSubscribedTopics());
        }

        GUI.enabled = true;
        if (GUILayout.Button("读取 URLBaseConfig", EditorStyles.toolbarButton, GUILayout.Width(140)))
        {
            TryLoadFromConfig(true);
        }

        GUILayout.FlexibleSpace();
        var stateColor = _state switch
        {
            ClientState.Connected => new Color(0.35f, 0.85f, 0.45f),
            ClientState.Connecting => new Color(0.95f, 0.8f, 0.2f),
            ClientState.Failed => new Color(1f, 0.4f, 0.35f),
            _ => new Color(0.7f, 0.7f, 0.7f)
        };
        var old = GUI.color;
        GUI.color = stateColor;
        GUILayout.Label(_statusText, EditorStyles.miniLabel, GUILayout.MaxWidth(320));
        GUI.color = old;
        EditorGUILayout.EndHorizontal();
    }

    private void DrawSettings()
    {
        _showSettings = EditorGUILayout.Foldout(_showSettings, "连接设置  ·  " + BuildBrokerLabel(), true);
        if (!_showSettings)
        {
            return;
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUI.BeginDisabledGroup(_state == ClientState.Connected || _state == ClientState.Connecting);

        EditorGUILayout.BeginHorizontal();
        _useWebSocket = EditorGUILayout.ToggleLeft("WebSocket", _useWebSocket, GUILayout.Width(92));
        DrawInlineLabel("前缀", 32);
        _prefix = EditorGUILayout.TextField(_prefix, GUILayout.Width(56));
        DrawInlineLabel("主机", 32);
        _host = EditorGUILayout.TextField(_host, GUILayout.MinWidth(80));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        DrawInlineLabel("端口", 32);
        _port = EditorGUILayout.IntField(_port, GUILayout.Width(70));
        if (_useWebSocket)
        {
            DrawInlineLabel("路径", 32);
            _path = EditorGUILayout.TextField(_path, GUILayout.Width(90));
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        DrawInlineLabel("账号", 32);
        _user = EditorGUILayout.TextField(_user, GUILayout.MinWidth(80));
        DrawInlineLabel("密码", 32);
        // PasswordField 在 Layout 事件上会返回空串。若直接写回，点「连接」前密码已被清掉，Broker 就会回 NotAuthorized。
        GUI.SetNextControlName(PasswordControlName);
        EditorGUI.BeginChangeCheck();
        string editedPassword = EditorGUILayout.PasswordField(_password ?? string.Empty, GUILayout.MinWidth(80));
        if (EditorGUI.EndChangeCheck()
            && Event.current.type != EventType.Layout
            && (GUI.GetNameOfFocusedControl() == PasswordControlName || !string.IsNullOrEmpty(editedPassword)))
        {
            _password = editedPassword;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndVertical();
    }

    private void DrawTopicManager()
    {
        int managedCount = _managedTopics.Count;
        int subscribedCount;
        lock (_subscribeLock)
        {
            subscribedCount = _subscribed.Count;
        }

        _showTopicManager = EditorGUILayout.Foldout(
            _showTopicManager,
            $"主题订阅管理  ·  列表 {managedCount}  /  已订阅 {subscribedCount}",
            true);
        if (!_showTopicManager)
        {
            return;
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("从配置加载", GUILayout.Width(88)))
        {
            LoadManagedTopicsFromConfig(true);
        }

        GUI.enabled = _state == ClientState.Connected && managedCount > 0;
        if (GUILayout.Button("全部订阅", GUILayout.Width(72)))
        {
            SubscribeTopics(SnapshotManagedTopics());
        }

        GUI.enabled = _state == ClientState.Connected && subscribedCount > 0;
        if (GUILayout.Button("全部取消订阅", GUILayout.Width(96)))
        {
            UnsubscribeTopics(SnapshotSubscribedTopics());
        }

        GUI.enabled = managedCount > 0;
        if (GUILayout.Button("清空列表", GUILayout.Width(72)))
        {
            ClearManagedTopics();
        }

        GUI.enabled = true;
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        DrawInlineLabel("主题", 32);
        _customTopic = EditorGUILayout.TextField(_customTopic);
        GUI.enabled = !string.IsNullOrWhiteSpace(_customTopic);
        if (GUILayout.Button("添加", GUILayout.Width(52)))
        {
            AddManagedTopic(_customTopic);
            _customTopic = "";
            GUI.FocusControl(null);
        }

        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        if (managedCount == 0)
        {
            EditorGUILayout.LabelField("列表为空。可手动添加，或点「从配置加载」。", _mutedStyle);
        }
        else
        {
            _topicManagerScroll = EditorGUILayout.BeginScrollView(_topicManagerScroll, GUILayout.MinHeight(72), GUILayout.MaxHeight(180));
            for (int i = 0; i < _managedTopics.Count; i++)
            {
                string topic = _managedTopics[i];
                bool isSubscribed;
                lock (_subscribeLock)
                {
                    isSubscribed = _subscribed.Contains(topic);
                }

                EditorGUILayout.BeginHorizontal();
                var statusStyle = isSubscribed ? _okStyle : _mutedStyle;
                GUILayout.Label(isSubscribed ? "已订" : "未订", statusStyle, GUILayout.Width(36));
                GUILayout.Label(topic, EditorStyles.miniLabel);

                GUI.enabled = _state == ClientState.Connected && !isSubscribed;
                if (GUILayout.Button("订阅", GUILayout.Width(44)))
                {
                    SubscribeTopics(new[] { topic });
                }

                GUI.enabled = _state == ClientState.Connected && isSubscribed;
                if (GUILayout.Button("取消", GUILayout.Width(44)))
                {
                    UnsubscribeTopics(new[] { topic });
                }

                GUI.enabled = true;
                if (GUILayout.Button("删除", GUILayout.Width(44)))
                {
                    RemoveManagedTopicAt(i);
                    i--;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawStats()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label($"收到 {_logs.Count}    正常 {_okCount}    异常 {_failCount}    设备 {_devices.Count}",
            _headerStyle);
        GUILayout.FlexibleSpace();
        DrawInlineLabel("筛选", 32);
        _filter = EditorGUILayout.TextField(_filter, GUILayout.Width(160));
        _onlyInvalid = GUILayout.Toggle(_onlyInvalid, "仅异常", GUILayout.Width(60));
        if (GUILayout.Button("清空", GUILayout.Width(52)))
        {
            _logs.Clear();
            _devices.Clear();
            _selected = null;
            ClearPrettyPayloadCache();
            _okCount = 0;
            _failCount = 0;
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawMainSplit()
    {
        bool sideBySide = position.width >= 860f;
        if (sideBySide)
        {
            EditorGUILayout.BeginHorizontal();
            DrawLogList(GUILayout.ExpandWidth(true), GUILayout.MinHeight(220), GUILayout.MaxHeight(360));
            DrawDeviceList(GUILayout.Width(260), GUILayout.MinHeight(220), GUILayout.MaxHeight(360));
            EditorGUILayout.EndHorizontal();
        }
        else
        {
            DrawLogList(GUILayout.MinHeight(180), GUILayout.MaxHeight(280));
            DrawDeviceList(GUILayout.MinHeight(120), GUILayout.MaxHeight(200));
        }
    }

    private void DrawLogList(params GUILayoutOption[] options)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, options);
        GUILayout.Label("最近报文", _headerStyle);
        DrawLogHeader();
        _logScroll = EditorGUILayout.BeginScrollView(_logScroll);
        for (int i = _logs.Count - 1; i >= 0; i--)
        {
            var entry = _logs[i];
            if (!PassFilter(entry))
            {
                continue;
            }

            DrawLogRow(entry);
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawLogHeader()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("时间", EditorStyles.miniLabel, GUILayout.Width(64));
        GUILayout.Label("主题", EditorStyles.miniLabel, GUILayout.MinWidth(80));
        GUILayout.Label("设备", EditorStyles.miniLabel, GUILayout.Width(110));
        GUILayout.Label("点位", EditorStyles.miniLabel, GUILayout.Width(36));
        GUILayout.Label("状态", EditorStyles.miniLabel, GUILayout.Width(72));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawLogRow(LogEntry entry)
    {
        Rect row = EditorGUILayout.BeginHorizontal(GUILayout.Height(20));
        if (ReferenceEquals(_selected, entry) && Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(row, new Color(0.24f, 0.42f, 0.72f, 0.35f));
        }

        if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
        {
            _selected = entry;
            Event.current.Use();
            Repaint();
        }

        var statusStyle = entry.ParseOk
            ? (string.IsNullOrEmpty(entry.Warning) ? _okStyle : _warnStyle)
            : _failStyle;
        GUILayout.Label($"{entry.ReceivedAt:HH:mm:ss}", EditorStyles.miniLabel, GUILayout.Width(64));
        GUILayout.Label(entry.Topic ?? string.Empty, EditorStyles.miniLabel, GUILayout.MinWidth(80));
        GUILayout.Label(string.IsNullOrEmpty(entry.DeviceCode) ? "-" : entry.DeviceCode, EditorStyles.miniLabel,
            GUILayout.Width(110));
        GUILayout.Label(entry.PointCount.ToString(), EditorStyles.miniLabel, GUILayout.Width(36));
        GUILayout.Label(entry.StatusLabel, statusStyle, GUILayout.Width(72));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawDeviceList(params GUILayoutOption[] options)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, options);
        GUILayout.Label("设备最新状态", _headerStyle);
        _deviceScroll = EditorGUILayout.BeginScrollView(_deviceScroll);
        var snapshots = new List<DeviceSnapshot>(_devices.Values);
        snapshots.Sort((a, b) => b.ReceivedAt.CompareTo(a.ReceivedAt));
        foreach (var snap in snapshots)
        {
            if (!string.IsNullOrEmpty(_filter) &&
                (snap.DeviceCode == null || snap.DeviceCode.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0) &&
                (snap.Topic == null || snap.Topic.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0))
            {
                continue;
            }

            EditorGUILayout.BeginHorizontal();
            var style = snap.ParseOk
                ? (string.IsNullOrEmpty(snap.Warning) ? _okStyle : _warnStyle)
                : _failStyle;
            GUILayout.Label(snap.ParseOk ? "正常" : "异常", style, GUILayout.Width(36));
            EditorGUILayout.BeginVertical();
            GUILayout.Label(string.IsNullOrEmpty(snap.DeviceCode) ? snap.Key : snap.DeviceCode, EditorStyles.miniBoldLabel);
            GUILayout.Label(
                $"{snap.TopicType}  {snap.Topic}\n{snap.PointSummary}",
                _mutedStyle);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawDetail()
    {
        _showDetail = EditorGUILayout.Foldout(_showDetail, "报文详情", true);
        if (!_showDetail)
        {
            return;
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        if (_selected == null)
        {
            GUILayout.Label("点击一条报文查看 JSON 与解析结果。", _mutedStyle);
            EditorGUILayout.EndVertical();
            return;
        }

        float oldWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 48;
        var entry = _selected;
        EditorGUILayout.LabelField("主题", entry.Topic ?? string.Empty);
        EditorGUILayout.LabelField("设备", $"{entry.DeviceCode}  {entry.DeviceName}");
        EditorGUILayout.LabelField("类型", $"{entry.TopicType}    {entry.ReadTime}");
        EditorGUILayout.LabelField("校验", string.IsNullOrEmpty(entry.Warning) ? entry.StatusLabel : entry.Warning);
        if (!string.IsNullOrEmpty(entry.PointSummary))
        {
            EditorGUILayout.LabelField("点位", entry.PointSummary, _mutedStyle);
        }

        EditorGUIUtility.labelWidth = oldWidth;

        _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll, GUILayout.Height(300));
        EditorGUILayout.TextArea(GetPrettyPayload(entry), GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private string GetPrettyPayload(LogEntry entry)
    {
        if (entry == null)
        {
            return string.Empty;
        }

        if (ReferenceEquals(_prettyPayloadEntry, entry) && _prettyPayload != null)
        {
            return _prettyPayload;
        }

        _prettyPayloadEntry = entry;
        _prettyPayload = FormatJsonIndented(entry.Payload);
        return _prettyPayload;
    }

    private void ClearPrettyPayloadCache()
    {
        _prettyPayloadEntry = null;
        _prettyPayload = null;
    }

    private static string FormatJsonIndented(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw ?? string.Empty;
        }

        try
        {
            return JToken.Parse(raw).ToString(Formatting.Indented);
        }
        catch
        {
            return raw;
        }
    }

    private void DrawPublish()
    {
        _showPublish = EditorGUILayout.Foldout(_showPublish, "发送测试报文", true);
        if (!_showPublish)
        {
            return;
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        DrawInlineLabel("Topic", 36);
        _pubTopic = EditorGUILayout.TextField(_pubTopic);
        EditorGUILayout.EndHorizontal();
        _payloadScroll = EditorGUILayout.BeginScrollView(_payloadScroll, GUILayout.Height(90));
        _pubPayload = EditorGUILayout.TextArea(_pubPayload, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
        EditorGUILayout.BeginHorizontal();
        GUI.enabled = _state == ClientState.Connected;
        if (GUILayout.Button("发送", GUILayout.Width(72)))
        {
            Publish(_pubTopic, _pubPayload);
        }

        GUI.enabled = true;
        if (GUILayout.Button("填入传输机样例", GUILayout.Width(120)))
        {
            FillSample();
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private static void DrawInlineLabel(string text, float width)
    {
        GUILayout.Label(text, EditorStyles.miniLabel, GUILayout.Width(width));
    }

    private bool PassFilter(LogEntry entry)
    {
        if (_onlyInvalid && entry.ParseOk && string.IsNullOrEmpty(entry.Warning))
        {
            return false;
        }

        if (string.IsNullOrEmpty(_filter))
        {
            return true;
        }

        return (entry.Topic != null && entry.Topic.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0)
               || (entry.DeviceCode != null && entry.DeviceCode.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0)
               || (entry.Payload != null && entry.Payload.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private void Connect()
    {
        Disconnect();
        _state = ClientState.Connecting;
        _statusText = "连接中…";
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var options = BuildOptions();
        var topics = SnapshotManagedTopics();
        if (topics.Count == 0)
        {
            LoadManagedTopicsFromConfig(false);
            topics = SnapshotManagedTopics();
        }

        Task.Run(async () =>
        {
            try
            {
                var client = new MqttFactory().CreateMqttClient();
                client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
                client.DisconnectedAsync += _ =>
                {
                    if (_state == ClientState.Connected)
                    {
                        _state = ClientState.Failed;
                        _statusText = "已断线";
                        _dirty = true;
                    }

                    return Task.CompletedTask;
                };

                _client = client;
                await client.ConnectAsync(options, token);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                _state = ClientState.Connected;
                _statusText = "已连接  " + BuildBrokerLabel();
                SubscribeTopics(topics);
                _dirty = true;
            }
            catch (Exception e)
            {
                _state = ClientState.Failed;
                _statusText = "连接失败: " + DescribeConnectError(e) + "  " + CredentialHint();
                _dirty = true;
            }
        }, token);
    }

    private void Disconnect()
    {
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // ignored
        }

        _cts = null;
        var client = _client;
        _client = null;
        lock (_subscribeLock)
        {
            _subscribed.Clear();
        }
        if (client != null)
        {
            Task.Run(async () =>
            {
                try
                {
                    if (client.IsConnected)
                    {
                        await client.DisconnectAsync();
                    }
                }
                catch
                {
                    // ignored
                }
                finally
                {
                    client.Dispose();
                }
            });
        }

        _state = ClientState.Disconnected;
        _statusText = "未连接";
        _dirty = true;
    }

    private void SubscribeTopics(IEnumerable<string> topics)
    {
        var client = _client;
        if (client == null || !client.IsConnected || topics == null)
        {
            return;
        }

        var list = new List<string>();
        lock (_subscribeLock)
        {
            foreach (var topic in topics)
            {
                if (string.IsNullOrWhiteSpace(topic) || _subscribed.Contains(topic))
                {
                    continue;
                }

                list.Add(topic);
            }
        }

        if (list.Count == 0)
        {
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                var options = new MqttClientSubscribeOptions
                {
                    TopicFilters = list.Select(t => new MqttTopicFilterBuilder()
                            .WithTopic(t)
                            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                            .Build())
                        .ToList()
                };

                await client.SubscribeAsync(options);
                lock (_subscribeLock)
                {
                    foreach (var topic in list)
                    {
                        if (!_subscribed.Contains(topic))
                        {
                            _subscribed.Add(topic);
                        }
                    }
                }

                _dirty = true;
            }
            catch (Exception e)
            {
                _statusText = "订阅失败: " + e.Message;
                _dirty = true;
            }
        });
    }

    private void UnsubscribeTopics(IEnumerable<string> topics)
    {
        var client = _client;
        if (topics == null)
        {
            return;
        }

        var list = new List<string>();
        foreach (var topic in topics)
        {
            if (string.IsNullOrWhiteSpace(topic) || list.Contains(topic))
            {
                continue;
            }

            list.Add(topic);
        }

        if (list.Count == 0)
        {
            return;
        }

        if (client == null || !client.IsConnected)
        {
            lock (_subscribeLock)
            {
                foreach (var topic in list)
                {
                    _subscribed.Remove(topic);
                }
            }

            _dirty = true;
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                var builder = new MqttClientUnsubscribeOptionsBuilder();
                foreach (var topic in list)
                {
                    builder.WithTopicFilter(topic);
                }

                await client.UnsubscribeAsync(builder.Build());
                lock (_subscribeLock)
                {
                    foreach (var topic in list)
                    {
                        _subscribed.Remove(topic);
                    }
                }

                _dirty = true;
            }
            catch (Exception e)
            {
                _statusText = "取消订阅失败: " + e.Message;
                _dirty = true;
            }
        });
    }

    private List<string> SnapshotManagedTopics()
    {
        return new List<string>(_managedTopics);
    }

    private List<string> SnapshotSubscribedTopics()
    {
        lock (_subscribeLock)
        {
            return new List<string>(_subscribed);
        }
    }

    private void AddManagedTopic(string rawTopic)
    {
        string topic = rawTopic?.Trim();
        if (string.IsNullOrEmpty(topic))
        {
            return;
        }

        if (!_managedTopics.Contains(topic))
        {
            _managedTopics.Add(topic);
            SaveManagedTopicsPrefs();
        }

        if (_state == ClientState.Connected)
        {
            SubscribeTopics(new[] { topic });
        }

        _dirty = true;
    }

    private void RemoveManagedTopicAt(int index)
    {
        if (index < 0 || index >= _managedTopics.Count)
        {
            return;
        }

        string topic = _managedTopics[index];
        _managedTopics.RemoveAt(index);
        SaveManagedTopicsPrefs();
        UnsubscribeTopics(new[] { topic });
        _dirty = true;
    }

    private void ClearManagedTopics()
    {
        var toUnsubscribe = SnapshotManagedTopics();
        _managedTopics.Clear();
        SaveManagedTopicsPrefs();
        UnsubscribeTopics(toUnsubscribe);
        _dirty = true;
    }

    private void LoadManagedTopicsFromConfig(bool notify)
    {
        var configured = GetConfiguredTopics();
        bool changed = false;
        foreach (var topic in configured)
        {
            if (string.IsNullOrWhiteSpace(topic) || _managedTopics.Contains(topic))
            {
                continue;
            }

            _managedTopics.Add(topic);
            changed = true;
        }

        if (changed)
        {
            SaveManagedTopicsPrefs();
        }

        if (_state == ClientState.Connected)
        {
            SubscribeTopics(configured);
        }

        if (notify)
        {
            ShowNotification(new GUIContent(changed
                ? $"已合并配置主题，当前列表 {_managedTopics.Count} 条"
                : $"配置主题已在列表中，共 {_managedTopics.Count} 条"));
        }

        _dirty = true;
    }

    private void SaveManagedTopicsPrefs()
    {
        EditorPrefs.SetString(PrefPrefix + "ManagedTopics", string.Join("\n", _managedTopics));
    }

    private void LoadManagedTopicsPrefs()
    {
        _managedTopics.Clear();
        string raw = EditorPrefs.GetString(PrefPrefix + "ManagedTopics", string.Empty);
        if (string.IsNullOrEmpty(raw))
        {
            return;
        }

        string[] parts = raw.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            string topic = part.Trim();
            if (!string.IsNullOrEmpty(topic) && !_managedTopics.Contains(topic))
            {
                _managedTopics.Add(topic);
            }
        }
    }

    private void Publish(string topic, string payload)
    {
        var client = _client;
        if (client == null || !client.IsConnected || string.IsNullOrWhiteSpace(topic))
        {
            return;
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic.Trim())
            .WithPayload(payload ?? string.Empty)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(false)
            .Build();
        client.PublishAsync(message);
    }

    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs arg)
    {
        string topic = arg.ApplicationMessage.Topic;
        string payload = ReadPayload(arg.ApplicationMessage);
        var entry = BuildLogEntry(topic, payload);
        _incoming.Enqueue(entry);
        _dirty = true;
        return Task.CompletedTask;
    }

    private static LogEntry BuildLogEntry(string topic, string payload)
    {
        var entry = new LogEntry
        {
            ReceivedAt = DateTime.Now,
            Topic = topic,
            Payload = payload
        };

        if (!MqttPayloadParser.TryParse(payload, out var data) || data == null)
        {
            entry.ParseOk = false;
            entry.StatusLabel = "解析失败";
            entry.Warning = "JSON 无法解析为设备报文（需 DeviceCode + Points）";
            return entry;
        }

        entry.ParseOk = true;
        entry.DeviceCode = data.DeviceCode;
        entry.DeviceName = data.DeviceName;
        entry.TopicType = data.TopicType;
        entry.ReadTime = data.ReadTime;
        entry.PointCount = data.Points.Count;
        entry.PointSummary = BuildPointSummary(data);
        entry.StatusLabel = "正常";

        if (!TopicMatchesDevice(topic, data.DeviceCode))
        {
            entry.Warning = $"主题末段与 DeviceCode 不一致: {LastSegment(topic)} / {data.DeviceCode}";
            entry.StatusLabel = "主题不匹配";
        }

        return entry;
    }

    private static string BuildPointSummary(MqttDevicePayload data)
    {
        if (data.Points == null || data.Points.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>(data.Points.Count);
        foreach (var p in data.Points)
        {
            if (p == null || string.IsNullOrEmpty(p.PointCode))
            {
                continue;
            }

            parts.Add($"{p.PointCode}={p.Value}");
        }

        return string.Join(", ", parts);
    }

    private static bool TopicMatchesDevice(string topic, string deviceCode)
    {
        if (string.IsNullOrEmpty(topic) || string.IsNullOrEmpty(deviceCode))
        {
            return false;
        }

        return string.Equals(LastSegment(topic), deviceCode, StringComparison.OrdinalIgnoreCase);
    }

    private static string LastSegment(string topic)
    {
        int i = topic.LastIndexOf('/');
        return i >= 0 ? topic.Substring(i + 1) : topic;
    }

    private static string ReadPayload(MqttApplicationMessage msg)
    {
        if (msg == null)
        {
            return string.Empty;
        }

        try
        {
            return msg.ConvertPayloadToString() ?? string.Empty;
        }
        catch
        {
            var seg = msg.PayloadSegment;
            if (seg.Array == null || seg.Count <= 0)
            {
                return string.Empty;
            }

            return Encoding.UTF8.GetString(seg.Array, seg.Offset, seg.Count);
        }
    }

    private MqttClientOptions BuildOptions()
    {
        bool useTls = _prefix.StartsWith("wss", StringComparison.OrdinalIgnoreCase)
                      || _prefix.StartsWith("ssl", StringComparison.OrdinalIgnoreCase)
                      || _prefix.StartsWith("mqtts", StringComparison.OrdinalIgnoreCase);
        string user = _user?.Trim() ?? string.Empty;
        var builder = new MqttClientOptionsBuilder()
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .WithClientId("sjz" + Guid.NewGuid().ToString("N").Substring(0, 8))
            .WithCleanSession()
            .WithTlsOptions(new MqttClientTlsOptions { UseTls = useTls });

        if (!string.IsNullOrEmpty(user))
        {
            builder.WithCredentials(user, _password ?? string.Empty);
        }

        if (_useWebSocket)
        {
            builder.WithWebSocketServer(o => o.WithUri(BuildBrokerLabel()));
        }
        else
        {
            builder.WithTcpServer(_host?.Trim(), _port);
        }

        return builder.Build();
    }

    private static string DescribeConnectError(Exception exception)
    {
        if (exception is MqttConnectingFailedException failed)
        {
            switch (failed.ResultCode)
            {
                case MqttClientConnectResultCode.NotAuthorized:
                    return "没有授权";
                case MqttClientConnectResultCode.BadUserNameOrPassword:
                    return "账号或密码错误";
                case MqttClientConnectResultCode.ClientIdentifierNotValid:
                    return "ClientId 被拒绝";
                default:
                    return failed.ResultCode.ToString();
            }
        }

        return exception.GetBaseException().Message;
    }

    private string CredentialHint()
    {
        if (string.IsNullOrWhiteSpace(_user))
        {
            return "（未发送账号密码，按匿名连接）";
        }

        if (string.IsNullOrEmpty(_password))
        {
            return "（已发送账号 " + _user.Trim() + "，密码为空）";
        }

        return "（已发送账号 " + _user.Trim() + " 和密码）";
    }

    private string BuildBrokerLabel()
    {
        return _useWebSocket ? $"{_prefix}{_host}:{_port}{_path}" : $"tcp://{_host}:{_port}";
    }

    private List<string> GetConfiguredTopics()
    {
        var config = AssetDatabase.LoadAssetAtPath<URLBaseConfig>(UrlBaseConfigPath);
        var topics = new List<string>();
        if (config?.data?.MqttTopics != null)
        {
            foreach (var item in config.data.MqttTopics)
            {
                item?.CollectSubscribeTopics(topics);
            }
        }

        if (topics.Count == 0)
        {
            topics.AddRange(FallbackTopics);
        }

        return topics;
    }

    private void TryLoadFromConfig(bool notify)
    {
        var config = AssetDatabase.LoadAssetAtPath<URLBaseConfig>(UrlBaseConfigPath);
        if (config?.data == null)
        {
            if (notify)
            {
                EditorUtility.DisplayDialog("MQTT 测试客户端", "未找到 URLBaseConfig。", "确定");
            }

            return;
        }

        var data = config.data;
        if (!string.IsNullOrEmpty(data.Prefix))
        {
            _prefix = data.Prefix;
        }

        if (!string.IsNullOrEmpty(data.IP))
        {
            _host = data.IP;
        }

        if (data.Port > 0)
        {
            _port = data.Port;
        }

        if (!string.IsNullOrEmpty(data.Path))
        {
            _path = data.Path;
        }

        _useWebSocket = !string.IsNullOrEmpty(_prefix) &&
                        _prefix.StartsWith("ws", StringComparison.OrdinalIgnoreCase);
        SavePrefs();
        if (notify)
        {
            ShowNotification(new GUIContent("已从 URLBaseConfig 读取: " + BuildBrokerLabel()));
        }
    }

    private void FillSample()
    {
        _pubTopic = "CSJ/CC_RC/CSJ_CC_1001";
        _pubPayload =
            "{\n  \"DeviceCode\": \"CSJ_CC_1001\",\n  \"DeviceName\": \"CSJ_CC_1001#一层传输机\",\n  \"TopicType\": \"CC/RC\",\n  \"ReadTime\": \"" +
            DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff") +
            "\",\n  \"Points\": [\n    {\"PointCode\": \"ConvFwdStatus\", \"PointName\": \"输送正转状态\", \"Value\": \"true\", \"DataType\": \"Bool\"},\n    {\"PointCode\": \"ConvRevStatus\", \"PointName\": \"输送反转状态\", \"Value\": \"false\", \"DataType\": \"Bool\"},\n    {\"PointCode\": \"NoPallet\", \"PointName\": \"输送无托盘状态\", \"Value\": \"false\", \"DataType\": \"Bool\"}\n  ]\n}";
    }

    private void EnsureStyles()
    {
        if (_okStyle != null)
        {
            return;
        }

        _okStyle = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = new Color(0.35f, 0.8f, 0.4f) } };
        _failStyle = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = new Color(1f, 0.4f, 0.35f) } };
        _warnStyle = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = new Color(0.95f, 0.75f, 0.2f) } };
        _mutedStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
        _headerStyle = new GUIStyle(EditorStyles.boldLabel);
    }

    private void LoadPrefs()
    {
        _prefix = EditorPrefs.GetString(PrefPrefix + "Prefix", _prefix);
        _host = EditorPrefs.GetString(PrefPrefix + "Host", _host);
        _port = EditorPrefs.GetInt(PrefPrefix + "Port", _port);
        _path = EditorPrefs.GetString(PrefPrefix + "Path", _path);
        _user = EditorPrefs.GetString(PrefPrefix + "User", _user);
        // 密码不写入 EditorPrefs（避免本机明文残留）
        _useWebSocket = EditorPrefs.GetBool(PrefPrefix + "UseWebSocket", _useWebSocket);
        _showTopicManager = EditorPrefs.GetBool(PrefPrefix + "ShowTopicManager", _showTopicManager);
        LoadManagedTopicsPrefs();
    }

    private void SavePrefs()
    {
        EditorPrefs.SetString(PrefPrefix + "Prefix", _prefix);
        EditorPrefs.SetString(PrefPrefix + "Host", _host);
        EditorPrefs.SetInt(PrefPrefix + "Port", _port);
        EditorPrefs.SetString(PrefPrefix + "Path", _path);
        EditorPrefs.SetString(PrefPrefix + "User", _user ?? string.Empty);
        EditorPrefs.DeleteKey(PrefPrefix + "Password");
        EditorPrefs.SetBool(PrefPrefix + "UseWebSocket", _useWebSocket);
        EditorPrefs.SetBool(PrefPrefix + "ShowTopicManager", _showTopicManager);
        SaveManagedTopicsPrefs();
    }

    private sealed class LogEntry
    {
        public DateTime ReceivedAt;
        public string Topic;
        public string Payload;
        public bool ParseOk;
        public string StatusLabel;
        public string Warning;
        public string DeviceCode;
        public string DeviceName;
        public string TopicType;
        public string ReadTime;
        public int PointCount;
        public string PointSummary;
    }

    private sealed class DeviceSnapshot
    {
        public string Key;
        public string Topic;
        public string DeviceCode;
        public string DeviceName;
        public string TopicType;
        public string ReadTime;
        public int PointCount;
        public string PointSummary;
        public bool ParseOk;
        public string Warning;
        public DateTime ReceivedAt;
    }
}
}
#endif
