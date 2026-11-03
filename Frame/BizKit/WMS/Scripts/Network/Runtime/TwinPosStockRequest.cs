using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NaughtyAttributes;
using NonsensicalKit.Core;
using UnityEngine;

/// <summary>
/// GET /api/wms/twin/pos-stock — 按货位拉取库存明细，取数到反序列化为止。
/// </summary>

namespace Frame.BizKit.WMS.Network
{
public class TwinPosStockRequest : TwinFetchRequestBase
{
    private const string IoccKey = "twinPosStock";

    [SerializeField, Label("货位 ID")]
    private long m_posId = 2586;

    public List<TwinPosStockItem> LastData { get; private set; }

    protected override string LogTag => "TwinPosStock";

    protected override string BuildUrl() => WmsUrls.GetTwinPosStock(m_serverKey);

    protected override Dictionary<string, string> BuildQueryArgs()
    {
        return new Dictionary<string, string>
        {
            { "pos_id", m_posId.ToString() }
        };
    }

    /// <summary>
    /// 按货位 ID 拉取库存明细；返回本次 <see cref="LastData"/>（失败或空则为空列表）。
    /// </summary>
    public async UniTask<List<TwinPosStockItem>> FetchByPosIdAsync(long posId)
    {
        m_posId = posId;
        LastData = null;

        float wait = 10f;
        while ((!UrlsReady || IsRequesting) && wait > 0f)
        {
            await UniTask.Yield();
            wait -= Time.unscaledDeltaTime;
        }

        if (!UrlsReady)
        {
            Debug.LogWarning($"[{LogTag}] URL 未就绪，取消货位库存拉取。", this);
            return new List<TwinPosStockItem>();
        }

        await FetchAwaitableAsync();
        return LastData ?? new List<TwinPosStockItem>();
    }

    protected override UniTask OnDataFetchedAsync(string rawJson, CancellationToken cancellationToken)
    {
        TwinApiResponse<TwinPosStockItem> response = ParseResponse<TwinPosStockItem>(rawJson);
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
