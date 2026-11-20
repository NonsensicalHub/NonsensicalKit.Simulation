using Frame.Core.Mqtt;
using Frame.BizKit.WMS.Hikvision;
using HikvisionAgv;
using NonsensicalKit.Core;
using System.Collections.Generic;
using UnityEngine;

namespace EquipmentStatus
{
/// <summary>
/// 只负责采集：订阅 AGV / MQTT 事件写入 <see cref="EquipmentStatusCache"/>。
/// 不组装面板、不注册 IOCC Handler。
/// </summary>
public class EquipmentStatusCollector : NonsensicalMono
{
    [SerializeField] private bool m_log;
    [SerializeField] private bool m_requestTaskOnQuery = true;

    private HikvisionTaskStatusRequest _taskRequest;
    private readonly Dictionary<string, float> _lastTaskRequestAt = new();

    public EquipmentStatusCache Cache => EquipmentStatusCache.Shared;

    private void Awake()
    {
        Subscribe<string, List<HikAgvStatusDto>>(HikvisionAgvStatusRequest.EventKey, OnAgvStatusRaw);
        Subscribe<string, List<HikTaskStatusDto>>(HikvisionTaskStatusRequest.EventKey, OnTaskStatusRaw);
        IOCC.Subscribe<string, string>("receivedMQTTData", OnMqttReceived);
        _taskRequest = FindFirstObjectByType<HikvisionTaskStatusRequest>();
    }

    protected override void OnDestroy()
    {
        IOCC.Unsubscribe<string, string>("receivedMQTTData", OnMqttReceived);
        base.OnDestroy();
    }

    /// <summary>面板查询 AGV 时补拉任务；同车号节流 1 秒。</summary>
    public void RequestAgvTaskIfNeeded(string deviceCode)
    {
        if (!m_requestTaskOnQuery || string.IsNullOrEmpty(deviceCode))
            return;

        float now = Time.realtimeSinceStartup;
        if (_lastTaskRequestAt.TryGetValue(deviceCode, out float last) && now - last < 1f)
            return;

        if (_taskRequest == null)
            _taskRequest = FindFirstObjectByType<HikvisionTaskStatusRequest>();
        if (_taskRequest == null)
            return;

        _lastTaskRequestAt[deviceCode] = now;
        _taskRequest.RequestVehicle(deviceCode);
        if (m_log)
            Debug.Log($"[EquipmentStatusCollector] 请求 AGV 任务 {deviceCode}", this);
    }

    private void OnAgvStatusRaw(string mapShortName, List<HikAgvStatusDto> list)
    {
        if (list == null || list.Count == 0)
            return;

        for (int i = 0; i < list.Count; i++)
        {
            var dto = list[i];
            if (dto == null || string.IsNullOrEmpty(dto.robotCode))
                continue;

            string code = dto.robotCode;
            Cache.SetPoint(code, HikvisionStatusPointCodes.PosX.Code, dto.posX);
            Cache.SetPoint(code, HikvisionStatusPointCodes.PosY.Code, dto.posY);
            Cache.SetPoint(code, HikvisionStatusPointCodes.RobotDir.Code, dto.robotDir);
            Cache.SetPoint(code, HikvisionStatusPointCodes.Speed.Code, dto.speed);
            Cache.SetPoint(code, HikvisionStatusPointCodes.Status.Code, dto.status);
            Cache.SetPoint(code, HikvisionStatusPointCodes.Stop.Code, dto.stop);
            Cache.SetPoint(code, HikvisionStatusPointCodes.Battery.Code, dto.battery);
            Cache.SetPoint(code, HikvisionStatusPointCodes.ExclType.Code, dto.exclType);
            Cache.SetPoint(code, HikvisionStatusPointCodes.PodCode.Code, dto.podCode);
            Cache.SetPoint(code, HikvisionStatusPointCodes.PodDir.Code, dto.podDir);
            Cache.SetPoint(code, HikvisionStatusPointCodes.MapCode.Code, dto.mapCode ?? mapShortName);
        }
    }

    private void OnTaskStatusRaw(string agvCode, List<HikTaskStatusDto> list)
    {
        if (list == null || list.Count == 0)
            return;

        var bestByAgv = new Dictionary<string, HikTaskStatusDto>();
        var bestPriority = new Dictionary<string, int>();

        for (int i = 0; i < list.Count; i++)
        {
            var dto = list[i];
            if (dto == null)
                continue;

            string code = string.IsNullOrEmpty(dto.agvCode) ? agvCode : dto.agvCode;
            if (string.IsNullOrEmpty(code))
                continue;

            int priority = HikvisionTaskStatusCodes.ActivePriority(dto.taskStatus);
            if (priority <= 0)
                continue;

            if (!bestPriority.TryGetValue(code, out int prev) || priority > prev)
            {
                bestByAgv[code] = dto;
                bestPriority[code] = priority;
            }
        }

        string requestCode = agvCode;
        if (!string.IsNullOrEmpty(requestCode) && !bestByAgv.ContainsKey(requestCode))
            WriteTaskPoints(requestCode, null);

        foreach (var kv in bestByAgv)
            WriteTaskPoints(kv.Key, kv.Value);
    }

    private void WriteTaskPoints(string code, HikTaskStatusDto dto)
    {
        if (dto == null)
        {
            Cache.SetPoint(code, HikvisionStatusPointCodes.TaskCode.Code, string.Empty);
            Cache.SetPoint(code, HikvisionStatusPointCodes.TaskStatus.Code, string.Empty);
            Cache.SetPoint(code, HikvisionStatusPointCodes.TaskTyp.Code, string.Empty);
            return;
        }

        Cache.SetPoint(code, HikvisionStatusPointCodes.TaskCode.Code, dto.taskCode);
        Cache.SetPoint(code, HikvisionStatusPointCodes.TaskStatus.Code, dto.taskStatus);
        Cache.SetPoint(code, HikvisionStatusPointCodes.TaskTyp.Code, dto.taskTyp);
    }

    private void OnMqttReceived(string topic, string json)
    {
        if (!MqttPayloadParser.TryParse(json, out var payload))
            return;

        if (!MqttPayloadParser.IsStackerTopicType(payload.TopicType) &&
            !MqttPayloadParser.IsLifterTopicType(payload.TopicType))
            return;

        string code = payload.DeviceCode;
        Cache.SetDeviceName(code, payload.DeviceName);

        var batch = new List<KeyValuePair<string, string>>(payload.Points.Count);
        for (int i = 0; i < payload.Points.Count; i++)
        {
            var p = payload.Points[i];
            if (p == null || string.IsNullOrEmpty(p.PointCode))
                continue;
            batch.Add(new KeyValuePair<string, string>(p.PointCode, p.Value ?? string.Empty));
        }

        Cache.SetPoints(code, batch);

        if (m_log)
            Debug.Log($"[EquipmentStatusCollector] MQTT {code} ({payload.TopicType}) points={batch.Count}", this);
    }
}
}
