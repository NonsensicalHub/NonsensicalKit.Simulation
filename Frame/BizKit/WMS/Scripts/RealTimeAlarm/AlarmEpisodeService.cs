using Frame.BizKit.WMS.Config;
using Frame.BizKit.WMS.RealTimeAlarm;
using Frame.Core.Config;
using NaughtyAttributes;
using Newtonsoft.Json;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Core.Service.Config;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Frame.RealTimeAlarm
{
    /// <summary>
    /// 在线报警记录。挂在始终激活的物体上。默认只收 ALARM/ALL；取消勾选后只收单设备报警，供测试。离线时不往列表发布。
    /// </summary>
    public class AlarmEpisodeService : NonsensicalMono
    {
        public static AlarmEpisodeService Instance { get; private set; }
        [InfoBox("在线模式报警记录接收服务")]

        [SerializeField, Tooltip("已恢复卡片保留秒数。开发阶段可调小；正式环境在这里写死。")]
        private int m_resolvedKeepSeconds = 600;

        [SerializeField, Tooltip("勾选只接收 ALARM/ALL。取消只接收 ALARM/设备编码，供测试。两路互斥，正式环境保持勾选。")]
        private bool m_useAggregateTopic = true;

        private static readonly JsonSerializerSettings JsonSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        private AlarmEpisodeStore _store;
        private bool _publishing;
        private bool _handlerRegistered;
        private readonly List<string> _historyTitles = new()
        {
            "报警时间",
            "处理时间",
            "故障代码",
            "故障简述",
            "故障位置",
            "状态"
        };
        private readonly MqttAlarmAggregatePayload _devicePack = new()
        {
            Devices = new List<MqttAlarmDevicePayload>(1)
        };

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if(PlatformInfo.IsEditor==false)
                m_useAggregateTopic = true;
            
            
            if (m_resolvedKeepSeconds < 1)
                m_resolvedKeepSeconds = 600;

            Subscribe<string, string>("receivedMQTTData", OnMqttReceived);
            TwinDataSource.ModeChanged += OnModeChanged;
            ServiceCore.SafeGet<ConfigService>(OnConfigReady);
            StartCoroutine(PurgeLoop());
            
        }

        private void OnConfigReady(ConfigService config)
        {
            TwinDataSource.TryInit(config, this);

            var stackerCatalog = new AlarmCategoryCatalog();
            if (config != null && config.TryGetConfig<StackerAlarmCategoryData>(out var stackerData) && stackerData != null)
                stackerCatalog.Bind(stackerData.Points);
            else
                Debug.LogWarning("[AlarmEpisodeService] 未读到堆垛机报警分类，堆垛机报警点将各自成卡。", this);

            var lifterCatalog = new AlarmCategoryCatalog();
            if (config != null && config.TryGetConfig<LifterAlarmCategoryData>(out var lifterData) && lifterData != null)
                lifterCatalog.Bind(lifterData.Points);
            else
                Debug.LogWarning("[AlarmEpisodeService] 未读到提升机报警分类，提升机报警点将各自成卡。", this);

            _store = new AlarmEpisodeStore(stackerCatalog, lifterCatalog);
            SyncHandler();
            if (TwinDataSource.IsOnline)
                PublishView();
        }

        protected override void OnDestroy()
        {
            TwinDataSource.ModeChanged -= OnModeChanged;
            if (_handlerRegistered)
            {
                RemoveHandler<List<RealTimeAlarmInfo>>(RealTimeAlarmInfo.SnapshotHandler, CopySnapshot);
                RemoveHandler<int, int, (List<List<string>>, int)>(RealTimeAlarmInfo.HistoryListHandler, GetHistoryAlarmList);
                _handlerRegistered = false;
            }

            if (Instance == this)
                Instance = null;
            base.OnDestroy();
        }

        public List<RealTimeAlarmInfo> CopySnapshot()
        {
            return _store == null ? new List<RealTimeAlarmInfo>() : _store.CopySnapshot();
        }

        private void OnModeChanged(TwinDataSourceMode mode)
        {
            SyncHandler();
            if (mode == TwinDataSourceMode.Online)
                PublishView();
        }

        private void SyncHandler()
        {
            bool shouldRegister = TwinDataSource.IsOnline && _store != null;
            if (shouldRegister == _handlerRegistered)
                return;

            if (shouldRegister)
            {
                AddHandler<List<RealTimeAlarmInfo>>(RealTimeAlarmInfo.SnapshotHandler, CopySnapshot);
                AddHandler<int, int, (List<List<string>>, int)>(RealTimeAlarmInfo.HistoryListHandler, GetHistoryAlarmList);
            }
            else
            {
                RemoveHandler<List<RealTimeAlarmInfo>>(RealTimeAlarmInfo.SnapshotHandler, CopySnapshot);
                RemoveHandler<int, int, (List<List<string>>, int)>(RealTimeAlarmInfo.HistoryListHandler, GetHistoryAlarmList);
            }

            _handlerRegistered = shouldRegister;
        }

        /// <summary>
        /// 在线历史报警分页。当前用 MQTT 卡片快照；后台独立历史接口接入后替换本方法即可。
        /// </summary>
        private (List<List<string>>, int) GetHistoryAlarmList(int page, int pageSize)
        {
            if (_store == null)
                return (new List<List<string>> { new(_historyTitles) }, 0);
            return _store.CopyHistoryPage(page, pageSize, _historyTitles);
        }

        private void OnMqttReceived(string topic, string json)
        {
            if (string.IsNullOrEmpty(json) || _store == null)
                return;

            bool changed;
            if (m_useAggregateTopic)
            {
                if (!IsAggregateTopic(topic))
                    return;
                if (!TryParseAggregate(json, out MqttAlarmAggregatePayload payload))
                    return;
                changed = _store.Apply(payload, true);
            }
            else
            {
                if (!IsDeviceAlarmTopic(topic))
                    return;
                if (!TryParseDevice(json, out MqttAlarmDevicePayload device))
                    return;

                _devicePack.PublishTime = device.ReadTime;
                _devicePack.Devices.Clear();
                _devicePack.Devices.Add(device);
                changed = _store.Apply(_devicePack, false);
            }

            if (changed)
                PublishView();
        }

        private static bool TryParseAggregate(string json, out MqttAlarmAggregatePayload payload)
        {
            try
            {
                payload = JsonConvert.DeserializeObject<MqttAlarmAggregatePayload>(json, JsonSettings);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AlarmEpisodeService] 汇总报文解析失败: {e.Message}");
                payload = null;
                return false;
            }

            return payload != null
                   && (string.IsNullOrEmpty(payload.TopicType) || payload.TopicType == "全场报警汇总");
        }

        private static bool TryParseDevice(string json, out MqttAlarmDevicePayload device)
        {
            try
            {
                device = JsonConvert.DeserializeObject<MqttAlarmDevicePayload>(json, JsonSettings);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AlarmEpisodeService] 设备报警报文解析失败: {e.Message}");
                device = null;
                return false;
            }

            return device != null
                   && !string.IsNullOrEmpty(device.DeviceCode)
                   && (string.IsNullOrEmpty(device.TopicType) || device.TopicType == "设备报警");
        }

        private IEnumerator PurgeLoop()
        {
            var wait = new WaitForSeconds(300f);
            while (true)
            {
                yield return wait;
                if (!TwinDataSource.IsOnline || _store == null)
                    continue;
                if (_store.PurgeResolved(DateTime.Now, m_resolvedKeepSeconds) > 0)
                    PublishView();
            }
        }

        private void PublishView()
        {
            if (!TwinDataSource.IsOnline || _store == null || _publishing)
                return;

            _publishing = true;
            try
            {
                Publish("refreshRealTimeAlarmTable", _store.CopySnapshot());
            }
            finally
            {
                _publishing = false;
            }
        }

        private static bool IsAggregateTopic(string topic)
        {
            return !string.IsNullOrEmpty(topic)
                   && (topic == "ALARM/ALL" || topic.EndsWith("/ALARM/ALL", StringComparison.Ordinal));
        }

        private static bool IsDeviceAlarmTopic(string topic)
        {
            if (string.IsNullOrEmpty(topic) || IsAggregateTopic(topic))
                return false;

            const string marker = "ALARM/";
            int index = topic.LastIndexOf(marker, StringComparison.Ordinal);
            if (index < 0 || (index > 0 && topic[index - 1] != '/'))
                return false;

            int codeStart = index + marker.Length;
            return codeStart < topic.Length && topic.IndexOf('/', codeStart) < 0;
        }
    }
}
