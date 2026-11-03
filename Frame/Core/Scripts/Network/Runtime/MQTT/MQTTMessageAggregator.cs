using System.Collections;
using System.Collections.Concurrent;
using NaughtyAttributes;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Core.Service.Config;
using UnityEngine;
using Frame.Core.Config;
using Frame.Core.RecordPlayback;
using Frame.Core.Network;

/// <summary>
/// 订阅 URLBaseConfig 中的 MQTT 主题，并将消息分发到 receivedMQTTData 事件总线。
/// 同时监听 IOCC 事件 MQTTMessage（实况 WebGL / 离线 RecordDataPlayer 均可注入）。
/// 可通过 MQTTUseOfflineData 动态切换：离线时忽略实况，仅处理总线注入的回放数据。
/// </summary>

namespace Frame.Core.Mqtt
{
public class MQTTMessageAggregator : NonsensicalMono
{
    public const string UseOfflineDataEvent = FrameMqttEvents.UseOfflineData;

    [SerializeField] private bool m_autoSubscribe = true;
    [SerializeField] private bool m_log;

    /// <summary>运行时由 RecordDataPlayer 自动切换；默认在线。</summary>
    [SerializeField] private bool m_useOfflineData;

    private MQTTManager _manager;
    private readonly ConcurrentQueue<(string topic, string message)> _queue = new();
    private bool _subscribeStarted;

    public bool UseOfflineData => m_useOfflineData;

    private void Awake()
    {
        Subscribe<string, string>("MQTTMessage", OnMqttMessage);
        Subscribe<bool>(UseOfflineDataEvent, SetUseOfflineData);
    }

    protected override void OnDestroy()
    {
        if (_manager != null)
        {
            _manager.MessageReceived -= OnLiveMqttMessage;
            _manager = null;
        }

        base.OnDestroy();
    }

    private void Start()
    {
        // 在主线程启动协程；不要在 SafeGet 回调里 StartCoroutine（MQTT 就绪可能在后台线程触发）。
        if (m_autoSubscribe)
        {
            StartCoroutine(WaitAndSubscribe());
        }
        else
        {
            ServiceCore.SafeGet<MQTTManager>(OnGetManager);
        }
    }

    /// <summary>
    /// 切换离线/在线数据源。true=忽略实况 MQTT，仅消费回放注入的 MQTTMessage。
    /// </summary>
    public void SetUseOfflineData(bool useOffline)
    {
        if (m_useOfflineData == useOffline)
        {
            return;
        }

        m_useOfflineData = useOffline;
        if (useOffline)
        {
            ClearQueue();
        }

        if (m_log)
        {
            Debug.Log($"[MQTTMessageAggregator] 数据源 → {(useOffline ? "离线回放" : "在线 MQTT")}");
        }
    }

    private void OnGetManager(MQTTManager manager)
    {
        BindManager(manager);
    }

    private void BindManager(MQTTManager manager)
    {
        if (manager == null || _manager == manager)
        {
            return;
        }

        if (_manager != null)
        {
            _manager.MessageReceived -= OnLiveMqttMessage;
        }

        _manager = manager;
        _manager.MessageReceived += OnLiveMqttMessage;
    }

    private IEnumerator WaitAndSubscribe()
    {
        MQTTManager manager = null;
        bool gotManager = false;
        ServiceCore.SafeGet<MQTTManager>(m =>
        {
            manager = m;
            gotManager = true;
        });

        yield return new WaitUntil(() => gotManager && manager != null);
        BindManager(manager);

        yield return new WaitUntil(() => ServiceCore.TryGet<ConfigService>(out var cfg) && cfg.IsReady);
        yield return new WaitUntil(() => URLS.IsReady);
        yield return new WaitUntil(() => _manager != null && _manager.IsReady && _manager.Status == MQTTStatus.Connected);

        if (_subscribeStarted)
        {
            yield break;
        }

        _subscribeStarted = true;
        SubscribeTopics();
    }

    [Button("订阅全部主题")]
    public void SubscribeTopics()
    {
        if (_manager == null || URLS.MQTTTopics == null || URLS.MQTTTopics.Count == 0)
        {
            Debug.LogWarning("[MQTTMessageAggregator] 无可用 MQTT 主题，请先从 Excel 导入 URLBaseConfig。");
            return;
        }

        _manager.SubscribeAsync(URLS.MQTTTopics.ToArray());
        Debug.Log($"[MQTTMessageAggregator] 已订阅 {URLS.MQTTTopics.Count} 个通配主题。");
    }

    private void Update()
    {
        while (_queue.TryDequeue(out var item))
        {
            PublishReceived(item.topic, item.message);
        }
    }

    private void OnLiveMqttMessage(string topic, string message)
    {
        if (m_useOfflineData)
        {
            return;
        }

        Enqueue(topic, message);
    }

    private void OnMqttMessage(string topic, string message)
    {
        // 离线回放：RecordDataPlayer 经 IOCC MQTTMessage 注入，只走总线。
        // 在线实况：统一走 MQTTManager.MessageReceived。
        // WebGL 下 Manager 会订阅同一总线再 Invoke MessageReceived，若这里再入队会双投递。
        if (!m_useOfflineData)
        {
            return;
        }

        Enqueue(topic, message);
    }

    private void Enqueue(string topic, string message)
    {
        _queue.Enqueue((topic, message));
        if (m_log)
        {
            Debug.Log($"[MQTTMessageAggregator] {topic}\n{message}");
        }
    }

    private void ClearQueue()
    {
        while (_queue.TryDequeue(out _))
        {
        }
    }

    private static void PublishReceived(string topic, string message)
    {
        IOCC.Publish("receivedMQTTData", topic, message);
        IOCC.PublishWithID("receivedMQTTData", topic, message);
        IOCC.PublishWithID("receivedMQTTData", topic, topic, message);
    }
}
}
