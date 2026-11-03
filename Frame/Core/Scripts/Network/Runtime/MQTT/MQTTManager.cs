using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NaughtyAttributes;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Core.Service.Config;
using UnityEngine;
using Frame.Core.Network;
using Frame.Core.Config;
#if UNITY_EDITOR || !UNITY_WEBGL
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
#endif
#if UNITY_WEBGL
using NonsensicalKit.WebGL;
#endif

/// <summary>
/// MQTT 连接管理：从 URLBaseConfig 读取 Broker 参数，支持 PC(WebSocket) 与 WebGL。
/// 注意：m_mqttPassword / URLBase Authorization 仅供运行时配置，生产密钥勿打进包默认资产。
/// </summary>

namespace Frame.Core.Mqtt
{
[ServicePrefab("Services/MQTTManager")]
public class MQTTManager : NonsensicalMono, IMonoService
{
    [SerializeField, BoxGroup("MQTT链接")] private string m_mqttPrefix = "ws://";
    [SerializeField, BoxGroup("MQTT链接")] private string m_mqttUri = "localhost";
    [SerializeField, BoxGroup("MQTT链接")] private int m_mqttPort = 8083;
    [SerializeField, BoxGroup("MQTT链接")] private string m_mqttSuffix = "/mqtt";
    [SerializeField, BoxGroup("MQTT链接")] private bool m_isWebSocketConnectionType = true;
    [SerializeField, BoxGroup("MQTT链接")] private string m_mqttUser = "";
    /// <summary>Broker 密码（序列化字段）。生产环境请用本地覆写，勿提交真实密钥到包默认资源。</summary>
    [SerializeField, BoxGroup("MQTT链接")] private string m_mqttPassword = "";

    [SerializeField] private bool m_connect = true;
    [SerializeField] private bool m_log;
    [SerializeField] private bool m_recordTopic = true;
    [SerializeField, Label("重连间隔时间(s)")] private float m_reconnectGapTime = 10f;
    [SerializeField] private bool m_useTls;

    [SerializeField, ShowIf("m_recordTopic"), ReadOnly, Label("已订阅的主题")]
    private List<string> _subscribeTopics = new();

    [SerializeField, Label("MQTT链接状态"), ReadOnly]
    private MQTTStatus _status;

#if UNITY_EDITOR || !UNITY_WEBGL
    private IMqttClient _client;
    private MqttClientOptions _clientOptions;
#endif
    private float _waitTime;
    private string _clientId;
    private int _connectGate; // 0=idle, 1=busy
    private bool _webMqttSubscribed;

    /// <summary>后台线程投递到主线程执行的动作（就绪回调、日志、收包派发等）。</summary>
    private readonly ConcurrentQueue<Action> _mainThreadActions = new();

    public Action<string, string> MessageReceived;
    public bool IsReady { get; private set; }
    public Action InitCompleted { get; set; }
    public MQTTStatus Status => _status;

    private void Awake()
    {
        IsReady = false;
        ServiceCore.SafeGet<ConfigService>(ApplyUrlBaseConfig);
    }

    private void ApplyUrlBaseConfig(ConfigService configService)
    {
        if (configService.TryGetConfig<URLBaseData>(out var data))
        {
            if (!string.IsNullOrEmpty(data.Prefix))
            {
                m_mqttPrefix = data.Prefix;
            }

            if (!string.IsNullOrEmpty(data.IP))
            {
                m_mqttUri = data.IP;
            }

            if (data.Port > 0)
            {
                m_mqttPort = data.Port;
            }

            if (!string.IsNullOrEmpty(data.Path))
            {
                m_mqttSuffix = data.Path;
            }

            m_isWebSocketConnectionType = m_mqttPrefix.StartsWith("ws", StringComparison.OrdinalIgnoreCase);

            if (data.MqttTopics != null && data.MqttTopics.Count > 0)
            {
                URLS.Init(data);
            }
        }

        Init();
    }

