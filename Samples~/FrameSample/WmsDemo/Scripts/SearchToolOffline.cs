using Frame.BizKit.WMS;
using Frame.BizKit.WMS.Network;
using NonsensicalKit.Core;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 离线物料检索数据源：通过 AddHandler 提供检索 / 随机详情，供 <see cref="SearchToolExample"/> Execute 调用。
/// 在线模式不会走到本组件；与 <see cref="DemoWarehouseRandom"/> 同一套离线 Handler 约定。
/// </summary>
public class SearchToolOffline : NonsensicalMono
{
    public const string SearchHandler = "OfflineMaterialSearch";
    public const string RandomDetailHandler = "OfflineMaterialRandomDetail";

    [SerializeField] [Tooltip("写入结果的 WarehouseName，与 SearchToolExample / WarehouseManager 一致")]
    private string _warehouseName = "StackerWarehouse";

    private List<CargoSlotDetailData> _catalog;

    private void Awake()
    {
        EnsureCatalog();
        AddHandler<string, string, List<CargoSlotDetailData>>(SearchHandler, Search);
        AddHandler<CargoSlotDetailData>(RandomDetailHandler, GetRandomDetail);
    }

    private List<CargoSlotDetailData> Search(string searchTypeLabel, string content)
    {
        EnsureCatalog();
        content = content?.Trim() ?? "";
        if (string.IsNullOrEmpty(content) || _catalog.Count == 0)
            return new List<CargoSlotDetailData>();

        string key = NormalizeSearchTypeLabel(searchTypeLabel);
        var hits = new List<CargoSlotDetailData>();
        for (int i = 0; i < _catalog.Count; i++)
        {
            CargoSlotDetailData row = _catalog[i];
            if (row == null || !Match(row, key, content))
                continue;

            hits.Add(CloneWithWarehouse(row));
        }

        return hits;
    }

    private CargoSlotDetailData GetRandomDetail()
    {
        EnsureCatalog();
        if (_catalog.Count == 0)
            return null;

        return CloneWithWarehouse(_catalog[UnityEngine.Random.Range(0, _catalog.Count)]);
    }

    private CargoSlotDetailData CloneWithWarehouse(CargoSlotDetailData src)
    {
        var copy = new CargoSlotDetailData
        {
            SlotCode = src.SlotCode,
            MaterialCode = src.MaterialCode,
            MaterialName = src.MaterialName,
            Specification = src.Specification,
            PalletCode = src.PalletCode,
            Amount = src.Amount,
            Unit = src.Unit,
            InDate = src.InDate,
            EffectiveDate = src.EffectiveDate,
            QualityStatus = src.QualityStatus,
            CargoPosition = src.CargoPosition,
            OriginBatch = src.OriginBatch,
            StockBatch = src.StockBatch,
            WarehouseGridIndex = src.WarehouseGridIndex,
            WarehouseName = string.IsNullOrEmpty(_warehouseName) ? src.WarehouseName : _warehouseName
        };
        return copy;
    }

    private static bool Match(CargoSlotDetailData row, string key, string content)
    {
        switch (key)
        {
            case TwinMaterialSearchKeys.GoodsName:
                return Contains(row.MaterialName, content);
            case TwinMaterialSearchKeys.OwnerGoodsId:
            case TwinMaterialSearchKeys.GoodsNo:
                return Contains(row.MaterialCode, content);
            case TwinMaterialSearchKeys.PosName:
                return Contains(row.SlotCode, content) || Contains(row.CargoPosition, content);
            case TwinMaterialSearchKeys.InboundDate:
                return MatchInboundDate(row.InDate, content);
            case TwinMaterialSearchKeys.SearchText:
            default:
                return Contains(row.MaterialName, content)
                       || Contains(row.MaterialCode, content)
                       || Contains(row.SlotCode, content)
                       || Contains(row.CargoPosition, content)
                       || Contains(row.PalletCode, content)
                       || Contains(row.InDate, content);
        }
    }

