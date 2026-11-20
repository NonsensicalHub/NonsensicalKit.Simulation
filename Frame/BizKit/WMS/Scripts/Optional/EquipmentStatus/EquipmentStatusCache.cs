using System;
using System.Collections.Generic;

namespace EquipmentStatus
{
/// <summary>
/// 设备点位本地缓存（主线程 IOCC 写入）。Key = DeviceCode / PointCode。
/// </summary>
public sealed class EquipmentStatusCache
{
    public static EquipmentStatusCache Shared { get; } = new();

    private readonly Dictionary<string, Dictionary<string, string>> _points = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _deviceNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _updatedAt = new(StringComparer.Ordinal);

    public void SetDeviceName(string deviceCode, string deviceName)
    {
        if (string.IsNullOrEmpty(deviceCode) || string.IsNullOrEmpty(deviceName))
            return;
        _deviceNames[deviceCode] = deviceName;
    }

    public void SetPoint(string deviceCode, string pointCode, string value)
    {
        if (string.IsNullOrEmpty(deviceCode) || string.IsNullOrEmpty(pointCode))
            return;

        if (!_points.TryGetValue(deviceCode, out var map))
        {
            map = new Dictionary<string, string>(StringComparer.Ordinal);
            _points[deviceCode] = map;
        }

        map[pointCode] = value ?? string.Empty;
        _updatedAt[deviceCode] = UnityEngine.Time.realtimeSinceStartup;
    }

    public void SetPoints(string deviceCode, IEnumerable<KeyValuePair<string, string>> points)
    {
        if (string.IsNullOrEmpty(deviceCode) || points == null)
            return;

        foreach (var kv in points)
            SetPoint(deviceCode, kv.Key, kv.Value);
    }

    public bool TryGetPoints(string deviceCode, out IReadOnlyDictionary<string, string> points)
    {
        if (!string.IsNullOrEmpty(deviceCode) && _points.TryGetValue(deviceCode, out var map))
        {
            points = map;
            return true;
        }

        points = null;
        return false;
    }

    public string Get(string deviceCode, string pointCode, string defaultValue = "")
    {
        if (TryGetPoints(deviceCode, out var map) && map.TryGetValue(pointCode, out var v))
            return v;
        return defaultValue;
    }

    public bool HasPoint(string deviceCode, string pointCode)
    {
        return TryGetPoints(deviceCode, out var map) && map.ContainsKey(pointCode);
    }

    public bool TryGetDeviceName(string deviceCode, out string deviceName) =>
        _deviceNames.TryGetValue(deviceCode, out deviceName);

    public bool HasAnyData(string deviceCode) =>
        !string.IsNullOrEmpty(deviceCode) && _points.ContainsKey(deviceCode);

    public float GetUpdatedAt(string deviceCode) =>
        _updatedAt.GetValueOrDefault(deviceCode, 0f);

    public void Clear()
    {
        _points.Clear();
        _deviceNames.Clear();
        _updatedAt.Clear();
    }
}
}
