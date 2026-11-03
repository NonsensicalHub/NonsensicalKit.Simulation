using System;

/// <summary>
/// GET /api/wms/twin/stock-realtime 全仓实时库存汇总（表内通常仅一行）。
/// </summary>

namespace Frame.BizKit.WMS.Network
{
[Serializable]
public class TwinStockRealtimeItem
{
    public decimal stock_qty;
    public decimal wait_out_qty;
    public decimal wait_in_qty;
    public decimal wait_move_in_qty;
    public decimal wait_move_out_qty;
}
}
