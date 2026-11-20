using System.Collections.Generic;
using System.Linq;
using Frame.BizKit.WMS;
using Frame.BizKit.WMS.ZCalendar;
using Frame.Core.UI;
using Newtonsoft.Json.Linq;
using NonsensicalKit.Core;
using NonsensicalKit.Tools;
using UnityEngine;

namespace Frame.Sample.WmsDemo
{
/// <summary>
/// 库存场景示例：初始化搜索类型与详情列配置，处理搜索并在选中结果后刷新详情面板、联动货架高亮。
/// 对接后端时请在 <see cref="OnSearch"/> / <see cref="OnSelectResult"/> 中替换示例数据与请求逻辑。
/// 依赖：Frame.BizKit.WMS、Frame.BizKit.WMS.ZCalendar（需 Sample ThirdParty/ZCalendar 或宿主自备）。
/// </summary>
public class SearchToolExample : NonsensicalMono
{
    [SerializeField] [Tooltip("与 BinData 一致：Row, Column, Level, Depth")]
    private string _warehouseName = "StackerWarehouse";

    private readonly Dictionary<string, CargoSlotDetailData> _slotByCode = new();

    private void Awake()
    {
        Subscribe<string, string>(SearchToolEvent.Search, OnSearch);
        Subscribe<string, string>(SearchToolEvent.SelectResult, OnSelectResult);
        AddHandler<Int4, bool>("ShowDetailLabelsOnGpuPicking", _warehouseName, ShowDetailLabels);
    }

    private void Start()
    {
        LoadMockSlotsIfEmpty();

        Publish(DetailLabelEvent.InitSampleConfig, CargoSlotDetailLabelSchema.CreateDefault());
        Publish(SearchToolEvent.InitSearchType, new List<SearchInfo>
        {
            new SearchInfo { Text = CargoSlotDetailKeys.SlotCode, Type = SearchType.String },
            new SearchInfo { Text = CargoSlotDetailKeys.InDate, Type = SearchType.DataRange }
        });
    }

    private void LoadMockSlotsIfEmpty()
    {
        if (_slotByCode.Count > 0)
            return;

        void Add(CargoSlotDetailData d)
        {
            d.WarehouseName = _warehouseName;
            _slotByCode[d.SlotCode] = d;
        }

        Add(new CargoSlotDetailData
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
            CargoPosition = "1巷 5列 12层",
            WarehouseGridIndex = new Int4(1, 5, 12, 0)
        });
        Add(new CargoSlotDetailData
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
            CargoPosition = "1巷 8列 3层",
            WarehouseGridIndex = new Int4(1, 8, 3, 0)
        });
        Add(new CargoSlotDetailData
        {
            SlotCode = "02-02-20-0",
            MaterialCode = "M-3003",
            MaterialName = "冰淇淋",
            Specification = "1L/桶",
            PalletCode = "",
            Amount = "0",
            Unit = "桶",
            InDate = "2024-05-20 09:00:00",
            EffectiveDate = "",
            QualityStatus = 0,
            CargoPosition = "2巷 2列 20层",
            WarehouseGridIndex = new Int4(2, 2, 20, 0)
        });
    }

    private void OnSearch(string searchTypeLabel, string content)
    {
        List<string> results = new List<string>();
        if (searchTypeLabel == CargoSlotDetailKeys.SlotCode)
        {
            if (string.IsNullOrEmpty(content))
            {
                Publish(SearchToolEvent.GetSearchResults, results);
                return;
            }

            foreach (var kv in _slotByCode)
            {
                if (kv.Key.Contains(content))
                    results.Add(kv.Key);
            }
        }
        else if (searchTypeLabel == CargoSlotDetailKeys.InDate)
        {
            foreach (var kv in _slotByCode)
            {
                if (MatchInboundDate(kv.Value.InDate, content))
                    results.Add(kv.Key);
            }
        }

        Publish(SearchToolEvent.GetSearchResults, results);
    }

    private static bool MatchInboundDate(string inDate, string rangeFromSearch)
    {
        if (string.IsNullOrEmpty(inDate))
            return false;
        if (string.IsNullOrEmpty(rangeFromSearch))
            return true;

        var parts = rangeFromSearch.Split('+');
        if (parts.Length >= 2)
        {
            var start = parts[0].Trim();
            var end = parts[1].Trim();
            if (string.IsNullOrEmpty(end))
                end = start;
            return string.CompareOrdinal(inDate, start) >= 0 && string.CompareOrdinal(inDate, end) <= 0;
        }

        return inDate.Contains(rangeFromSearch);
    }

    private void OnSelectResult(string _, string selectedSlotCode)
    {
        if (string.IsNullOrEmpty(selectedSlotCode) || !_slotByCode.TryGetValue(selectedSlotCode, out var slot))
            return;

        Publish(DetailLabelEvent.ShowDetailLabels, CargoSlotDetailData.ToDetailLabelArray(slot));
        Execute<Int4, bool>("LocateHighlightBin", slot.WarehouseName, slot.WarehouseGridIndex);
    }

    private bool ShowDetailLabels(Int4 arg)
    {
        if (arg.Equals(Int4.Zero))
        {
            Publish(DetailLabelEvent.ShowDetailLabels, new JArray());
            return true;
        }

        var result = _slotByCode.ElementAt(UnityEngine.Random.Range(0, _slotByCode.Keys.Count)).Value;
        if (result == null) return false;
        Publish(DetailLabelEvent.ShowDetailLabels, CargoSlotDetailData.ToDetailLabelArray(result));
        return true;
    }
}
}