using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 按固定行头格式解析录制行，再按 RecordParseConfig 解析正文。
/// 固定头：yyyy-MM-dd HH:mm:ss.fff|ticks(18)|key(5)|EE|
/// MQ 额外：变长频道 + '|' + 正文；其它事件：'|' 后整段即为正文。
/// </summary>

namespace Frame.Core.RecordPlayback
{
public static class RecordPayloadParser
{
    public const char FieldSeparator = '|';
    public const int MinLineLength = 52;
    public const string MqttEventType = "MQ";

    public const int TimeStart = 0;
    public const int TimeLength = 23;
    public const int TicksStart = 24;
    public const int TicksLength = 18;
    public const int KeyStart = 43;
    public const int KeyLength = 5;
    public const int EventStart = 49;
    public const int EventLength = 2;
    public const int AfterEventStart = 52;

    public sealed class ParsedLine
    {
        public string Time;
        public long Ticks;
        public string Key;
        public string EventType;

        /// <summary>MQ 的变长频道（topic）；非 MQ 为空。</summary>
        public string Channel;

        /// <summary>事件正文（MQ 为频道后的内容，格式因项目而异）。</summary>
        public string Payload;

        public string[] Parts;
        public Dictionary<string, string> NamedGroups;
    }

    public static RecordParseConfig LoadConfig(TextAsset configAsset)
    {
        if (configAsset == null || string.IsNullOrWhiteSpace(configAsset.text))
        {
            throw new ArgumentException("解析配置 TextAsset 为空");
        }

        var config = JsonConvert.DeserializeObject<RecordParseConfig>(configAsset.text);
        if (config == null)
        {
            throw new InvalidOperationException("无法反序列化解析配置");
        }

        config.events ??= new List<RecordEventRule>();
        return config;
    }

    public static bool TryParseLine(string line, out ParsedLine parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(line) || line.Length < MinLineLength)
        {
            return false;
        }

        var ticksStr = line.Substring(TicksStart, TicksLength);
        if (!long.TryParse(ticksStr, out var ticks))
        {
            return false;
        }

        var eventType = line.Substring(EventStart, EventLength);
        var rest = line[AfterEventStart..];
        var channel = string.Empty;
        var payload = rest;

        // MQ：event 后还有变长频道字段，用第一个 '|' 与正文分隔
        if (string.Equals(eventType, MqttEventType, StringComparison.OrdinalIgnoreCase))
        {
            var sepIndex = rest.IndexOf(FieldSeparator);
            if (sepIndex < 0)
            {
                channel = rest.Trim();
                payload = string.Empty;
            }
            else
            {
                channel = rest[..sepIndex].Trim();
                payload = rest[(sepIndex + 1)..].Trim();
            }
        }

        parsed = new ParsedLine
        {
            Time = line.Substring(TimeStart, TimeLength),
            Ticks = ticks,
            Key = line.Substring(KeyStart, KeyLength),
            EventType = eventType,
            Channel = channel,
            Payload = payload,
            Parts = Array.Empty<string>(),
            NamedGroups = new Dictionary<string, string>(StringComparer.Ordinal)
        };
        return true;
    }

    public static bool TryParsePayload(ParsedLine line, RecordEventRule rule)
    {
        if (line == null || rule == null)
        {
            return false;
        }

        var mode = string.IsNullOrWhiteSpace(rule.payloadMode) ? "raw" : rule.payloadMode.Trim();
        var trim = rule.trimParts;
        var payload = line.Payload ?? string.Empty;

        switch (mode.ToLowerInvariant())
        {
            case "raw":
                line.Parts = new[] { ApplyTrim(payload, trim) };
                return true;

            case "split":
            {
                var parts = rule.payloadMaxParts > 0
                    ? payload.Split(new[] { FieldSeparator }, rule.payloadMaxParts, StringSplitOptions.None)
                    : payload.Split(FieldSeparator);
                line.Parts = TrimAll(parts, trim);
                return true;
            }

            case "lastseparator":
            {
                var idx = payload.LastIndexOf(FieldSeparator);
                if (idx < 0)
                {
                    line.Parts = new[] { ApplyTrim(payload, trim) };
                }
                else
                {
                    line.Parts = new[]
                    {
                        ApplyTrim(payload[..idx], trim),
                        ApplyTrim(payload[(idx + 1)..], trim)
                    };
                }

                return true;
            }

            case "regex":
            {
                if (string.IsNullOrEmpty(rule.payloadPattern))
                {
                    Debug.LogWarning("[RecordPayloadParser] regex 模式缺少 payloadPattern");
                    return false;
                }

                var regex = new Regex(rule.payloadPattern);
                var match = regex.Match(payload);
                if (!match.Success)
                {
                    return false;
                }

                var list = new List<string>();
                for (var i = 1; i < match.Groups.Count; i++)
                {
                    list.Add(ApplyTrim(match.Groups[i].Value, trim));
                }

                line.Parts = list.ToArray();
                line.NamedGroups.Clear();
                foreach (var name in regex.GetGroupNames())
                {
                    if (int.TryParse(name, out _))
                    {
                        continue;
                    }

                    line.NamedGroups[name] = ApplyTrim(match.Groups[name].Value, trim);
                }

                return true;
            }

            default:
                Debug.LogWarning($"[RecordPayloadParser] 未知 payloadMode: {mode}");
                return false;
        }
    }

    public static string ResolveTemplate(string template, ParsedLine line)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        if (line == null)
        {
            return template;
        }

        var result = template
            .Replace("{time}", line.Time ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{ticks}", line.Ticks.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{key}", line.Key ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{event}", line.EventType ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{channel}", line.Channel ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{topic}", line.Channel ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{payload}", line.Payload ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        if (line.Parts != null)
        {
            for (var i = 0; i < line.Parts.Length; i++)
            {
                result = result.Replace($"{{{i}}}", line.Parts[i] ?? string.Empty, StringComparison.Ordinal);
            }
        }

        if (line.NamedGroups != null)
        {
            foreach (var kv in line.NamedGroups)
            {
                result = result.Replace($"{{{kv.Key}}}", kv.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }
        }

        return result;
    }

    private static string[] TrimAll(string[] parts, bool trim)
    {
        if (!trim || parts == null)
        {
            return parts ?? Array.Empty<string>();
        }

        var result = new string[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            result[i] = parts[i]?.Trim() ?? string.Empty;
        }

        return result;
    }

    private static string ApplyTrim(string value, bool trim)
    {
        if (value == null)
        {
            return string.Empty;
        }

        return trim ? value.Trim() : value;
    }
}
}
