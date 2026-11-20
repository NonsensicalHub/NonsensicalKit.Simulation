using System.Collections.Generic;
using Frame.BizKit.WMS;
using Frame.BizKit.WMS.Equipment;
using Frame.Core.Config;
using Frame.Core.Mqtt;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Core.Service.Config;
using UnityEngine;

namespace EquipmentStatus
{
/// <summary>
/// 设备管理面板「查设备详情」由谁回答，由本组件决定：
///
/// 1) Twin 数据源 = 在线：本组件回答（显示 Collector 缓存的真数据）；同时关掉框架 Mock。
/// 2) 正在用 RecordDataPlayer 回放（会发 MQTTUseOfflineData=true）：同样本组件回答。
///    注意：回放常会把 Twin 切成 Offline，若不接管，面板会落到随机 Mock，看起来像「没数据」。
/// 3) Twin = 离线，且没有在回放：本组件让出，交给框架
///    <see cref="EquipmentSimulationDataGenerator"/> 的随机 Mock（保证没挂本目录脚本时框架仍能跑）。
///
/// Inspector 两个开关（默认都勾）：
/// - overrideMockWhenOnline：在线时要不要接管（一般保持勾选）
/// - overrideMockDuringMqttPlayback：回放时要不要接管（一般保持勾选）
/// </summary>
public class EquipmentInfoQueryService : NonsensicalMono
{
    [SerializeField] private EquipmentStatusCollector m_collector;
    [SerializeField] private EquipmentSimulationDataGenerator m_simulationGenerator;

    [SerializeField, Tooltip("勾选：Twin 在线时由本组件提供设备详情，并挂起框架随机 Mock。\n取消：在线也不接管（一般不建议）。")]
    private bool m_overrideMockWhenOnline = true;

    [SerializeField, Tooltip("勾选：RecordDataPlayer 回放时由本组件提供设备详情（走缓存真点位），并挂起框架随机 Mock。\n取消：回放时面板会回到 Offline 随机数。")]
    private bool m_overrideMockDuringMqttPlayback = true;

    private bool _modeReady;
    private bool _started;
    private bool _handlerRegistered;
    private bool _mqttPlayback;
    private bool _takingOver;

    private void Awake()
    {

        m_collector ??= FindFirstObjectByType<EquipmentStatusCollector>();
        m_simulationGenerator ??= FindFirstObjectByType<EquipmentSimulationDataGenerator>();

        TwinDataSource.ModeChanged += OnModeChanged;
        IOCC.Subscribe<bool>(MQTTMessageAggregator.UseOfflineDataEvent, OnMqttPlaybackFlag);
        ServiceCore.SafeGet<ConfigService>(OnConfigReady);
    }

    private void Start()
    {
        _started = true;
        EvaluateTakeOver();
    }

    protected override void OnDestroy()
    {
        TwinDataSource.ModeChanged -= OnModeChanged;
        IOCC.Unsubscribe<bool>(MQTTMessageAggregator.UseOfflineDataEvent, OnMqttPlaybackFlag);

        if (_handlerRegistered)
            UnregisterHandler();

        // 销毁时恢复 Mock，避免留下挂起状态
        m_simulationGenerator?.SetEquipmentInfoSuspended(false);

        base.OnDestroy();
    }

    private void OnConfigReady(ConfigService config)
    {
        TwinDataSource.TryInit(config, this);
        _modeReady = true;
        EvaluateTakeOver();
    }

    private void OnModeChanged(TwinDataSourceMode _)
    {
        EvaluateTakeOver();
    }

    private void OnMqttPlaybackFlag(bool useOfflineData)
    {
        _mqttPlayback = useOfflineData;
        EvaluateTakeOver();
    }

    private void EvaluateTakeOver()
    {
        if (!_modeReady || !_started)
            return;

        bool takeOver =
            (m_overrideMockWhenOnline && TwinDataSource.IsReady && !TwinDataSource.IsOffline) ||
            (m_overrideMockDuringMqttPlayback && _mqttPlayback);

        if (takeOver == _takingOver)
        {
            // Mock 可能刚 Enable，再同步一次挂起状态
            if (takeOver)
                m_simulationGenerator?.SetEquipmentInfoSuspended(true);
            return;
        }

        _takingOver = takeOver;

        if (takeOver)
        {
            m_simulationGenerator?.SetEquipmentInfoSuspended(true);
            RegisterHandler();
        }
        else
        {
            UnregisterHandler();
            m_simulationGenerator?.SetEquipmentInfoSuspended(false);
        }

        Debug.Log(
            $"[{nameof(EquipmentInfoQueryService)}] " +
            $"takeOver={takeOver} online={!TwinDataSource.IsOffline} mqttPlayback={_mqttPlayback}",
            this);
    }

    private void RegisterHandler()
    {
        if (_handlerRegistered)
            return;

        AddHandler<Dictionary<string, string>, (SimpleInfo, Dictionary<string, string>)>(
            "GetEquipmentInfo", CreateInfo);
        _handlerRegistered = true;
    }

    private void UnregisterHandler()
    {
        if (!_handlerRegistered)
            return;

        RemoveHandler<Dictionary<string, string>, (SimpleInfo, Dictionary<string, string>)>(
            "GetEquipmentInfo", CreateInfo);
        _handlerRegistered = false;
    }

    private (SimpleInfo, Dictionary<string, string>) CreateInfo(Dictionary<string, string> baseInfo)
    {
        string deviceCode = baseInfo != null && baseInfo.TryGetValue("DeviceCode", out var code)
            ? code
            : string.Empty;
        string deviceName = baseInfo != null && baseInfo.TryGetValue("DeviceName", out var value)
            ? value
            : string.Empty;

        return EquipmentStatusFormatter.Build(
            deviceCode, deviceName, EquipmentStatusCache.Shared, m_collector);
    }
}}
