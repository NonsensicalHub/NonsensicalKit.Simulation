using System;

/// <summary>
/// GET /api/wms/twin/near-expiry 近效期明细行（含货位）。
/// </summary>
[Serializable]
public class TwinNearExpiryItem
{
    public string data_date;
    public long id;
    public long warehouse_id;
    public long goodsowner_id;
    public string goodsowner_name;
    public long owner_goods_id;
    public string goods_name;
    public string goods_no;
    public long pos_id;
    public string pos_no;
    public string lot_no;
    public string end_validate;
    public int days_to_expiry;
    public decimal goods_qty;
    public int expiry_threshold_days;
    public string refresh_time;
}
