using System;

/// <summary>
/// GET /api/wms/twin/pending-inbound 待入库订单行。
/// </summary>
[Serializable]
public class TwinPendingInboundItem
{
    public string data_date;
    public long order_id;
    public string order_no;
    public long warehouse_id;
    public long goodsowner_id;
    public string goodsowner_name;
    public long? owner_company_id;
    public string owner_company_name;
    public int use_status;
    public string use_status_name;
    public decimal goods_qty;
    public string create_time;
    public string refresh_time;
}
