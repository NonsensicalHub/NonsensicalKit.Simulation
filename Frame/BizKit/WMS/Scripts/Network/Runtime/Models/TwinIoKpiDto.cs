using System;

/// <summary>
/// GET /api/wms/twin/inbound-kpi 入库 KPI 行。
/// </summary>

namespace Frame.BizKit.WMS.Network
{
[Serializable]
public class TwinInboundKpiItem
{
    public string granularity;
    public long warehouse_id;
    public string stat_date;
    public string stat_period_start;
    public string stat_period_end;
    public int in_order_cnt;
    public decimal in_qty;
    public string refresh_time;
}

/// <summary>
/// GET /api/wms/twin/outbound-kpi 出库 KPI 行。
/// </summary>
[Serializable]
public class TwinOutboundKpiItem
{
    public string granularity;
    public long warehouse_id;
    public string stat_date;
    public string stat_period_start;
    public string stat_period_end;
    public int out_order_cnt;
    public decimal out_qty;
    public string refresh_time;
}
}
