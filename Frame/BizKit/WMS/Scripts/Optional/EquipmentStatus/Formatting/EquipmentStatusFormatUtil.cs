using System;
using System.Collections.Generic;
using System.Globalization;
using Frame.BizKit.WMS.Equipment;
using NonsensicalKit.DigitalTwin.Motion;
using UnityEngine;

namespace EquipmentStatus
{
    /// <summary>面板组装共用工具（布尔释义、颜色、PartConfig 标签）。</summary>
    public static class EquipmentStatusFormatUtil
    {
        private static readonly Dictionary<string, Dictionary<string, string>> LabelCache = new();

        public static SimpleInfo MakeSimple(
            string questKey, string questValue,
            string statusKey, string statusValue,
            int level)
        {
            return new SimpleInfo
            {
                Quest = new KeyValuePair<string, string>(questKey, questValue),
                CompletionStatus = new KeyValuePair<string, string>(statusKey, statusValue),
                Color = LevelColor(level),
                Status = level
            };
        }

        public static Color LevelColor(int level) => level switch
        {
            0 => Color.green,
            2 => Color.red,
            _ => Color.yellow
        };

        public static string OrDash(string v) => string.IsNullOrEmpty(v) ? "-" : v;

        public static bool IsTruthy(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return false;
            if (bool.TryParse(raw, out bool b))
                return b;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                return n != 0;
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) || raw == "1";
        }

        public static string FormatBool(string raw, string trueText, string falseText)
        {
            if (string.IsNullOrEmpty(raw))
                return "-";
            return IsTruthy(raw) ? trueText : falseText;
        }

        public static string ResolveName(string deviceCode, string deviceName, EquipmentStatusCache cache)
        {
            if (!string.IsNullOrEmpty(deviceName))
                return deviceName;
            if (cache.TryGetDeviceName(deviceCode, out var cached) && !string.IsNullOrEmpty(cached))
                return cached;
            return deviceCode;
        }

        public static void TryAddLabeled(
            Dictionary<string, string> dic,
            string deviceCode,
            EquipmentStatusCache cache,
            EquipmentKind kind,
            params string[] pointCodes)
        {
            var labels = GetLabels(deviceCode, kind);
            for (int i = 0; i < pointCodes.Length; i++)
            {
                string code = pointCodes[i];
                string value = cache.Get(deviceCode, code);
                if (string.IsNullOrEmpty(value))
                    continue;

                string label = labels != null && labels.TryGetValue(code, out var name) ? name : code;
                if (!dic.ContainsKey(label))
                    dic[label] = value;
            }
        }

        public static Dictionary<string, string> GetLabels(string deviceCode, EquipmentKind kind)
        {
            string resourceName = ResolveResourceName(deviceCode, kind);
            if (string.IsNullOrEmpty(resourceName))
                return null;

            if (LabelCache.TryGetValue(resourceName, out var cached))
                return cached;

            var asset = Resources.Load<PartConfigData>(resourceName);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (asset?.data?.pointConfigs != null)
            {
                foreach (var p in asset.data.pointConfigs)
                {
                    if (p == null || string.IsNullOrEmpty(p.pointID))
                        continue;
                    map[p.pointID] = string.IsNullOrEmpty(p.pointName) ? p.pointID : p.pointName;
                }
            }

            LabelCache[resourceName] = map;
            return map;
        }

        public static string ResolveResourceName(string deviceCode, EquipmentKind kind)
        {
            if (string.IsNullOrEmpty(deviceCode))
                return null;

            return kind switch
            {
                EquipmentKind.Agv => $"PartConfigs/AGV_{deviceCode}",
                EquipmentKind.Stacker or EquipmentKind.Lifter => $"PartConfigs/{deviceCode}",
                _ => null
            };
        }

        public static bool IsAllDigits(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;
            for (int i = 0; i < s.Length; i++)
            {
                if (!char.IsDigit(s[i]))
                    return false;
            }

            return true;
        }
    }
}
