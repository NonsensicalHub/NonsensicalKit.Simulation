using System.Collections.Generic;
using Newtonsoft.Json;
using NonsensicalKit.Tools;
using UnityEngine;

public static class URLS
{
    public static string Authorization;
    public static JsonSerializerSettings jsonSetting = new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore };

    /// <summary>Key → BaseUrl</summary>
    private static readonly Dictionary<string, string> ServerBases = new();

    /// <summary>
    /// 按后台 Key 取 BaseUrl；未配置时返回 null。
    /// </summary>
    public static string GetBase(string serverKey)
    {
        if (string.IsNullOrEmpty(serverKey))
        {
            return null;
        }

        return ServerBases.TryGetValue(serverKey, out var baseUrl) ? baseUrl : null;
    }

    /// <summary>
    /// 拼接接口完整地址：BaseUrl + path（path 建议以 / 开头）。
    /// </summary>
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

    /// <summary>
    /// 全部货位状态（有货/逻辑坐标/类型），无入参查全表。
    /// </summary>
    public static string GetTwinPosStatus(string serverKey) =>
        Api(serverKey, "/api/wms/twin/pos-status");

    /// <summary>查询货位信息（库存+货主商品+批号明细）。</summary>
    public static string GetTwinPosStock(string serverKey) =>
        Api(serverKey, "/api/wms/twin/pos-stock");

    /// <summary>物料检索（POST JSON）。</summary>
    public static string PostTwinMaterialSearch(string serverKey) =>
        Api(serverKey, "/api/wms/twin/material-search");

    /// <summary>实时库存全仓汇总，无入参。</summary>
    public static string GetTwinStockRealtime(string serverKey) =>
        Api(serverKey, "/api/wms/twin/stock-realtime");

    /// <summary>待入库订单列表。</summary>
    public static string GetTwinPendingInbound(string serverKey) =>
        Api(serverKey, "/api/wms/twin/pending-inbound");

    /// <summary>近效期明细（含货位）。</summary>
    public static string GetTwinNearExpiry(string serverKey) =>
        Api(serverKey, "/api/wms/twin/near-expiry");

    /// <summary>入库 KPI。</summary>
    public static string GetTwinInboundKpi(string serverKey) =>
        Api(serverKey, "/api/wms/twin/inbound-kpi");

    /// <summary>出库 KPI。</summary>
    public static string GetTwinOutboundKpi(string serverKey) =>
        Api(serverKey, "/api/wms/twin/outbound-kpi");

    /// <summary>库存分布（库龄分段）。</summary>
    public static string GetTwinStockAge(string serverKey) =>
        Api(serverKey, "/api/wms/twin/stock-age");

    /// <summary>待出库订单列表（outbound 模块，含 goods_qty）。</summary>
    public static string GetOutboundPendingList(string serverKey) =>
        Api(serverKey, "/api/wms/outbound/pending-list");

    /// <summary>海康 RCS：查询 AGV 状态（mapShortName）。</summary>
    public static string PostQueryAgvStatus(string serverKey) =>
        Api(serverKey, "/rcms-dps/rest/queryAgvStatus");

    /// <summary>海康 RCS：查询任务状态（agvCode / taskCodes）。</summary>
    public static string PostQueryTaskStatus(string serverKey) =>
        Api(serverKey, "/rcms/services/rest/hikRpcService/queryTaskStatus");

    /// <summary>
    /// 从 URLBaseConfig / ConfigService 写入各后台基址与鉴权。
    /// </summary>
    public static void Init(URLBaseData data)
    {
        if (data == null)
        {
            return;
        }

        ServerBases.Clear();
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
