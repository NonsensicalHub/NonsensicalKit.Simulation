using System;

/// <summary>
/// GET /api/wms/twin/stock-age 库存库龄分布行。
/// </summary>

namespace Frame.BizKit.WMS.Network
{
[Serializable]
public class TwinStockAgeItem
{
    public string data_date;
    public long warehouse_id;
    public string bucket_code;
    public string bucket_label;
    public int? bucket_min_days;
    public int? bucket_max_days;
    public decimal goods_qty;
    public int sku_cnt;
    public int use_inbound_date_mode;
    public string refresh_time;
}
}
