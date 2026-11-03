using Frame.Core.Network;

/// <summary>
/// 仓储 / 冷链 / 海康 RCS 接口路径（BizKit）。Core 仅提供 <see cref="URLS.Api"/>。
/// </summary>

namespace Frame.BizKit.WMS.Network
{
public static class WmsUrls
{
    public static string GetTwinPosStatus(string serverKey) =>
        URLS.Api(serverKey, "/api/wms/twin/pos-status");

    public static string GetTwinPosStock(string serverKey) =>
        URLS.Api(serverKey, "/api/wms/twin/pos-stock");

    public static string PostTwinMaterialSearch(string serverKey) =>
        URLS.Api(serverKey, "/api/wms/twin/material-search");

    public static string GetTwinStockRealtime(string serverKey) =>
        URLS.Api(serverKey, "/api/wms/twin/stock-realtime");

    public static string GetTwinPendingInbound(string serverKey) =>
        URLS.Api(serverKey, "/api/wms/twin/pending-inbound");

    public static string GetTwinNearExpiry(string serverKey) =>
        URLS.Api(serverKey, "/api/wms/twin/near-expiry");

    public static string GetTwinInboundKpi(string serverKey) =>
        URLS.Api(serverKey, "/api/wms/twin/inbound-kpi");

    public static string GetTwinOutboundKpi(string serverKey) =>
        URLS.Api(serverKey, "/api/wms/twin/outbound-kpi");

    public static string GetTwinStockAge(string serverKey) =>
        URLS.Api(serverKey, "/api/wms/twin/stock-age");

    public static string GetOutboundPendingList(string serverKey) =>
        URLS.Api(serverKey, "/api/wms/outbound/pending-list");

    public static string PostQueryAgvStatus(string serverKey) =>
        URLS.Api(serverKey, "/rcms-dps/rest/queryAgvStatus");

    public static string PostQueryTaskStatus(string serverKey) =>
        URLS.Api(serverKey, "/rcms/services/rest/hikRpcService/queryTaskStatus");
}
}