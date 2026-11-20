using EquipmentStatus;
using Frame.BizKit.WMS.Equipment;
using Frame.BizKit.WMS.Hikvision;
using HikvisionAgv;
using System;
using System.Collections.Generic;

namespace EquipmentStatus
{
/// <summary>
/// 设备面板数据组装门面：按 DeviceCode 分流到各机型 Builder。
/// </summary>
public static class EquipmentStatusFormatter
{
    public static EquipmentKind DetectKind(string deviceCode)
    {
        if (string.IsNullOrEmpty(deviceCode))
            return EquipmentKind.Unknown;

        if (deviceCode.StartsWith("DDJ", StringComparison.OrdinalIgnoreCase))
            return EquipmentKind.Stacker;
        if (deviceCode.StartsWith("TSJ", StringComparison.OrdinalIgnoreCase))
            return EquipmentKind.Lifter;

        if (HikvisionAgvSite.SiteIndexOf(deviceCode) >= 0)
            return EquipmentKind.Agv;

        if (EquipmentStatusFormatUtil.IsAllDigits(deviceCode))
            return EquipmentKind.Agv;

        return EquipmentKind.Unknown;
    }

    public static (SimpleInfo, Dictionary<string, string>) Build(
        string deviceCode,
        string deviceName,
        EquipmentStatusCache cache,
        EquipmentStatusCollector collector = null)
    {
        cache ??= EquipmentStatusCache.Shared;
        var kind = DetectKind(deviceCode);

        if (kind == EquipmentKind.Agv && collector != null)
            collector.RequestAgvTaskIfNeeded(deviceCode);

        if (kind == EquipmentKind.Unknown)
            return BuildPlaceholder(deviceCode, deviceName, "暂无实时数据");

        if (!cache.HasAnyData(deviceCode))
            return BuildPlaceholder(deviceCode, deviceName, "等待数据…");

        return kind switch
        {
            EquipmentKind.Agv => AgvEquipmentStatusBuilder.Build(deviceCode, deviceName, cache),
            EquipmentKind.Stacker => StackerEquipmentStatusBuilder.Build(deviceCode, deviceName, cache),
            EquipmentKind.Lifter => LifterEquipmentStatusBuilder.Build(deviceCode, deviceName, cache),
            _ => BuildPlaceholder(deviceCode, deviceName, "暂无实时数据")
        };
    }

    /// <summary>无缓存时的离线占位（纯本地，不依赖 Frame Mock）。</summary>
    public static (SimpleInfo, Dictionary<string, string>) BuildOfflinePlaceholder(
        string deviceCode, string deviceName)
    {
        int statusCode = UnityEngine.Random.Range(0, 2);
        var simple = EquipmentStatusFormatUtil.MakeSimple(
            "任务信息",
            "--",
            "完成情况",
            statusCode == 0 ? "已完成" : "故障",
            statusCode);

        var dic = new Dictionary<string, string>
        {
            ["DeviceCode"] = deviceCode ?? "",
            ["名称"] = deviceName ?? deviceCode ?? "",
            ["运行状态"] = statusCode == 0 ? "可用" : "不可用",
            ["当前位置"] = "---",
            ["当前执行任务"] = "---",
            ["故障详情"] = "---"
        };

        return (simple, dic);
    }

    private static (SimpleInfo, Dictionary<string, string>) BuildPlaceholder(
        string deviceCode, string deviceName, string reason)
    {
        var simple = EquipmentStatusFormatUtil.MakeSimple(
            "任务信息", "-", "完成情况", reason, 1);

        var dic = new Dictionary<string, string>
        {
            ["DeviceCode"] = deviceCode ?? "",
            ["名称"] = deviceName ?? deviceCode ?? "",
            ["运行状态"] = reason,
            ["当前位置"] = "-",
            ["当前执行任务"] = "-",
            ["故障详情"] = "-"
        };

        return (simple, dic);
    }
}
}
