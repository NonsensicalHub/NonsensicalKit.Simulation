using Cysharp.Threading.Tasks;
using Frame.BizKit.WMS;
using Frame.BizKit.WMS.Network;
using Frame.Core.Binding;
using Frame.Core.Config;
using Frame.Core.XCharts;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Core.Service.Config;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using XCharts.Runtime;

/// <summary>
/// 「库龄分布」运行时在线灌数（与 BindingToken / BindingArray 无关）。
/// 拉 stock-age → 写入 Inspector 绑定的行模板克隆 + XChartSerieBinder。
/// </summary>
public class StockAgeOnlineBinder : MonoBehaviour
{
    private static readonly string[] BucketOrder = { "lt90", "d90_360", "gt360" };

    [Header("请求")]
    [SerializeField] private TwinStockAgeRequest m_request;

    [Header("右侧列表（自管克隆，不走 BindingArray）")]
    [SerializeField] private Transform m_rowContainer;
    [SerializeField] private StockAgeRowView m_rowTemplate;

    [Header("饼图（直接挂 PieChart/BaseChart，或挂同物体上的 XChartSerieBinder）")]
    [SerializeField] private BaseChart m_pieChart;
    [SerializeField] private XChartSerieBinder m_chart;

    private readonly List<StockAgeRowView> _rows = new();
    private bool _uiPrepared;

    private void Awake()
    {
        if (m_request == null)
            m_request = GetComponent<TwinStockAgeRequest>();
        if (m_pieChart == null)
            m_pieChart = GetComponentInChildren<BaseChart>(true);
        if (m_chart == null)
            m_chart = GetComponentInChildren<XChartSerieBinder>(true);
        if (m_rowTemplate != null)
            m_rowTemplate.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (m_request != null)
            m_request.DataUpdated += OnRequestDataUpdated;
        RefreshAsync().Forget();
    }

    private void OnDisable()
    {
        if (m_request != null)
            m_request.DataUpdated -= OnRequestDataUpdated;
    }

    private void OnRequestDataUpdated(List<TwinStockAgeItem> list)
    {
        if (TwinDataSource.IsOffline)
            return;
        if (list == null || list.Count == 0)
            return;
        ApplyFetched(list);
    }

    private async UniTaskVoid RefreshAsync()
    {
        await WaitTwinModeReadyAsync();
        if (TwinDataSource.IsOffline)
            return;

        if (m_request == null)
        {
            Debug.LogWarning($"[{nameof(StockAgeOnlineBinder)}] 缺少 TwinStockAgeRequest。", this);
            return;
        }

        List<TwinStockAgeItem> list = await m_request.FetchBucketsAsync(BucketOrder);
        if (list == null || list.Count == 0)
        {
            Debug.LogWarning($"[{nameof(StockAgeOnlineBinder)}] stock-age 无数据。", this);
            return;
        }

        ApplyFetched(list);
    }

    private void ApplyFetched(List<TwinStockAgeItem> list)
    {
        if (!_uiPrepared)
        {
            DetachBindingArrayAndClearForeignRows();
            _uiPrepared = true;
        }

        Apply(list);
    }

