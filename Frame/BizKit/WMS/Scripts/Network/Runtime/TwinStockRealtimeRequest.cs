using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NonsensicalKit.Core;
using UnityEngine;

/// <summary>
/// GET /api/wms/twin/stock-realtime — 全仓实时库存汇总，取数到反序列化为止。
/// </summary>

namespace Frame.BizKit.WMS.Network
{
public class TwinStockRealtimeRequest : TwinFetchRequestBase
{
    public const string IoccKey = "twinStockRealtime";

    public List<TwinStockRealtimeItem> LastData { get; private set; }

    protected override string LogTag => "TwinStockRealtime";

    protected override string BuildUrl() => WmsUrls.GetTwinStockRealtime(m_serverKey);

    /// <summary>
    /// 拉取全仓实时库存；返回本次 <see cref="LastData"/>（失败或空则为空列表）。
    /// </summary>
    public async UniTask<List<TwinStockRealtimeItem>> FetchOnceAsync()
    {
        LastData = null;

        float wait = 10f;
        while ((!UrlsReady || IsRequesting) && wait > 0f)
        {
            await UniTask.Yield();
            wait -= Time.unscaledDeltaTime;
        }

        if (!UrlsReady)
        {
            Debug.LogWarning($"[{LogTag}] URL 未就绪，取消实时库存拉取。", this);
            return new List<TwinStockRealtimeItem>();
        }

        await FetchAwaitableAsync();
        return LastData ?? new List<TwinStockRealtimeItem>();
    }

    protected override UniTask OnDataFetchedAsync(string rawJson, CancellationToken cancellationToken)
    {
        TwinApiResponse<TwinStockRealtimeItem> response =
            ParseResponse<TwinStockRealtimeItem>(rawJson);
        if (response == null)
        {
            LastData = null;
            return UniTask.CompletedTask;
        }

        LastData = response.data;
        IOCC.Set(IoccKey, LastData);
        return UniTask.CompletedTask;
    }
}
}
