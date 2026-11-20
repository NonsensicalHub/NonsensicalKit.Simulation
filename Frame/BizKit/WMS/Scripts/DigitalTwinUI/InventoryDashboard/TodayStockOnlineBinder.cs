using Cysharp.Threading.Tasks;
using Frame.BizKit.WMS.Network;
using Frame.Core.Config;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Core.Service.Config;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Frame.BizKit.WMS
{
/// <summary>
/// 「关键库存信息」表格在线灌数（挂在 DataExample 上）：
/// <see cref="TwinMaterialSearchRequest"/> → POST /api/wms/twin/material-search
/// → 按物料聚合 → IOCC.PublishWithID("showTable", "关键库存信息", …)
/// → 面板 TableControl 订阅该事件刷新表格。
/// </summary>
public class TodayStockOnlineBinder : NonsensicalMono
{
    private const string TableId = "关键库存信息";

    [SerializeField] private TwinMaterialSearchRequest m_request;
    [SerializeField] private int m_topN = 30;

    private void Awake()
    {
        if (m_request == null)
            m_request = GetComponent<TwinMaterialSearchRequest>();
        Subscribe("RefreshTableData",TableId, OnEnable);
    }

    private void OnEnable()
    {
        RefreshAsync().Forget();
    }

    private async UniTaskVoid RefreshAsync()
    {
        await WaitTwinModeReadyAsync();

        if (TwinDataSource.IsOffline)
        {
            GetOffLineData();
            return;
        }

        if (m_request == null)
        {
            Debug.LogWarning($"[{nameof(TodayStockOnlineBinder)}] 缺少 TwinMaterialSearchRequest。", this);
            return;
        }

        List<TwinMaterialSearchItem> items = await m_request.SearchAsync(new TwinMaterialSearchRequestBody());
        List<List<string>> table = BuildTable(items, m_topN);
        // TableControl 在 separateHeaderRow 下会 RemoveAt(0)；仅剩表头时需补空行，避免 data[0] 越界。
        if (table.Count == 1)
            table.Add(new List<string> { "", "", "", "", "" });

        IOCC.PublishWithID("showTable", TableId, table, true);
        Debug.Log($"[{nameof(TodayStockOnlineBinder)}] 已刷新关键库存表 {Mathf.Max(0, table.Count - 1)} 行。", this);
    }

    private static async UniTask WaitTwinModeReadyAsync()
    {
        float wait = 15f;
        while (wait > 0f)
        {
            if (TwinDataSource.IsReady)
                return;

            ServiceCore.SafeGet<ConfigService>(cfg => TwinDataSource.TryInit(cfg));
            if (TwinDataSource.IsReady)
                return;

            await UniTask.Yield();
            wait -= Time.unscaledDeltaTime;
        }
    }

    private static List<List<string>> BuildTable(List<TwinMaterialSearchItem> items, int topN)
    {
        var table = new List<List<string>>
        {
            new List<string> { "物料名称", "料号", "货位占用", "实时库存", "库存占比" }
        };
        if (items == null || items.Count == 0)
            return table;

        var groups = new Dictionary<string, Agg>();
        decimal totalQty = 0m;
        for (int i = 0; i < items.Count; i++)
        {
            TwinMaterialSearchItem item = items[i];
            if (item == null)
                continue;

            string key = item.owner_goods_id != 0
                ? item.owner_goods_id.ToString(CultureInfo.InvariantCulture)
                : $"n:{item.goods_name}|{item.goods_no}";

            if (!groups.TryGetValue(key, out Agg agg))
            {
                agg = new Agg
                {
                    Name = item.goods_name ?? "",
                    No = !string.IsNullOrEmpty(item.goods_no)
                        ? item.goods_no
                        : (item.owner_goods_id != 0
                            ? item.owner_goods_id.ToString(CultureInfo.InvariantCulture)
                            : "")
                };
                groups[key] = agg;
            }

            agg.Qty += item.goods_qty;
            totalQty += item.goods_qty;
            if (item.pos_id != 0)
                agg.PosIds.Add(item.pos_id);
            else if (!string.IsNullOrEmpty(item.pos_no))
                agg.PosNos.Add(item.pos_no);
        }

        foreach (Agg g in groups.Values.OrderByDescending(x => x.Qty).Take(Mathf.Max(1, topN)))
        {
            int pos = g.PosIds.Count > 0 ? g.PosIds.Count : g.PosNos.Count;
            decimal pct = totalQty > 0m ? g.Qty / totalQty * 100m : 0m;
            table.Add(new List<string>
            {
                g.Name,
                g.No,
                pos.ToString(CultureInfo.InvariantCulture),
                FormatQty(g.Qty),
                $"{pct:0.##}%"
            });
        }

        return table;
    }

    private static string FormatQty(decimal qty)
    {
        if (qty == decimal.Truncate(qty))
            return decimal.Truncate(qty).ToString(CultureInfo.InvariantCulture);
        return qty.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private sealed class Agg
    {
        public string Name;
        public string No;
        public decimal Qty;
        public readonly HashSet<long> PosIds = new();
        public readonly HashSet<string> PosNos = new();
    }

    private void GetOffLineData()
    {
        List<List<string>> data2 = new List<List<string>>()
        {
            new List<string>() { "物料名称", "料号", "货位占用", "实时库存", "库存占比" },
            new List<string>() { "晶圆", "LH1254520145sdsdsdsdsd", "500", "1200", "100" },
            new List<string>() { "晶圆", "LH1254520145sdsdsdsdsd", "500", "1200", "100" },
            new List<string>() { "晶圆", "LH1254520145sdsdsdsdsd", "500", "1200", "100" },
            new List<string>() { "晶圆", "LH1254520145sdsdsdsdsd", "500", "1200", "100" },
            new List<string>() { "晶圆", "LH1254520145sdsdsdsdsd", "500", "1200", "100" },
            new List<string>() { "晶圆", "LH1254520145sdsdsdsdsd", "500", "1200", "100" },
            new List<string>() { "晶圆", "LH1254520145sdsdsdsdsd", "500", "1200", "100" },
            new List<string>() { "晶圆", "LH1254520145sdsdsdsdsd", "500", "1200", "100" },
            new List<string>() { "晶圆", "LH1254520145sdsdsdsdsd", "500", "1200", "100" },
            new List<string>() { "晶圆", "LH1254520145sdsdsdsdsd", "500", "1200", "100" },
        };
        IOCC.PublishWithID("showTable", "关键库存信息", data2, true);
    }
}
}
