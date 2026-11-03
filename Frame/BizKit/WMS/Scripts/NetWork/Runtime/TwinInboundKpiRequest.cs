using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NaughtyAttributes;
using NonsensicalKit.Core;
using UnityEngine;

/// <summary>
/// GET /api/wms/twin/inbound-kpi — 入库 KPI，取数到反序列化为止。
/// </summary>
public class TwinInboundKpiRequest : TwinFetchRequestBase
{
    private const string IoccKey = "twinInboundKpi";

    [SerializeField, Label("粒度 hour/day/week/month/year")]
    private string m_granularity = "day";

    [SerializeField, Label("物流中心（0=全仓）")]
    private string m_warehouseId = "0";

    [SerializeField, Label("开始日期 yyyy-MM-dd")]
    private string m_startDate = "";

    [SerializeField, Label("结束日期 yyyy-MM-dd")]
    private string m_endDate = "";

    public List<TwinInboundKpiItem> LastData { get; private set; }

    protected override string LogTag => "TwinInboundKpi";

    protected override string BuildUrl() => URLS.GetTwinInboundKpi(m_serverKey);

    protected override Dictionary<string, string> BuildQueryArgs()
    {
        var args = new Dictionary<string, string>();
        AddQueryIfSet(args, "granularity", m_granularity);
        AddQueryIfSet(args, "warehouse_id", m_warehouseId);
        AddQueryIfSet(args, "start_date", m_startDate);
        AddQueryIfSet(args, "end_date", m_endDate);
        return args.Count > 0 ? args : null;
    }

    protected override UniTask OnDataFetchedAsync(string rawJson, CancellationToken cancellationToken)
    {
        TwinApiResponse<TwinInboundKpiItem> response =
            ParseResponse<TwinInboundKpiItem>(rawJson);
        if (response == null)
        {
            return UniTask.CompletedTask;
        }

        LastData = response.data;
        IOCC.Set(IoccKey, LastData);
        return UniTask.CompletedTask;
    }
}
