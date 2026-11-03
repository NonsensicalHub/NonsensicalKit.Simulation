using System.Collections.Generic;
using Newtonsoft.Json;
using NonsensicalKit.Tools;
using UnityEngine;
using Frame.Core.Config;


namespace Frame.Core.Network
{
public static class URLS
{
    /// <summary>全局默认鉴权头；优先用 <see cref="GetAuthorization"/> 按 Server Key 取值。</summary>
    public static string Authorization;

    public static JsonSerializerSettings jsonSetting = new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore };

    /// <summary>Key → BaseUrl</summary>
    private static readonly Dictionary<string, string> ServerBases = new();

    /// <summary>Key → Authorization（可空则回退全局 Authorization）</summary>
    private static readonly Dictionary<string, string> ServerAuths = new();

    public static string GetBase(string serverKey)
    {
        if (string.IsNullOrEmpty(serverKey))
        {
            return null;
        }

        return ServerBases.TryGetValue(serverKey, out var baseUrl) ? baseUrl : null;
    }

    /// <summary>按 Server Key 取鉴权；未配置时回退全局 Authorization。</summary>
    public static string GetAuthorization(string serverKey)
    {
        if (!string.IsNullOrEmpty(serverKey)
            && ServerAuths.TryGetValue(serverKey, out var auth)
            && !string.IsNullOrEmpty(auth))
        {
            return auth;
        }

        return Authorization;
    }

    public static string Api(string serverKey, string path)
    {
        string baseUrl = GetBase(serverKey);
        if (string.IsNullOrEmpty(baseUrl))
        {
            return null;
        }

        if (string.IsNullOrEmpty(path))
        {
            return baseUrl;
        }

        return baseUrl.TrimEnd('/') + (path.StartsWith("/") ? path : "/" + path);
    }

    public static void Init(URLBaseData data)
    {
        if (data == null)
        {
            return;
        }

        ServerBases.Clear();
        ServerAuths.Clear();
        Authorization = data.Authorization;

        if (data.Servers != null)
        {
            foreach (var entry in data.Servers)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Key) || string.IsNullOrEmpty(entry.BaseUrl))
                {
                    continue;
                }

                string key = entry.Key.Trim();
                string baseUrl = entry.BaseUrl.Trim().TrimEnd('/');
                if (ServerBases.ContainsKey(key))
                {
                    Debug.LogWarning($"[URLS] 重复的 Server Key「{key}」，后者覆盖前者。");
                }

                ServerBases[key] = baseUrl;
                if (!string.IsNullOrEmpty(entry.Authorization))
                {
                    ServerAuths[key] = entry.Authorization.Trim();
                }
            }
        }

        if (data.MqttTopics != null && data.MqttTopics.Count > 0)
        {
            InitMQTTTopics(data.MqttTopics);
        }
        else
        {
            IsReady = true;
        }
    }

    #region MQTT主题

    public static bool IsReady;

    public static List<string> MQTTTopics = new();

    /// <summary>
    /// key: topicZHName value: topic
    /// </summary>
    public static Dictionary<string, List<string>> MQTTTopicsDic = new();

    public static void InitMQTTTopics(List<MQTTConfig> Topics)
    {
        MQTTTopics.Clear();
        MQTTTopicsDic.Clear();

        if (Topics == null)
        {
            IsReady = true;
            return;
        }

        foreach (var item in Topics)
        {
            if (item == null || string.IsNullOrEmpty(item.Topic))
            {
                continue;
            }

            item.CollectSubscribeTopics(MQTTTopics);

            string replaceStr = item.GetReplaceToken();
            if (item.SNCodes == null)
            {
                continue;
            }

            foreach (var sn in item.SNCodes)
            {
                if (string.IsNullOrWhiteSpace(sn))
                {
                    continue;
                }

                MQTTTopicsDic.ListAdd(item.TopicName, item.Topic.Replace(replaceStr, sn.Trim()));
            }
        }

        IsReady = true;
    }

    #endregion
}
}
