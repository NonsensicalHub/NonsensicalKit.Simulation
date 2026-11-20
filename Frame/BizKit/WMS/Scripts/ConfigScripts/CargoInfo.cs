using UnityEngine;

namespace Frame.BizKit.WMS
{
/// <summary>
/// WMS 货位/托盘物料信息（宿主或网络层可填充）。
/// </summary>
[System.Serializable]
public struct CargoInfo
{
    /// <summary>物料编号</summary>
    [Tooltip("物料编号")]
    public string _materialNo;
    /// <summary>货位号</summary>
    [Tooltip("货位号")]
    public string _storageId;
    /// <summary>托盘号</summary>
    [Tooltip("托盘号")]
    public string _palletCode;
    /// <summary>原厂批次</summary>
    [Tooltip("原厂批次")]
    public string _originBatch;
    /// <summary>本厂批次</summary>
    [Tooltip("本厂批次")]
    public string _stockBatch;
    /// <summary>质量状态（0待检1合格2不合格）</summary>
    [Tooltip("质量状态（0待检1合格2不合格）")]
    public int _qualityStatus;
    /// <summary>是否生成请检单(1是0否)</summary>
    [Tooltip("是否生成请检单(1是0否)")]
    public int _isQualityCheck;
    /// <summary>有效期至 （yyyy-MM-dd）</summary>
    [Tooltip("有效期至 （yyyy-MM-dd）")]
    public string _effectiveDate;
    /// <summary>复检期 （yyyy-MM-dd）</summary>
    [Tooltip("复检期 （yyyy-MM-dd）")]
    public string _recheckDate;
    /// <summary>入库日期</summary>
    [Tooltip("入库日期")]
    public string _inDate;
    /// <summary>入库单号</summary>
    [Tooltip("入库单号")]
    public string _inOrderId;
    /// <summary>数量</summary>
    [Tooltip("数量")]
    public string _amount;

    public string _materialName;
    public string _materialSpecification;
    public string _uint;

    public Vector3Int _V3_cargoPosition;
    /// <summary>货品位置</summary>
    public string _cargoPosition;
}
}