    private static bool MatchInboundDate(string inDate, string content)
    {
        if (string.IsNullOrEmpty(inDate))
            return false;

        string start = content.Split('+')[0].Trim();
        if (start.Length >= 10 && start[4] == '-' && start[7] == '-')
            start = start.Substring(0, 10);

        return inDate.IndexOf(start, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool Contains(string field, string content)
    {
        return !string.IsNullOrEmpty(field)
               && field.IndexOf(content, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// 与 SearchToolExample 一致：将 UI 下拉文案归一到 TwinMaterialSearchKeys。
    /// </summary>
    private static string NormalizeSearchTypeLabel(string label)
    {
        if (string.IsNullOrEmpty(label))
            return TwinMaterialSearchKeys.SearchText;

        switch (label.Trim())
        {
            case TwinMaterialSearchKeys.GoodsName:
                return TwinMaterialSearchKeys.GoodsName;
            case TwinMaterialSearchKeys.OwnerGoodsId:
            case TwinMaterialSearchKeys.OwnerGoodsIdAlias:
                return TwinMaterialSearchKeys.OwnerGoodsId;
            case TwinMaterialSearchKeys.GoodsNo:
                return TwinMaterialSearchKeys.GoodsNo;
            case TwinMaterialSearchKeys.PosName:
            case TwinMaterialSearchKeys.PosNameAlias:
            case "货位号/名称":
                return TwinMaterialSearchKeys.PosName;
            case TwinMaterialSearchKeys.InboundDate:
            case TwinMaterialSearchKeys.InboundDateAlias:
                return TwinMaterialSearchKeys.InboundDate;
            case TwinMaterialSearchKeys.SearchText:
            case "综合检索":
                return TwinMaterialSearchKeys.SearchText;
            default:
                return TwinMaterialSearchKeys.SearchText;
        }
    }

    private void EnsureCatalog()
    {
        if (_catalog != null)
            return;

        // Int4: X=层 Y=列 Z=排 W=深；坐标落在库容 (8,60,20) 内，避免角点
        _catalog = new List<CargoSlotDetailData>
        {
            new CargoSlotDetailData
            {
                SlotCode = "01-05-12-0",
                MaterialCode = "M-1001",
                MaterialName = "冷冻牛肉",
                Specification = "20kg/箱",
                PalletCode = "TP-90001",
                Amount = "120",
                Unit = "箱",
                InDate = "2024-06-01 10:30:00",
                EffectiveDate = "2025-12-01",
                QualityStatus = 1,
                CargoPosition = "12排 5列 1层",
                WarehouseGridIndex = new Int4(1, 5, 12, 0),
            },
            new CargoSlotDetailData
            {
                SlotCode = "01-08-03-0",
                MaterialCode = "M-2002",
                MaterialName = "速冻水饺",
                Specification = "500g/袋",
                PalletCode = "TP-90002",
                Amount = "80",
                Unit = "袋",
                InDate = "2024-07-15 14:00:00",
                EffectiveDate = "2025-03-01",
                QualityStatus = 1,
                CargoPosition = "3排 8列 1层",
                WarehouseGridIndex = new Int4(1, 8, 3, 0),
            },
            new CargoSlotDetailData
            {
                SlotCode = "02-02-19-0",
                MaterialCode = "M-3003",
                MaterialName = "冰淇淋",
                Specification = "1L/桶",
                PalletCode = "",
                Amount = "0",
                Unit = "桶",
                InDate = "2024-05-20 09:00:00",
                EffectiveDate = "",
                QualityStatus = 0,
                CargoPosition = "19排 2列 2层",
                WarehouseGridIndex = new Int4(2, 2, 19, 0),
            },
            new CargoSlotDetailData
            {
                SlotCode = "02-15-06-0",
                MaterialCode = "M-4004",
                MaterialName = "冷冻虾仁",
                Specification = "10kg/箱",
                PalletCode = "TP-90004",
                Amount = "56",
                Unit = "箱",
                InDate = "2024-08-08 08:20:00",
                EffectiveDate = "2025-08-08",
                QualityStatus = 1,
                CargoPosition = "6排 15列 2层",
                WarehouseGridIndex = new Int4(2, 15, 6, 0),
            },
            new CargoSlotDetailData
            {
                SlotCode = "05-42-11-0",
                MaterialCode = "M-4005",
                MaterialName = "速冻青豆",
                Specification = "5kg/袋",
                PalletCode = "TP-90005",
                Amount = "200",
                Unit = "袋",
                InDate = "2024-09-01 11:00:00",
                EffectiveDate = "2025-06-01",
                QualityStatus = 1,
                CargoPosition = "11排 42列 5层",
                WarehouseGridIndex = new Int4(5, 42, 11, 0),
            },
            new CargoSlotDetailData
            {
                SlotCode = "03-30-10-0",
                MaterialCode = "M-4006",
                MaterialName = "冷冻鸡翅",
                Specification = "15kg/箱",
                PalletCode = "TP-90006",
                Amount = "90",
                Unit = "箱",
                InDate = "2024-06-18 16:45:00",
                EffectiveDate = "2025-06-18",
                QualityStatus = 1,
                CargoPosition = "10排 30列 3层",
                WarehouseGridIndex = new Int4(3, 30, 10, 0),
            },
            new CargoSlotDetailData
            {
                SlotCode = "05-12-05-0",
                MaterialCode = "M-4007",
                MaterialName = "速冻汤圆",
                Specification = "1kg/袋",
                PalletCode = "TP-90007",
                Amount = "150",
                Unit = "袋",
                InDate = "2024-11-02 09:30:00",
                EffectiveDate = "2025-11-02",
                QualityStatus = 1,
                CargoPosition = "5排 12列 5层",
                WarehouseGridIndex = new Int4(5, 12, 5, 0),
            },
            new CargoSlotDetailData
            {
                SlotCode = "04-45-15-0",
                MaterialCode = "M-4008",
                MaterialName = "冷冻猪排",
                Specification = "12kg/箱",
                PalletCode = "TP-90008",
                Amount = "64",
                Unit = "箱",
                InDate = "2024-04-12 13:10:00",
                EffectiveDate = "2025-04-12",
                QualityStatus = 2,
                CargoPosition = "15排 45列 4层",
                WarehouseGridIndex = new Int4(4, 45, 15, 0),
            },
            new CargoSlotDetailData
            {
                SlotCode = "06-01-01-0",
                MaterialCode = "M-4009",
                MaterialName = "冷冻三文鱼",
                Specification = "8kg/箱",
                PalletCode = "TP-90009",
                Amount = "40",
                Unit = "箱",
                InDate = "2024-10-20 07:50:00",
                EffectiveDate = "2025-04-20",
                QualityStatus = 1,
                CargoPosition = "1排 1列 6层",
                WarehouseGridIndex = new Int4(6, 1, 1, 0),
            },
            new CargoSlotDetailData
            {
                SlotCode = "02-20-08-0",
                MaterialCode = "M-4010",
                MaterialName = "速冻包子",
                Specification = "2kg/袋",
                PalletCode = "TP-90010",
                Amount = "110",
                Unit = "袋",
                InDate = "2024-12-01 15:00:00",
                EffectiveDate = "2025-06-01",
                QualityStatus = 1,
                CargoPosition = "8排 20列 2层",
                WarehouseGridIndex = new Int4(2, 20, 8, 0),
            },
            new CargoSlotDetailData
            {
                SlotCode = "00-55-18-0",
                MaterialCode = "M-4011",
                MaterialName = "冷冻黄油",
                Specification = "25kg/箱",
                PalletCode = "TP-90011",
                Amount = "30",
                Unit = "箱",
                InDate = "2024-03-08 10:00:00",
                EffectiveDate = "2025-03-08",
                QualityStatus = 0,
                CargoPosition = "18排 55列 0层",
                WarehouseGridIndex = new Int4(0, 55, 18, 0),
            },
            new CargoSlotDetailData
            {
                SlotCode = "07-10-02-0",
                MaterialCode = "M-4012",
                MaterialName = "速冻薯条",
                Specification = "2.5kg/袋",
                PalletCode = "TP-90012",
                Amount = "220",
                Unit = "袋",
                InDate = "2025-01-15 12:30:00",
                EffectiveDate = "2025-10-15",
                QualityStatus = 1,
                CargoPosition = "2排 10列 7层",
                WarehouseGridIndex = new Int4(7, 10, 2, 0),
            },
            new CargoSlotDetailData
            {
                SlotCode = "03-59-00-0",
                MaterialCode = "M-4013",
                MaterialName = "冷冻羊肉卷",
                Specification = "10kg/箱",
                PalletCode = "TP-90013",
                Amount = "75",
                Unit = "箱",
                InDate = "2024-07-01 18:00:00",
                EffectiveDate = "2025-07-01",
                QualityStatus = 1,
                CargoPosition = "0排 59列 3层",
                WarehouseGridIndex = new Int4(3, 59, 0, 0),
            },
            new CargoSlotDetailData
            {
                SlotCode = "01-40-19-0",
                MaterialCode = "M-4014",
                MaterialName = "冰淇淋甜筒",
                Specification = "24支/箱",
                PalletCode = "TP-90014",
                Amount = "48",
                Unit = "箱",
                InDate = "2024-05-20 09:00:00",
                EffectiveDate = "2025-05-20",
                QualityStatus = 1,
                CargoPosition = "19排 40列 1层",
                WarehouseGridIndex = new Int4(1, 40, 19, 0),
            },
            new CargoSlotDetailData
            {
                SlotCode = "04-00-14-0",
                MaterialCode = "M-4015",
                MaterialName = "冷冻玉米粒",
                Specification = "5kg/袋",
                PalletCode = "TP-90015",
                Amount = "180",
                Unit = "袋",
                InDate = "2024-09-25 14:20:00",
                EffectiveDate = "2025-09-25",
                QualityStatus = 1,
                CargoPosition = "14排 0列 4层",
                WarehouseGridIndex = new Int4(4, 0, 14, 0),
            }
        };
    }
}
