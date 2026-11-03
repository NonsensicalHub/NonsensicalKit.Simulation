using System;
using System.Collections.Generic;
using NaughtyAttributes;
using NonsensicalKit.Core.Service.Config;
using UnityEngine;
using Debug = UnityEngine.Debug;

[CreateAssetMenu(fileName = "URLBaseConfig", menuName = "ScriptableObjects/URLBaseConfig")]
public class URLBaseConfig : ConfigObject
{
    public URLBaseData data;

    /// <summary>
    /// 由 BizKit 注册：从报警设备表回填 MQTT SN Code。
    /// Core 不引用业务配置类型。
    /// </summary>
    public static Action<URLBaseConfig> SnCodeUpdater;

    public override ConfigData GetData()
    {
        return data;
    }

    public override void SetData(ConfigData cd)
    {
        data = cd as URLBaseData;
    }

    [Button("更新SN Code")]
    private void UpdateSnCode()
    {
        if (SnCodeUpdater == null)
        {
            Debug.LogWarning("[URLBaseConfig] 未注册 SN Code 更新器。仓储项目请引入 BizKit.WMS。", this);
            return;
        }

        SnCodeUpdater(this);
    }
}

/// <summary>
/// 一个后台服务对应一条配置：Key 唯一，指向其 BaseUrl。
/// </summary>
[Serializable]
public class WarehouseServerEntry
{
    [Tooltip("后台唯一标识，调用方用此 Key 取 BaseUrl")]
    public string Key;

    [Tooltip("可选显示名")]
    public string Name;

    [Tooltip("该后台的 HTTP 根地址，勿带末尾斜杠")]
    public string BaseUrl;
}

[Serializable]
public class URLBaseData : ConfigData
{
    [Header("后台服务（一个后台一个 Key）")]
    public List<WarehouseServerEntry> Servers = new List<WarehouseServerEntry>();

    /// <summary>HTTP 鉴权头。生产密钥请本地覆写，勿把真实 token 打进包默认资产。</summary>
    public string Authorization;

    [Header("MQTT服务器")]
    public string Prefix;

    public string IP;
    public int Port;
    public string Path;

    [Header("MQTT订阅")]
    public List<MQTTConfig> MqttTopics = new List<MQTTConfig>();
}
