using System;

/// <summary>
/// GET /api/wms/twin/pos-stock 货位库存明细行。
/// </summary>

namespace Frame.BizKit.WMS.Network
{
[Serializable]
public class TwinPosStockItem
{
    public string data_date;
    public long id;
    public long warehouse_id;
    public long pos_id;
    public string pos_no;
    public string pos_name;
    public long owner_goods_id;
    public long goods_id;
    public string goods_no;
    public string goods_name;
    public string goods_bus_name;
    public string goods_mni_code;
    public string goods_spec_type;
    public string goods_unit;
    public string prod_area;
    public string barcode;
    public string src_goods_id;
    public long goodsowner_id;
    public long goods_class_id;
    public int? validity;
    public int? validity_unit;
    public string storage_flag;
    public string min_stock;
    public string max_stock;
    public string safe_qty;
    public long goods_lot_id;
    public string lot_no;
    public string pro_date;
    public string end_validate;
    public string approved_no;
    public string register_no;
    public string kill_lot_no;
    public string inbound_date;
    public decimal goods_qty;
    public decimal pos_qty;
    public decimal wait_out_qty;
    public decimal wait_in_qty;
    public decimal wait_move_in_qty;
    public decimal wait_move_out_qty;
    public string refresh_time;
}
}