    private void Init()
    {
        if (!m_connect)
        {
            return;
        }

        _clientId = Guid.NewGuid().ToString();
        if (PlatformInfo.IsWindow || PlatformInfo.IsEditor)
        {
#if UNITY_EDITOR || !UNITY_WEBGL
            // 网络连接可在线程池；IsReady / InitCompleted / MessageReceived 必须回主线程。
            TryStartPcConnect(InitPcMqtt);
#endif
        }
        else if (PlatformInfo.IsWebGL)
        {
#if UNITY_WEBGL
            Subscribe<string[]>("WebBridge", "MQTTInitCompleted", _ => InitWebMqtt(null));
#endif
        }
    }

    private void Update()
    {
        while (_mainThreadActions.TryDequeue(out var action))
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

#if UNITY_EDITOR || !UNITY_WEBGL
        if (_client != null && _status == MQTTStatus.ConnectFailed)
        {
            _waitTime += Time.deltaTime;
            if (_waitTime > m_reconnectGapTime)
            {
                _waitTime = 0f;
                Reconnect();
            }
        }
#endif
    }

    private void RunOnMainThread(Action action)
    {
        if (action == null)
        {
            return;
        }

        _mainThreadActions.Enqueue(action);
    }

    protected override void OnDestroy()
    {
#if UNITY_WEBGL
        if (_webMqttSubscribed)
        {
            IOCC.Unsubscribe<string, string>("MQTTMessage", OnWebMqttMessageReceived);
            IOCC.Unsubscribe<string, string>("MQTTConnectSuccess", OnWebMqttConnectSuccess);
            _webMqttSubscribed = false;
        }
#endif
#if UNITY_EDITOR || !UNITY_WEBGL
        try
        {
            if (_client != null)
            {
                _client.ConnectedAsync -= ClientConnectedAsync;
                _client.DisconnectedAsync -= ClientDisconnectedAsync;
                _client.ApplicationMessageReceivedAsync -= ClientApplicationMessageReceivedAsync;
                _client.Dispose();
                _client = null;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MQTTManager] Dispose: {e.Message}");
        }
#endif
        base.OnDestroy();
    }

    private void OnApplicationQuit()
    {
#if UNITY_EDITOR || !UNITY_WEBGL
        _client?.Dispose();
#elif UNITY_WEBGL
        if (PlatformInfo.IsWebGL)
        {
            WebMQTT.Instance?.CloseAll();
        }
#endif
    }

#if UNITY_EDITOR || !UNITY_WEBGL
    private void TryStartPcConnect(Action worker)
    {
        if (Interlocked.CompareExchange(ref _connectGate, 1, 0) != 0)
        {
            if (m_log)
            {
                RunOnMainThread(() => Debug.Log("[MQTTManager] 连接/重连已在进行，跳过。"));
            }

            return;
        }

        Task.Run(() =>
        {
            try
            {
                worker();
            }
            finally
            {
                Interlocked.Exchange(ref _connectGate, 0);
            }
        });
    }

    private void InitPcMqtt()
    {
        try
        {
            var builder = new MqttClientOptionsBuilder()
                .WithCredentials(m_mqttUser, m_mqttPassword)
                .WithClientId(_clientId)
                .WithCleanSession()
                .WithTlsOptions(new MqttClientTlsOptions { UseTls = m_useTls });

            if (m_isWebSocketConnectionType)
            {
                builder.WithWebSocketServer(o => o.WithUri($"{m_mqttPrefix}{m_mqttUri}:{m_mqttPort}{m_mqttSuffix}"));
            }
            else
            {
                builder.WithTcpServer(m_mqttUri, m_mqttPort);
            }

            string endpoint = $"{m_mqttPrefix}{m_mqttUri}:{m_mqttPort}{m_mqttSuffix}";
            if (m_log)
            {
                RunOnMainThread(() => Debug.Log($"[MQTTManager] 连接 {endpoint}"));
            }

            _clientOptions = builder.Build();
            if (_client != null)
            {
                try
                {
                    _client.ConnectedAsync -= ClientConnectedAsync;
                    _client.DisconnectedAsync -= ClientDisconnectedAsync;
                    _client.ApplicationMessageReceivedAsync -= ClientApplicationMessageReceivedAsync;
                    _client.Dispose();
                }
                catch
                {
                    // ignore dispose races
                }
            }

            _client = new MqttFactory().CreateMqttClient();
            _client.ConnectedAsync += ClientConnectedAsync;
            _client.DisconnectedAsync += ClientDisconnectedAsync;
            _client.ApplicationMessageReceivedAsync += ClientApplicationMessageReceivedAsync;

            _status = MQTTStatus.Connecting;
            _client.ConnectAsync(_clientOptions).GetAwaiter().GetResult();

            RunOnMainThread(MarkReady);
        }
        catch (Exception e)
        {
            _status = MQTTStatus.ConnectFailed;
            RunOnMainThread(() => Debug.LogError($"[MQTTManager] 初始化失败: {e.Message}\n{e}"));
        }
    }

