using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using NaughtyAttributes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NonsensicalKit.Core;
using UnityEngine;
using Frame.Core.Network;
using Frame.Core.Config;

/// <summary>
/// GET /api/wms/twin/stock-age — 库存库龄分布。
/// 每次请求必须带齐 data_date / warehouse_id / bucket_code，且均不可为空。
/// 全量需按 bucket_code 分三次：lt90 / d90_360 / gt360。
/// </summary>

namespace Frame.BizKit.WMS.Network
{
public class TwinStockAgeRequest : TwinFetchRequestBase
{
    public const string IoccKey = "twinStockAge";

    public static readonly string[] DefaultBuckets = { "lt90", "d90_360", "gt360" };

    [SerializeField, Label("快照日 yyyy-MM-dd（空=当天）")]
    private string m_dataDate = "";

    [SerializeField, Label("物流中心（空=0 全仓）")]
    private string m_warehouseId = "0";

    [SerializeField, Label("库龄段（空则分三次拉默认三段）")]
    private string m_bucketCode = "";

    public List<TwinStockAgeItem> LastData { get; private set; }

    /// <summary>基类定时器 <see cref="GetData"/> 拉完三段后通知 UI。</summary>
    public event Action<List<TwinStockAgeItem>> DataUpdated;

    protected override string LogTag => "TwinStockAge";

    protected override string BuildUrl() => WmsUrls.GetTwinStockAge(m_serverKey);

    /// <summary>RequestBase 定时回调：按三段拉取，不走单次 FetchAsync。</summary>
    protected override void GetData(int id)
    {
        TimerFetchBucketsAsync().Forget();
    }

    private async UniTaskVoid TimerFetchBucketsAsync()
    {
        if (TwinDataSource.IsOffline)
            return;

        List<TwinStockAgeItem> list = await FetchBucketsAsync(DefaultBuckets);
        DataUpdated?.Invoke(list ?? new List<TwinStockAgeItem>());
    }

    protected override Dictionary<string, string> BuildQueryArgs()
    {
        // 三个参数都必须有非空值
        return new Dictionary<string, string>
        {
            ["data_date"] = ResolveDataDate(),
            ["warehouse_id"] = ResolveWarehouseId(),
            ["bucket_code"] = (m_bucketCode ?? "").Trim()
        };
    }

    /// <summary>
    /// 拉取库龄。bucket 为空时按 lt90 / d90_360 / gt360 各请求一次。
    /// </summary>
    public async UniTask<List<TwinStockAgeItem>> FetchOnceAsync(string bucketCode = null)
    {
        if (!await WaitReadyAsync())
            return new List<TwinStockAgeItem>();

        string code = bucketCode ?? m_bucketCode;
        if (string.IsNullOrWhiteSpace(code))
            return await FetchBucketsAsync(DefaultBuckets);

        return await FetchSingleBucketAsync(code.Trim());
    }

    /// <summary>按分段逐次请求并合并（每次都带齐三个非空参数）。</summary>
    public async UniTask<List<TwinStockAgeItem>> FetchBucketsAsync(params string[] bucketCodes)
    {
        if (bucketCodes == null || bucketCodes.Length == 0)
            bucketCodes = DefaultBuckets;

        if (!await WaitReadyAsync())
            return new List<TwinStockAgeItem>();

        var merged = new List<TwinStockAgeItem>(bucketCodes.Length);
        for (int i = 0; i < bucketCodes.Length; i++)
        {
            string code = bucketCodes[i];
            if (string.IsNullOrWhiteSpace(code))
                continue;

            List<TwinStockAgeItem> part = await FetchSingleBucketAsync(code.Trim());
            if (part == null || part.Count == 0)
                continue;

            merged.AddRange(part);
        }

        LastData = merged;
        IOCC.Set(IoccKey, LastData);
        Debug.Log($"[{LogTag}] 合并库龄分段共 {LastData.Count} 条。", this);
        return LastData;
    }

