using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NaughtyAttributes;
using NonsensicalKit.Core;
using UnityEngine;

/// <summary>
/// GET /api/wms/outbound/pending-list — 待出库订单列表（含 goods_qty），取数到反序列化为止。
/// </summary>
public class OutboundPendingListRequest : TwinFetchRequestBase
{
    private const string IoccKey = "outboundPendingList";

    [SerializeField, Label("快照日 yyyy-MM-dd（空=当天）")]
    private string m_dataDate = "";

    public List<OutboundPendingListItem> LastData { get; private set; }

    protected override string LogTag => "OutboundPendingList";

    protected override string BuildUrl() => URLS.GetOutboundPendingList(m_serverKey);

    protected override Dictionary<string, string> BuildQueryArgs()
    {
        var args = new Dictionary<string, string>();
        AddQueryIfSet(args, "data_date", m_dataDate);
        return args.Count > 0 ? args : null;
    }

    protected override UniTask OnDataFetchedAsync(string rawJson, CancellationToken cancellationToken)
    {
        TwinApiResponse<OutboundPendingListItem> response =
            ParseResponse<OutboundPendingListItem>(rawJson);
        if (response == null)
        {
            return UniTask.CompletedTask;
        }

        LastData = response.data;
        IOCC.Set(IoccKey, LastData);
        return UniTask.CompletedTask;
    }
}
