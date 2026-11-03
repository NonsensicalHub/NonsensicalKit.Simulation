using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NaughtyAttributes;
using NonsensicalKit.Core;
using UnityEngine;

/// <summary>
/// GET /api/wms/twin/pending-inbound — 待入库订单列表，取数到反序列化为止。
/// </summary>
public class TwinPendingInboundRequest : TwinFetchRequestBase
{
    private const string IoccKey = "twinPendingInbound";

    [SerializeField, Label("快照日 yyyy-MM-dd（空=当天）")]
    private string m_dataDate = "";

    [SerializeField, Label("状态编码（空=全部）")]
    private string m_useStatus = "";

    [SerializeField, Label("物流中心（空=全部）")]
    private string m_warehouseId = "";

    public List<TwinPendingInboundItem> LastData { get; private set; }

    protected override string LogTag => "TwinPendingInbound";

    protected override string BuildUrl() => URLS.GetTwinPendingInbound(m_serverKey);

    protected override Dictionary<string, string> BuildQueryArgs()
    {
        var args = new Dictionary<string, string>();
        AddQueryIfSet(args, "data_date", m_dataDate);
        AddQueryIfSet(args, "use_status", m_useStatus);
        AddQueryIfSet(args, "warehouse_id", m_warehouseId);
        return args.Count > 0 ? args : null;
    }

    protected override UniTask OnDataFetchedAsync(string rawJson, CancellationToken cancellationToken)
    {
        TwinApiResponse<TwinPendingInboundItem> response =
            ParseResponse<TwinPendingInboundItem>(rawJson);
        if (response == null)
        {
            return UniTask.CompletedTask;
        }

        LastData = response.data;
        IOCC.Set(IoccKey, LastData);
        return UniTask.CompletedTask;
    }
}