    private void DetachBindingArrayAndClearForeignRows()
    {
        if (m_rowContainer == null)
            return;

        BindingArray bindingArray = m_rowContainer.GetComponent<BindingArray>();
        if (bindingArray != null)
            Destroy(bindingArray);

        _rows.Clear();
        for (int i = m_rowContainer.childCount - 1; i >= 0; i--)
        {
            Transform child = m_rowContainer.GetChild(i);
            if (m_rowTemplate != null && child.gameObject == m_rowTemplate.gameObject)
                continue;
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
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

    private void Apply(List<TwinStockAgeItem> items)
    {
        var ordered = new List<TwinStockAgeItem>(BucketOrder.Length);
        decimal total = 0m;
        for (int i = 0; i < BucketOrder.Length; i++)
        {
            string code = BucketOrder[i];
            TwinStockAgeItem hit = items.FirstOrDefault(x =>
                x != null && string.Equals(x.bucket_code, code, StringComparison.OrdinalIgnoreCase));
            if (hit == null)
            {
                hit = new TwinStockAgeItem
                {
                    bucket_code = code,
                    bucket_label = DefaultLabel(code),
                    goods_qty = 0m
                };
            }

            ordered.Add(hit);
            total += hit.goods_qty;
        }

        EnsureRowCount(ordered.Count);

        for (int i = 0; i < _rows.Count && i < ordered.Count; i++)
        {
            TwinStockAgeItem row = ordered[i];
            string title = !string.IsNullOrEmpty(row.bucket_label)
                ? row.bucket_label
                : DefaultLabel(row.bucket_code);
            decimal pct = total > 0m ? row.goods_qty / total * 100m : 0m;
            _rows[i].Set(title, FormatQty(row.goods_qty), $"{pct:0}%");
        }

        ApplyPie(ordered);
        Debug.Log($"[{nameof(StockAgeOnlineBinder)}] 已刷新 UI 行={_rows.Count}", this);
    }

    private void ApplyPie(List<TwinStockAgeItem> ordered)
    {
        var values = new List<double>(ordered.Count);
        var names = new List<string>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            values.Add((double)ordered[i].goods_qty);
            names.Add(!string.IsNullOrEmpty(ordered[i].bucket_label)
                ? ordered[i].bucket_label
                : DefaultLabel(ordered[i].bucket_code));
        }

        if (m_chart != null)
        {
            m_chart.SetSerieValues(0, values, names);
            return;
        }

        if (m_pieChart == null)
            m_pieChart = GetComponentInChildren<BaseChart>(true);

        if (m_pieChart == null)
        {
            Debug.LogWarning($"[{nameof(StockAgeOnlineBinder)}] 未找到饼图 BaseChart。", this);
            return;
        }

        // 无 XChartSerieBinder 时直接写 XCharts
        Serie serie = m_pieChart.GetSerie(0);
        if (serie == null)
        {
            Debug.LogWarning($"[{nameof(StockAgeOnlineBinder)}] 饼图 GetSerie(0) 为空。", this);
            return;
        }

        serie.dataSortType = SerieDataSortType.None;
        for (int i = 0; i < values.Count; i++)
        {
            if (i < serie.data.Count)
            {
                serie.data[i].UpdateData(1, values[i]);
                serie.data[i].ignore = false;
                serie.UpdateDataName(i, names[i]);
            }
            else
            {
                serie.AddYData(values[i], names[i]);
            }
        }

        m_pieChart.RefreshChart();
        Debug.Log(
            $"[{nameof(StockAgeOnlineBinder)}] 直接写饼图 " +
            $"{names[0]}={values[0]}, {names[1]}={values[1]}, {names[2]}={values[2]}",
            this);
    }

    private void EnsureRowCount(int count)
    {
        if (m_rowTemplate == null || m_rowContainer == null)
        {
            Debug.LogWarning($"[{nameof(StockAgeOnlineBinder)}] 未绑定 m_rowTemplate / m_rowContainer。", this);
            return;
        }

        m_rowTemplate.gameObject.SetActive(false);

        while (_rows.Count > count)
        {
            int last = _rows.Count - 1;
            StockAgeRowView extra = _rows[last];
            _rows.RemoveAt(last);
            if (extra != null)
            {
                extra.gameObject.SetActive(false);
                Destroy(extra.gameObject);
            }
        }

        while (_rows.Count < count)
        {
            StockAgeRowView view = Instantiate(m_rowTemplate, m_rowContainer, false);
            view.gameObject.SetActive(true);
            _rows.Add(view);
        }

        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i] != null)
                _rows[i].gameObject.SetActive(true);
        }
    }

    private static string DefaultLabel(string code)
    {
        switch (code)
        {
            case "lt90": return "0-90";
            case "d90_360": return "90-360";
            case "gt360": return "≥360";
            default: return code ?? "";
        }
    }

    private static string FormatQty(decimal qty)
    {
        if (qty == decimal.Truncate(qty))
            return decimal.Truncate(qty).ToString(CultureInfo.InvariantCulture);
        return qty.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
