using System.Collections.Generic;
using Frame.BizKit.WMS.Equipment;
using Newtonsoft.Json;
using UnityEngine;
using Random = UnityEngine.Random;
using Frame.Core.Config;

/// <summary>
/// 离线 Mock：设备详情查询 Handler。在线时不注册，由真实接口 Handler 接管。
/// 可选：外部可通过 <see cref="SetEquipmentInfoSuspended"/> 临时挂起本 Handler（例如业务侧接管查询）。
/// 无外部脚本时本组件行为与原框架一致。
/// </summary>

namespace Frame.BizKit.WMS
{
public class EquipmentSimulationDataGenerator : MockDataProviderBase
{
    private bool _mockActive;
    private bool _handlerRegistered;
    private bool _equipmentInfoSuspended;

    /// <summary>
    /// 外部挂起/恢复 GetEquipmentInfo。无人调用时保持框架默认 Offline 注册逻辑。
    /// </summary>
    public void SetEquipmentInfoSuspended(bool suspended)
    {
        if (_equipmentInfoSuspended == suspended)
            return;

        _equipmentInfoSuspended = suspended;
        SyncHandlerRegistration();
    }

    public bool IsEquipmentInfoSuspended => _equipmentInfoSuspended;

    protected override void OnMockEnabled()
    {
        _mockActive = true;
        SyncHandlerRegistration();
    }

    protected override void OnMockDisabled()
    {
        _mockActive = false;
        UnregisterHandler();
        base.OnMockDisabled();
    }

    private void SyncHandlerRegistration()
    {
        if (_mockActive && !_equipmentInfoSuspended)
            RegisterHandler();
        else
            UnregisterHandler();
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
       // Debug.Log(JsonConvert.SerializeObject(baseInfo));
        int code = Random.Range(0, 10) % 2;
        var baseDic = new SimpleInfo
        {
            Quest = new("任务信息", "XXXXXXXXX"),
            CompletionStatus = new("完成情况", code switch { 0 => "已完成", 1 => "故障", _ => "进行中" }),
            Color = code switch { 0 => Color.green, 1 => Color.red, _ => Color.yellow },
            Status = code
        };

        var dic = new Dictionary<string, string>
        {
            { "DeviceCode", baseInfo["DeviceCode"] },
            { "名称", baseInfo["DeviceName"] },
            { "运行状态", code == 0 ? "可用" : "不可用" },
            { "当前位置", "XXXXXX" },
            { "当前执行任务", "XXXXXXXXX" },
            { "故障详情", "XXXXXX" }
        };

        return (baseDic, dic);
    }
}
}
