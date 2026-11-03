using System;

/// <summary>
/// GET /api/wms/outbound/pending-list 待出库订单行（含 goods_qty）。
/// </summary>

namespace Frame.BizKit.WMS.Network
{
[Serializable]
public class OutboundPendingListItem
{
    public string data_date;
    public long order_id;
    public string order_no;
    public string goodsowner_name;
    public string owner_company_name;
    public int use_status;
    public string use_status_name;
    public decimal goods_qty;
    public string create_time;
    public string etl_load_time;
}
}
