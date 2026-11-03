using System;
using System.Collections.Generic;
using NaughtyAttributes;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using NonsensicalKit.DigitalTwin.Motion;
using UnityEngine;

/// <summary>
/// 将 receivedMQTTData 解析为 MotionPartUpdate / 设备专用事件，驱动数字孪生。
/// </summary>

namespace Frame.Core.Mqtt
{
public class MqttDataBridge : NonsensicalMono
{
    [SerializeField] private bool m_log;
    [SerializeField] private bool m_feedDataHub = true;

    private DataHub _dataHub;

    private void Awake()
    {
        // 走 NonsensicalMono.Subscribe，销毁时自动退订。
        Subscribe<string, string>("receivedMQTTData", OnMqttReceived);
    }

    private void Start()
    {
        if (m_feedDataHub)
        {
            ServiceCore.SafeGet<DataHub>(hub => _dataHub = hub);
        }
    }

    private void OnMqttReceived(string topic, string json)
    {
        if (IsAlarmTopic(topic))
            return;

        if (!MqttPayloadParser.TryParse(json, out var payload))
        {
            if (m_log)
            {
                Debug.LogWarning($"[MqttDataBridge] 无法解析报文 topic={topic}");
            }

            return;
        }

        if (payload.TopicType == "设备报警" || payload.TopicType == "全场报警汇总")
            return;

        if (m_log)
        {
            Debug.Log($"[MqttDataBridge] {payload.DeviceCode} ({payload.TopicType})");
        }

        if (MqttPayloadParser.IsConveyorTopicType(payload.TopicType))
        {
            var motionPoints = MqttPayloadParser.ToMotionPoints(payload);
            if (motionPoints.Count > 0)
            {
                PublishWithID("MotionPartUpdate", payload.DeviceCode, motionPoints);
            }
        }
        else if (MqttPayloadParser.IsStackerTopicType(payload.TopicType))
        {
            var motionPoints = MqttPayloadParser.ToMotionPoints(payload);
            if (motionPoints.Count > 0)
            {
                PublishWithID("MotionPartUpdate", payload.DeviceCode, motionPoints);
            }

            PublishWithID("MqttStackerUpdate", payload.DeviceCode, payload);
        }
        else if (MqttPayloadParser.IsLifterTopicType(payload.TopicType))
        {
            var motionPoints = MqttPayloadParser.ToMotionPoints(payload);
            if (motionPoints.Count > 0)
            {
                PublishWithID("MotionPartUpdate", payload.DeviceCode, motionPoints);
            }

            PublishWithID("MqttLifterUpdate", payload.DeviceCode, payload);
        }
        else if (MqttPayloadParser.IsShaperTopicType(payload.TopicType)
                 || MqttPayloadParser.IsFilmWrapTopicType(payload.TopicType))
        {
            var motionPoints = MqttPayloadParser.ToMotionPoints(payload);
            if (motionPoints.Count > 0)
            {
                PublishWithID("MotionPartUpdate", payload.DeviceCode, motionPoints);
            }
        }

        if (_dataHub != null)
        {
            _dataHub.ReceivePoints(MqttPayloadParser.ToDataHubPoints(payload));
        }
    }

    private static bool IsAlarmTopic(string topic)
    {
        if (string.IsNullOrEmpty(topic))
            return false;
        if (topic == "ALARM/ALL" || topic.EndsWith("/ALARM/ALL", StringComparison.Ordinal))
            return true;
        return topic.StartsWith("ALARM/", StringComparison.Ordinal) || topic.Contains("/ALARM/");
    }

    [Button("模拟单条报文")]
    private void SimulateSample()
    {
        const string sample =
            "{\"DeviceCode\":\"CSJ_CC_1001\",\"DeviceName\":\"CSJ_CC_1001#一层传输机\",\"TopicType\":\"CC/RC\",\"ReadTime\":\"2026-09-01T10:00:00.000\",\"Points\":[{\"PointCode\":\"ConvFwdStatus\",\"PointName\":\"输送正转状态\",\"Value\":\"true\",\"DataType\":\"Bool\"},{\"PointCode\":\"ConvRevStatus\",\"PointName\":\"输送反转状态\",\"Value\":\"false\",\"DataType\":\"Bool\"},{\"PointCode\":\"NoPallet\",\"PointName\":\"输送无托盘状态\",\"Value\":\"false\",\"DataType\":\"Bool\"}]}";
        OnMqttReceived("CSJ/CC_RC/CSJ_CC_1001", sample);
    }
}
}
