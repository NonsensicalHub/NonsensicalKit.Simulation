#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using CusAttribute;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把仓储报警设备表接到 Core 扩展点，避免 Core 引用 RealTimeSctObj。
/// </summary>
[InitializeOnLoad]
internal static class WmsCoreEditorHooks
{
    private const string DefaultAlarmConfigResource = "CusConfigs/RealTiemAlarmInfo";

    static WmsCoreEditorHooks()
    {
        URLBaseConfig.SnCodeUpdater = UpdateSnCodesFromAlarmConfig;
        TopicPropertyDrawer.ResolveDeviceSnCodes = ResolveDeviceSnCodes;
    }

    private static void UpdateSnCodesFromAlarmConfig(URLBaseConfig config)
    {
        if (config == null || config.data?.MqttTopics == null)
        {
            Debug.LogWarning("[URLBaseConfig] 配置或 MQTT 主题为空，无法更新 SN Code。", config);
            return;
        }

        var alarmConfig = Resources.Load<RealTimeSctObj>(DefaultAlarmConfigResource);
        var devices = alarmConfig?.deviceInfos?.list_deviceInfos;
        if (devices == null)
        {
            Debug.LogWarning($"[URLBaseConfig] 未找到报警配置 Resources/{DefaultAlarmConfigResource}。", config);
            return;
        }

        Debug.Log("更新SN Code : " + devices.Count);
        foreach (var item in devices)
        {
            if (item == null || string.IsNullOrEmpty(item.m_deviceName))
            {
                continue;
            }

            foreach (var item2 in config.data.MqttTopics)
            {
                if (item2 == null || string.IsNullOrEmpty(item2.TopicName))
                {
                    continue;
                }

                var a = item2.TopicName.Replace("主题", "");
                Debug.Log(item.m_deviceName.Contains(a));

                if (item.m_deviceName.Contains(a))
                {
                    item2.SNCodes ??= Array.Empty<string>();
                    Array.Resize(ref item2.SNCodes, item2.SNCodes.Length + 1);
                    item2.SNCodes[^1] = item.m_DeviceID;
                }
            }
        }

        EditorUtility.SetDirty(config);
    }

    private static List<string> ResolveDeviceSnCodes(TopicAttribute attribute)
    {
        if (attribute == null || string.IsNullOrEmpty(attribute.ConfigPath))
        {
            return new List<string>();
        }

        var alarmConfig = Resources.Load<RealTimeSctObj>(attribute.ConfigPath);
        var devices = alarmConfig?.deviceInfos?.list_deviceInfos;
        if (devices == null)
        {
            return new List<string>();
        }

        return devices
            .Where(x => x != null && x.m_deviceName == attribute.DeviceSNType)
            .Select(x => x.m_DeviceID)
            .ToList();
    }
}
#endif
