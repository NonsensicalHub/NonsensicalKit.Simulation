using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using NonsensicalKit.DigitalTwin.Motion;
using UnityEngine;

/// <summary>
/// 将设备 MQTT JSON 报文解析为 DataHub / MotionPartUpdate 可用的 <see cref="PointData"/>。
/// </summary>

namespace Frame.Core.Mqtt
{
public static class MqttPayloadParser
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore
    };

    public static bool TryParse(string json, out MqttDevicePayload payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            payload = JsonConvert.DeserializeObject<MqttDevicePayload>(json, JsonSettings);
            return payload != null
                   && !string.IsNullOrEmpty(payload.DeviceCode)
                   && payload.Points != null
                   && payload.Points.Count > 0;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MqttPayloadParser] JSON 解析失败: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// 转为 MotionPartUpdate 使用的点位列表。
    /// 传输机类型在列表前填充 2 个占位点，使 ConveyorPartMotion 默认索引 2/3/4 对应正转/反转/载货。
    /// NoPallet 会反转为载货信号（IsLoad = !NoPallet）。
    /// </summary>
    public static List<PointData> ToMotionPoints(MqttDevicePayload payload)
    {
        var points = new List<PointData>();
        if (payload?.Points == null)
        {
            return points;
        }

        long ticks = ParseReadTimeTicks(payload.ReadTime);
        string partId = payload.DeviceCode;
        bool padForDrum = IsConveyorTopicType(payload.TopicType);

        if (padForDrum)
        {
            points.Add(CreatePoint("_pad0", partId, ticks, false));
            points.Add(CreatePoint("_pad1", partId, ticks, false));
        }

        foreach (var p in payload.Points)
        {
            if (p == null || string.IsNullOrEmpty(p.PointCode))
            {
                continue;
            }

            string value = NormalizeValue(p);
            if (padForDrum && p.PointCode == "NoPallet")
            {
                value = InvertBoolString(value);
            }

            points.Add(new PointData(
                p.PointName ?? p.PointCode,
                BuildPointId(partId, p.PointCode),
                p.PointCode,
                ticks,
                value));
        }

        return points;
    }

    public static IEnumerable<PointData> ToDataHubPoints(MqttDevicePayload payload)
    {
        if (payload?.Points == null)
        {
            yield break;
        }

        long ticks = ParseReadTimeTicks(payload.ReadTime);
        string partId = payload.DeviceCode;

        foreach (var p in payload.Points)
        {
            if (p == null || string.IsNullOrEmpty(p.PointCode))
            {
                continue;
            }

            yield return new PointData(
                p.PointName ?? p.PointCode,
                BuildPointId(partId, p.PointCode),
                p.PointCode,
                ticks,
                NormalizeValue(p));
        }
    }

    public static bool IsConveyorTopicType(string topicType)
    {
        if (string.IsNullOrEmpty(topicType))
        {
            return false;
        }

        return topicType is "CC/RC" or "LR" or "TCC";
    }

    public static bool IsStackerTopicType(string topicType) =>
        topicType == "堆垛机" || topicType?.StartsWith("DDJ", StringComparison.Ordinal) == true;

    public static bool IsLifterTopicType(string topicType) =>
        topicType == "提升机" || topicType?.StartsWith("TSJ", StringComparison.Ordinal) == true;

    public static bool IsShaperTopicType(string topicType) =>
        topicType == "整形机" || topicType?.StartsWith("ZXJ", StringComparison.Ordinal) == true;

    public static bool IsFilmWrapTopicType(string topicType) =>
        topicType == "缠膜机" || topicType?.StartsWith("CMJ", StringComparison.Ordinal) == true;

    private static string BuildPointId(string partId, string pointCode) => $"{partId}_{pointCode}";

    private static PointData CreatePoint(string pointCode, string partId, long ticks, bool value) =>
        new(pointCode, BuildPointId(partId, pointCode), pointCode, ticks, value);

    private static string NormalizeValue(MqttPointPayload point)
    {
        if (point.Value == null)
        {
            return string.Empty;
        }

        string raw = point.Value.Trim();
        if (raw.Length == 0)
        {
            return string.Empty;
        }

        if (IsBoolType(point.DataType))
        {
            return ParseBool(raw).ToString();
        }

        return raw;
    }

    private static bool IsBoolType(string dataType) =>
        string.Equals(dataType, "Bool", StringComparison.OrdinalIgnoreCase);

    private static bool ParseBool(string raw)
    {
        if (bool.TryParse(raw, out bool b))
        {
            return b;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
        {
            return n != 0;
        }

        return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase);
    }

    private static string InvertBoolString(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return "False";
        }

        return ParseBool(raw) ? "False" : "True";
    }

    private static long ParseReadTimeTicks(string readTime)
    {
        if (DateTime.TryParse(readTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
        {
            return dt.Ticks / 10000;
        }

        return DateTime.Now.Ticks / 10000;
    }
}
}
