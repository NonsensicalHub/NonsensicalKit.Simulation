using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NaughtyAttributes;
using NonsensicalKit.Core;
using UnityEngine;

/// <summary>
/// GET /api/wms/twin/near-expiry — 近效期明细（含货位），取数到反序列化为止。
/// </summary>
public class TwinNearExpiryRequest : TwinFetchRequestBase
{
    public const string IoccKey = "twinNearExpiry";

    [SerializeField, Label("快照日 yyyy-MM-dd（空=当天）")]
    private string m_dataDate = "";

    [SerializeField, Label("货主 ID（空=全部）")]
    private string m_goodsownerId = "";

    [SerializeField, Label("物流中心（空=全部）")]
    private string m_warehouseId = "";

    public List<TwinNearExpiryItem> LastData { get; private set; }

    protected override string LogTag => "TwinNearExpiry";

    protected override string BuildUrl() => URLS.GetTwinNearExpiry(m_serverKey);

    protected override Dictionary<string, string> BuildQueryArgs()
    {
        var args = new Dictionary<string, string>();
        AddQueryIfSet(args, "data_date", m_dataDate);
        AddQueryIfSet(args, "goodsowner_id", m_goodsownerId);
        AddQueryIfSet(args, "warehouse_id", m_warehouseId);
        return args.Count > 0 ? args : null;
    }

    /// <summary>
    /// 按快照日等条件拉取近效期明细；返回本次 <see cref="LastData"/>（失败或空则为空列表）。
    /// </summary>
    public async UniTask<List<TwinNearExpiryItem>> SearchAsync(
        string dataDate = "",
        string goodsownerId = "",
        string warehouseId = "")
    {
        m_dataDate = dataDate ?? "";
        m_goodsownerId = goodsownerId ?? "";
        m_warehouseId = warehouseId ?? "";

        float wait = 10f;
        while ((!UrlsReady || IsRequesting) && wait > 0f)
        {
            await UniTask.Yield();
            wait -= Time.unscaledDeltaTime;
        }

        if (!UrlsReady)
        {
            Debug.LogWarning($"[{LogTag}] URL 未就绪，取消检索。", this);
            return new List<TwinNearExpiryItem>();
        }

        await FetchAwaitableAsync();
        return LastData ?? new List<TwinNearExpiryItem>();
    }

    protected override UniTask OnDataFetchedAsync(string rawJson, CancellationToken cancellationToken)
    {
        TwinApiResponse<TwinNearExpiryItem> response =
            ParseResponse<TwinNearExpiryItem>(rawJson);
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
