using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class MQTTConfig
{
    public string TopicName;
    public string Topic;
    public string ReplaceStr;
    public string[] SNCodes;

    [Tooltip("默认勾选：订阅时用 + 通配替换占位符。取消勾选：按 SNCodes 逐条拼接主题再订阅。")]
    public bool UseWildcardSubscribe = true;

    public string GetReplaceToken()
    {
        return string.IsNullOrEmpty(ReplaceStr) ? "{DeviceCode}" : ReplaceStr;
    }

    /// <summary>
    /// 收集实际要向 Broker 订阅的主题（通配或 SN 展开），自动去重。
    /// </summary>
    public void CollectSubscribeTopics(List<string> into)
    {
        if (into == null || string.IsNullOrEmpty(Topic))
        {
            return;
        }

        string token = GetReplaceToken();
        if (UseWildcardSubscribe)
        {
            AddUnique(into, Topic.Replace(token, "+"));
            return;
        }

        if (SNCodes == null || SNCodes.Length == 0)
        {
            Debug.LogWarning($"[MQTTConfig] 「{TopicName}」未启用通配且 SNCodes 为空，跳过订阅。Topic={Topic}");
            return;
        }

        for (int i = 0; i < SNCodes.Length; i++)
        {
            string sn = SNCodes[i];
            if (string.IsNullOrWhiteSpace(sn))
            {
                continue;
            }

            AddUnique(into, Topic.Replace(token, sn.Trim()));
        }
    }

    private static void AddUnique(List<string> into, string topic)
    {
        if (string.IsNullOrEmpty(topic) || into.Contains(topic))
        {
            return;
        }

        into.Add(topic);
    }
}
