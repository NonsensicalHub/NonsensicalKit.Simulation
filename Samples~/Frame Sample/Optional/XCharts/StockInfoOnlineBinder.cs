using Cysharp.Threading.Tasks;
using Frame.BizKit.WMS.Network;
using Frame.Core.Config;
using Frame.Core.XCharts;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Core.Service.Config;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

/// <summary>
/// 「库存信息」运行时在线灌数（与 BindingToken / JSON 绑定无关）。
/// 直接写 Inspector 绑定的 TMP_Text + XChartSerieBinder。
/// </summary>
public class StockInfoOnlineBinder : MonoBehaviour
{
    [Header("请求")]
    [SerializeField] private TwinStockRealtimeRequest m_request;

    [Header("四周 KPI 文本")]
    [SerializeField] private TMP_Text m_ltTitle;
    [SerializeField] private TMP_Text m_ltCount;
    [SerializeField] private TMP_Text m_lbTitle;
    [SerializeField] private TMP_Text m_lbCount;
    [SerializeField] private TMP_Text m_rtTitle;
    [SerializeField] private TMP_Text m_rtCount;
    [SerializeField] private TMP_Text m_rbTitle;
    [SerializeField] private TMP_Text m_rbCount;
    [SerializeField] private TMP_Text m_rbUnit;
    [SerializeField] private TMP_Text m_cTitle;
    [SerializeField] private TMP_Text m_cCount;

    [Header("饼图")]
    [SerializeField] private XChartSerieBinder m_chart;

    private void Awake()
    {
        if (m_request == null)
            m_request = GetComponent<TwinStockRealtimeRequest>();
        if (m_chart == null)
            m_chart = GetComponentInChildren<XChartSerieBinder>(true);
    }

    private void OnEnable()
    {
        RefreshAsync().Forget();
    }

    private async UniTaskVoid RefreshAsync()
    {
        await WaitTwinModeReadyAsync();
        if (TwinDataSource.IsOffline)
            return;

        if (m_request == null)
        {
            Debug.LogWarning($"[{nameof(StockInfoOnlineBinder)}] 缺少 TwinStockRealtimeRequest。", this);
            return;
        }

        List<TwinStockRealtimeItem> list = await m_request.FetchOnceAsync();
        if (list == null || list.Count == 0)
        {
            Debug.LogWarning($"[{nameof(StockInfoOnlineBinder)}] stock-realtime 无数据。", this);
            return;
        }

        Apply(list[0]);
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

    private void Apply(TwinStockRealtimeItem item)
    {
        Set(m_ltTitle, "库存总数");
        Set(m_ltCount, FormatQty(item.stock_qty));
        Set(m_lbTitle, "待出库");
        Set(m_lbCount, FormatQty(item.wait_out_qty));
        Set(m_rtTitle, "待入库");
        Set(m_rtCount, FormatQty(item.wait_in_qty));
        Set(m_rbTitle, "待移入");
        Set(m_rbCount, FormatQty(item.wait_move_in_qty));
        Set(m_rbUnit, "");
        Set(m_cTitle, "待移出");
        Set(m_cCount, FormatQty(item.wait_move_out_qty));

        if (m_chart != null)
        {
            m_chart.SetSerieValues(0, new List<double>
            {
                (double)item.stock_qty,
                (double)item.wait_out_qty,
                (double)item.wait_in_qty
            }, new List<string> { "库存总数", "待出库", "待入库" });
        }

        Debug.Log(
            $"[{nameof(StockInfoOnlineBinder)}] 已刷新 UI " +
            $"stock={item.stock_qty} out={item.wait_out_qty} in={item.wait_in_qty}",
            this);
    }

    private static void Set(TMP_Text text, string value)
    {
        if (text == null)
            return;
        text.SetText(value ?? "");
        text.ForceMeshUpdate(true);
    }

    private static string FormatQty(decimal qty)
    {
        if (qty == decimal.Truncate(qty))
            return decimal.Truncate(qty).ToString(CultureInfo.InvariantCulture);
        return qty.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
