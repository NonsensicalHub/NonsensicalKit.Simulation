using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NaughtyAttributes;
using Newtonsoft.Json;
using NonsensicalKit.Core;
using UnityEngine;

/// <summary>
/// POST /api/wms/twin/material-search — 物料检索，取数到反序列化为止。
/// </summary>
public class TwinMaterialSearchRequest : TwinFetchRequestBase
{
    public const string IoccKey = "twinMaterialSearch";

    [SerializeField, Label("物料名称（模糊）")]
    private string m_goodsName = "";

    [SerializeField, Label("物料编码 owner_goods_id")]
    private string m_ownerGoodsId = "";

    [SerializeField, Label("商品编号")]
    private string m_goodsNo = "";

    [SerializeField, Label("货位号/名称（模糊）")]
    private string m_posName = "";

    [SerializeField, Label("入库日期 yyyy-MM-dd")]
    private string m_inboundDate = "";

    [SerializeField, Label("综合模糊 search_text")]
    private string m_searchText = "";

    public List<TwinMaterialSearchItem> LastData { get; private set; }

    protected override string LogTag => "TwinMaterialSearch";

    protected override string BuildUrl() => URLS.PostTwinMaterialSearch(m_serverKey);

    protected override string BuildPostBody()
    {
        var body = new TwinMaterialSearchRequestBody
        {
            goods_name = m_goodsName ?? "",
            owner_goods_id = m_ownerGoodsId ?? "",
            goods_no = m_goodsNo ?? "",
            pos_name = m_posName ?? "",
            inbound_date = m_inboundDate ?? "",
            search_text = m_searchText ?? ""
        };
        return JsonConvert.SerializeObject(body, JsonSetting);
    }

    /// <summary>
    /// 按给定条件检索；返回本次 <see cref="LastData"/>（失败或空则为空列表）。
    /// </summary>
    public async UniTask<List<TwinMaterialSearchItem>> SearchAsync(TwinMaterialSearchRequestBody body)
    {
        ApplyBody(body);

        float wait = 10f;
        while ((!UrlsReady || IsRequesting) && wait > 0f)
        {
            await UniTask.Yield();
            wait -= Time.unscaledDeltaTime;
        }

        if (!UrlsReady)
        {
            Debug.LogWarning($"[{LogTag}] URL 未就绪，取消检索。", this);
            return new List<TwinMaterialSearchItem>();
        }

        await FetchAwaitableAsync();
        return LastData ?? new List<TwinMaterialSearchItem>();
    }

    public void ApplyBody(TwinMaterialSearchRequestBody body)
    {
        body ??= new TwinMaterialSearchRequestBody();
        m_goodsName = body.goods_name ?? "";
        m_ownerGoodsId = body.owner_goods_id ?? "";
        m_goodsNo = body.goods_no ?? "";
        m_posName = body.pos_name ?? "";
        m_inboundDate = body.inbound_date ?? "";
        m_searchText = body.search_text ?? "";
    }

    protected override UniTask OnDataFetchedAsync(string rawJson, CancellationToken cancellationToken)
    {
        TwinApiResponse<TwinMaterialSearchItem> response =
            ParseResponse<TwinMaterialSearchItem>(rawJson);
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
