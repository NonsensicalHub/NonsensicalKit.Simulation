using System;
using System.Collections.Generic;
using Frame.Core.UI;
using Newtonsoft.Json.Linq;
using NonsensicalKit.Core;

namespace Frame.BizKit.WMS
{
/// <summary>
/// 货位信息展示用数据模型（可与 WMS API、<see cref="CargoInfo"/> 等对接）。
/// </summary>
[Serializable]
public class CargoSlotDetailData
{
    public string SlotCode;
    public string MaterialCode;
    public string MaterialName;
    public string Specification;
    public string PalletCode;
    public string Amount;
    public string Unit;
    public string InDate;
    public string EffectiveDate;
    /// <summary>与后端约定：0 待检 / 1 合格 / 2 不合格（对应 DetailLabel 枚举映射）</summary>
    public int QualityStatus;
    public string CargoPosition;
    public string OriginBatch;
    public string StockBatch;

    /// <summary>数字孪生货架网格，对应 LocateHighlightBin 的 Int4。</summary>
    public Int4 WarehouseGridIndex;

    /// <summary>仓库逻辑名，如 StackerWarehouse。</summary>
    public string WarehouseName = "StackerWarehouse";

    /// <summary>
    /// 转为详情标签面板使用的行数据；属性顺序固定，货位号始终为第一项以保证左侧列表标题正确。
    /// </summary>
    public JObject ToDetailLabelRow()
    {
        var o = new JObject
        {
            [CargoSlotDetailKeys.SlotCode] = SlotCode ?? "",
            [CargoSlotDetailKeys.MaterialCode] = MaterialCode ?? "",
            [CargoSlotDetailKeys.MaterialName] = MaterialName ?? "",
            [CargoSlotDetailKeys.Specification] = Specification ?? "",
            [CargoSlotDetailKeys.PalletCode] = PalletCode ?? "",
            [CargoSlotDetailKeys.Amount] = Amount ?? "",
            [CargoSlotDetailKeys.Unit] = Unit ?? "",
            [CargoSlotDetailKeys.InDate] = InDate ?? "",
            [CargoSlotDetailKeys.EffectiveDate] = EffectiveDate ?? "",
            [CargoSlotDetailKeys.QualityStatus] = QualityStatus.ToString(),
            [CargoSlotDetailKeys.CargoPosition] = CargoPosition ?? "",
            [CargoSlotDetailKeys.OriginBatch] = OriginBatch ?? "",
            [CargoSlotDetailKeys.StockBatch] = StockBatch ?? ""
        };
        return o;
    }

    public static JArray ToDetailLabelArray(IEnumerable<CargoSlotDetailData> rows)
    {
        var arr = new JArray();
        foreach (var r in rows)
            arr.Add(r.ToDetailLabelRow());
        return arr;
    }

    public static JArray ToDetailLabelArray(CargoSlotDetailData row)
    {
        var arr = new JArray();
        if (row != null)
            arr.Add(row.ToDetailLabelRow());
        return arr;
    }

    /// <summary>
    /// 从现有 <see cref="CargoInfo"/> 填充（字段不全时保持默认）。
    /// </summary>
    public static CargoSlotDetailData FromCargoInfo(CargoInfo info)
    {
        return new CargoSlotDetailData
        {
            SlotCode = info._storageId,
            MaterialCode = info._materialNo,
            MaterialName = info._materialName,
            Specification = info._materialSpecification,
            PalletCode = info._palletCode,
            Amount = info._amount,
            Unit = info._uint,
            InDate = info._inDate,
            EffectiveDate = info._effectiveDate,
            QualityStatus = info._qualityStatus,
            CargoPosition = info._cargoPosition,
            OriginBatch = info._originBatch,
            StockBatch = info._stockBatch,
            WarehouseGridIndex = new Int4(info._V3_cargoPosition.x, info._V3_cargoPosition.y, info._V3_cargoPosition.z, 0)
        };
    }
}

/// <summary>
/// 货位详情 JSON 字段名（与 <see cref="DetailLabelInfo.Name"/>、<see cref="CargoSlotDetailData.ToDetailLabelRow"/> 一致）。
/// </summary>
public static class CargoSlotDetailKeys
{
    public const string SlotCode = "货位号";
    public const string MaterialCode = "物料编码";
    public const string MaterialName = "物料名称";
    public const string Specification = "规格";
    public const string PalletCode = "托盘号";
    public const string Amount = "数量";
    public const string Unit = "单位";
    public const string InDate = "入库时间";
    public const string EffectiveDate = "有效期";
    public const string QualityStatus = "质量状态";
    public const string CargoPosition = "货品位置";
    public const string OriginBatch = "原厂批次";
    public const string StockBatch = "本厂批次";
}

/// <summary>
/// 详情面板左侧列表与字段映射的默认配置（质量状态枚举展示）。
/// </summary>
public static class CargoSlotDetailLabelSchema
{
    private static readonly string[] EmptyKeys = System.Array.Empty<string>();
    private static readonly string[] EmptyVals = System.Array.Empty<string>();

    public static List<DetailLabelInfo> CreateDefault()
    {
        return new List<DetailLabelInfo>
        {
            Row(CargoSlotDetailKeys.SlotCode),
            Row(CargoSlotDetailKeys.MaterialCode),
            Row(CargoSlotDetailKeys.MaterialName),
            Row(CargoSlotDetailKeys.Specification),
            Row(CargoSlotDetailKeys.PalletCode),
            Row(CargoSlotDetailKeys.Amount),
            Row(CargoSlotDetailKeys.Unit),
            Row(CargoSlotDetailKeys.InDate),
            Row(CargoSlotDetailKeys.EffectiveDate),
            new DetailLabelInfo
            {
                Name = CargoSlotDetailKeys.QualityStatus,
                Unit = "",
                ConvertKey = new[] { "0", "1", "2" },
                ConvertValue = new[] { "待检", "合格", "不合格" }
            },
            Row(CargoSlotDetailKeys.CargoPosition),
            Row(CargoSlotDetailKeys.OriginBatch),
            Row(CargoSlotDetailKeys.StockBatch)
        };
    }

    private static DetailLabelInfo Row(string name)
    {
        return new DetailLabelInfo
        {
            Name = name,
            Unit = "",
            ConvertKey = EmptyKeys,
            ConvertValue = EmptyVals
        };
    }
}
}