    private void MarkReady()
    {
        if (IsReady)
        {
            return;
        }

        IOCC.Set(this);
        IsReady = true;
        InitCompleted?.Invoke();
    }

    private Task ClientApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs arg)
    {
        string payload = ReadPayload(arg.ApplicationMessage);
        string topic = arg.ApplicationMessage.Topic;
        if (m_log)
        {
            RunOnMainThread(() => Debug.Log($"[MQTTManager] 收到 {topic}: {payload}"));
        }

        // 必须回主线程再派发，避免订阅方直接碰 Unity API。
        RunOnMainThread(() => MessageReceived?.Invoke(topic, payload));
        return Task.CompletedTask;
    }

    private static string ReadPayload(MqttApplicationMessage message)
    {
        if (message == null)
        {
            return string.Empty;
        }

        var segment = message.PayloadSegment;
        if (segment.Array == null || segment.Count <= 0)
        {
            return string.Empty;
        }

        return Encoding.UTF8.GetString(segment.Array, segment.Offset, segment.Count);
    }

    private Task ClientDisconnectedAsync(MqttClientDisconnectedEventArgs arg)
    {
        _status = MQTTStatus.ConnectFailed;
        if (m_log)
        {
            RunOnMainThread(() => Debug.LogWarning($"[MQTTManager] 已断开: {arg?.Reason}"));
        }

        return Task.CompletedTask;
    }

    private Task ClientConnectedAsync(MqttClientConnectedEventArgs arg)
    {
        _status = MQTTStatus.Connected;
        if (m_log)
        {
            RunOnMainThread(() => Debug.Log("[MQTTManager] MQTT 已连接"));
        }

        RunOnMainThread(MarkReady);
        RunOnMainThread(ResubscribeRecordedTopics);
        return Task.CompletedTask;
    }

    private void ResubscribeRecordedTopics()
    {
        if (_client == null || _status != MQTTStatus.Connected || _subscribeTopics.Count == 0)
        {
            return;
        }

        // CleanSession 重连必须强制向 Broker 再订；不能走对外幂等跳过逻辑。
        var topicFilters = new MqttClientSubscribeOptions
        {
            TopicFilters = _subscribeTopics.Select(topic => new MqttTopicFilterBuilder()
                    .WithTopic(topic)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                    .Build())
                .ToList()
        };
        _client.SubscribeAsync(topicFilters);
    }

    private void Reconnect()
    {
        TryStartPcConnect(() =>
        {
            try
            {
                _status = MQTTStatus.Connecting;
                if (_client == null || _clientOptions == null)
                {
                    InitPcMqtt();
                    return;
                }

                if (_client.IsConnected)
                {
                    _client.ReconnectAsync().GetAwaiter().GetResult();
                }
                else
                {
                    _client.ConnectAsync(_clientOptions).GetAwaiter().GetResult();
                }
            }
            catch (Exception e)
            {
                _status = MQTTStatus.ConnectFailed;
                RunOnMainThread(() => Debug.LogWarning($"[MQTTManager] 重连失败: {e.Message}"));
            }
        });
    }

    public void SubscribeAsync(string topic, MqttQualityOfServiceLevel level = MqttQualityOfServiceLevel.AtMostOnce)
    {
        if (!IsReady || string.IsNullOrEmpty(topic))
        {
            return;
        }

        bool alreadyRecorded = m_recordTopic && _subscribeTopics.Contains(topic);
        if (_client != null && _status == MQTTStatus.Connected && !alreadyRecorded)
        {
            var topicFilter = new MqttTopicFilterBuilder()
                .WithTopic(topic)
                .WithQualityOfServiceLevel(level)
                .Build();
            _client.SubscribeAsync(topicFilter);
        }

        if (m_recordTopic && !_subscribeTopics.Contains(topic))
        {
            _subscribeTopics.Add(topic);
        }
    }