    private async UniTask<List<TwinStockAgeItem>> FetchSingleBucketAsync(string bucketCode)
    {
        if (string.IsNullOrWhiteSpace(bucketCode))
        {
            Debug.LogError($"[{LogTag}] bucket_code 不能为空。", this);
            return new List<TwinStockAgeItem>();
        }

        m_bucketCode = bucketCode.Trim();
        LastData = null;

        float wait = 10f;
        while (IsRequesting && wait > 0f)
        {
            await UniTask.Yield();
            wait -= Time.unscaledDeltaTime;
        }

        Dictionary<string, string> args = BuildQueryArgs();
        if (string.IsNullOrEmpty(args["bucket_code"]))
        {
            Debug.LogError($"[{LogTag}] bucket_code 不能为空，取消请求。", this);
            return new List<TwinStockAgeItem>();
        }

        Debug.Log($"[{LogTag}] 请求 {FormatUrlWithQuery(BuildUrl(), args)}", this);

        await FetchAwaitableAsync();
        return LastData ?? new List<TwinStockAgeItem>();
    }

    private string ResolveDataDate()
    {
        string date = (m_dataDate ?? "").Trim();
        if (!string.IsNullOrEmpty(date))
            return date;
        return DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private string ResolveWarehouseId()
    {
        string id = (m_warehouseId ?? "").Trim();
        return string.IsNullOrEmpty(id) ? "0" : id;
    }

    private async UniTask<bool> WaitReadyAsync()
    {
        float wait = 10f;
        while ((!UrlsReady || IsRequesting) && wait > 0f)
        {
            await UniTask.Yield();
            wait -= Time.unscaledDeltaTime;
        }

        if (!UrlsReady)
        {
            Debug.LogWarning($"[{LogTag}] URL 未就绪，取消库龄分布拉取。", this);
            return false;
        }

        return true;
    }

    protected override UniTask OnDataFetchedAsync(string rawJson, CancellationToken cancellationToken)
    {
        LastData = TryParseStockAge(rawJson);
        if (LastData != null)
            IOCC.Set(IoccKey, LastData);
        return UniTask.CompletedTask;
    }

    private List<TwinStockAgeItem> TryParseStockAge(string rawJson)
    {
        if (string.IsNullOrEmpty(rawJson))
        {
            Debug.LogError($"[{LogTag}] 响应为空。", this);
            return null;
        }

        try
        {
            JObject root = JObject.Parse(rawJson);
            bool success = root.Value<bool?>("success") ?? false;
            int code = root.Value<int?>("code") ?? 0;
            string message = root.Value<string>("message");

            if (!success || code != 200)
            {
                Debug.LogError(
                    $"[{LogTag}] 接口失败 success={success} code={code} message={message} raw={TrimForLog(rawJson)}",
                    this);
                return null;
            }

            JToken dataToken = root["data"];
            var list = new List<TwinStockAgeItem>();

            if (dataToken == null || dataToken.Type == JTokenType.Null)
            {
                Debug.LogError($"[{LogTag}] data 为空。raw={TrimForLog(rawJson)}", this);
                return null;
            }

            if (dataToken.Type == JTokenType.Array)
            {
                List<TwinStockAgeItem> arr =
                    dataToken.ToObject<List<TwinStockAgeItem>>(JsonSerializer.Create(JsonSetting));
                if (arr != null)
                    list.AddRange(arr);
            }
            else if (dataToken.Type == JTokenType.Object)
            {
                TwinStockAgeItem one =
                    dataToken.ToObject<TwinStockAgeItem>(JsonSerializer.Create(JsonSetting));
                if (one != null)
                    list.Add(one);
            }
            else
            {
                Debug.LogError(
                    $"[{LogTag}] data 类型不支持 type={dataToken.Type} raw={TrimForLog(rawJson)}",
                    this);
                return null;
            }

            Debug.Log($"[{LogTag}] bucket={m_bucketCode} 解析到 {list.Count} 条。", this);
            return list;
        }
        catch (Exception e)
        {
            Debug.LogError($"[{LogTag}] 响应解析失败: {e.Message} raw={TrimForLog(rawJson)}", this);
            return null;
        }
    }

    private static string FormatUrlWithQuery(string url, Dictionary<string, string> args)
    {
        if (args == null || args.Count == 0)
            return url;

        var sb = new StringBuilder(url);
        sb.Append('?');
        bool first = true;
        foreach (KeyValuePair<string, string> kv in args)
        {
            if (!first) sb.Append('&');
            first = false;
            sb.Append(kv.Key).Append('=').Append(kv.Value);
        }

        return sb.ToString();
    }

    private static string TrimForLog(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "";
        return raw.Length <= 500 ? raw : raw.Substring(0, 500) + "...";
    }
}
}
