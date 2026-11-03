using System;
using System.Collections.Generic;

/// <summary>
/// GET /api/wms/twin/pos-status 统一响应包装。
/// </summary>

namespace Frame.BizKit.WMS.Network
{
[Serializable]
public class TwinPosStatusResponse
{
    public bool success;
    public int code;
    public string message;
    public List<TwinPosStatusItem> data;
}

/// <summary>
/// ads_twin_pos_status 货位状态行。
/// </summary>
[Serializable]
public class TwinPosStatusItem
{
    public string data_date;
    public long warehouse_id;
    public long pos_id;
    public string pos_no;
    public string pos_name;
    public string row_no;
    public string column_no;
    public string layer_no;
    public string depth_no;
    public int pos_type_id;
    public string pos_type_name;
    public string use_status;
    public int has_goods;
    public decimal goods_qty;
    public string refresh_time;

    public bool HasGoods => has_goods == 1;
}
}
