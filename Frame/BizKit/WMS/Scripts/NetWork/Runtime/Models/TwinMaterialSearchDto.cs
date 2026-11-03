using System;

/// <summary>
/// POST /api/wms/twin/material-search 请求体（字段均可空字符串）。
/// </summary>
[Serializable]
public class TwinMaterialSearchRequestBody
{
    public string goods_name = "";
    public string owner_goods_id = "";
    public string goods_no = "";
    public string pos_name = "";
    public string inbound_date = "";
    public string search_text = "";
}

/// <summary>
/// 物料检索 UI / 文档中的检索类型文案（与接口 Body 字段一一对应）。
/// <para>
/// 下列主文案与 <c>CargoSlotDetailKeys</c> 同值：物料名称 / 物料编码 / 货位号 / 入库时间。
/// 在同一个 <c>switch</c> 里不要同时写两边的 const，否则会触发 CS0152。
/// </para>
/// </summary>
public static class TwinMaterialSearchKeys
{
    public const string GoodsName = "物料名称";
    public const string OwnerGoodsId = "物料编码";
    public const string OwnerGoodsIdAlias = "物料编码(ID)";
    public const string GoodsNo = "商品编号";
    public const string PosName = "货位号";
    public const string PosNameAlias = "货位名称";
    public const string InboundDate = "入库时间";
    public const string InboundDateAlias = "入库日期";
    public const string SearchText = "综合模糊";
}

/// <summary>
/// POST /api/wms/twin/material-search 检索结果行。
/// </summary>
[Serializable]
public class TwinMaterialSearchItem
{
    public string data_date;
    public long owner_goods_id;
    public string goods_no;
    public string goods_name;
    public long pos_id;
    public string pos_no;
    public string pos_name;
    public string inbound_date;
    public decimal goods_qty;
    public string lot_no;
    public string end_validate;
    public long warehouse_id;
    public string search_text;
    public string refresh_time;
}