    public void SubscribeAsync(string[] topics, MqttQualityOfServiceLevel level = MqttQualityOfServiceLevel.AtMostOnce)
    {
        if (!IsReady || topics == null || topics.Length == 0)
        {
            return;
        }

        var pending = topics.Where(t => !string.IsNullOrEmpty(t) && (!_subscribeTopics.Contains(t) || !m_recordTopic)).Distinct().ToArray();
        if (_client != null && _status == MQTTStatus.Connected && pending.Length > 0)
        {
            var topicFilters = new MqttClientSubscribeOptions
            {
                TopicFilters = pending.Select(t => new MqttTopicFilterBuilder()
                        .WithTopic(t)
                        .WithQualityOfServiceLevel(level)
                        .Build())
                    .ToList()
            };
            _client.SubscribeAsync(topicFilters);
        }

        if (!m_recordTopic)
        {
            return;
        }

        foreach (var topic in topics)
        {
            if (!string.IsNullOrEmpty(topic) && !_subscribeTopics.Contains(topic))
            {
                _subscribeTopics.Add(topic);
            }
        }
    }

    public void PublishAsync(string topic, string message, MqttQualityOfServiceLevel level = MqttQualityOfServiceLevel.AtLeastOnce)
    {
        if (!IsReady || _client == null)
        {
            return;
        }

        var mqttMessage = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(message)
            .WithQualityOfServiceLevel(level)
            .WithRetainFlag(false)
            .Build();
        _client.PublishAsync(mqttMessage);
    }
#else
    public void SubscribeAsync(string topic)
    {
        if (!IsReady || string.IsNullOrEmpty(topic))
        {
            return;
        }

        if (!(m_recordTopic && _subscribeTopics.Contains(topic)))
        {
            WebMQTT.Instance.Subscribe(BuildWebUrl(), topic);
        }

        if (m_recordTopic && !_subscribeTopics.Contains(topic))
        {
            _subscribeTopics.Add(topic);
        }
    }

    public void SubscribeAsync(string[] topics)
    {
        if (!IsReady || topics == null)
        {
            return;
        }

        foreach (var topic in topics)
        {
            SubscribeAsync(topic);
        }
    }

    public void PublishAsync(string topic, string message)
    {
        if (!IsReady)
        {
            return;
        }

        WebMQTT.Instance.SendMessage(BuildWebUrl(), topic, message);
    }
#endif

#if UNITY_WEBGL
    private void InitWebMqtt(string[] _)
    {
        string url = BuildWebUrl();
        WebMQTT.Instance.Connect(url, m_mqttUser, m_mqttPassword);
        if (!_webMqttSubscribed)
        {
            IOCC.Subscribe<string, string>("MQTTMessage", OnWebMqttMessageReceived);
            IOCC.Subscribe<string, string>("MQTTConnectSuccess", OnWebMqttConnectSuccess);
            _webMqttSubscribed = true;
        }
    }

    private string BuildWebUrl() => $"{m_mqttPrefix}{m_mqttUri}:{m_mqttPort}{m_mqttSuffix}";

    private void OnWebMqttMessageReceived(string topic, string message)
    {
        if (m_log)
        {
            Debug.Log($"[MQTTManager] WebGL 收到 {topic}: {message}");
        }

        MessageReceived?.Invoke(topic, message);
    }

    private void OnWebMqttConnectSuccess(string url, string clientId)
    {
        if (!string.Equals(url, BuildWebUrl(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _status = MQTTStatus.Connected;
        IOCC.Set(this);
        IsReady = true;
        InitCompleted?.Invoke();
        if (m_log)
        {
            Debug.Log("[MQTTManager] WebGL MQTT 已连接");
        }
    }
#endif

    public List<string> ShowSubscribedTopics() => _subscribeTopics;
}

public enum MQTTStatus
{
    Empty = 0,
    Connecting = 1,
    Connected = 2,
    ConnectFailed = 3,
}
}
