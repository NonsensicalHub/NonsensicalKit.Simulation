using System.Collections.Generic;
using System.Text;
using UnityEngine;
using XCharts.Runtime;

/// <summary>
/// XCharts 灌数辅助。在线/离线业务应调用 <see cref="SetSerieValues"/>，
/// 直接改 Serie 数据点（无动画），再 RefreshChart。
/// </summary>
public class XChartSerieBinder : MonoBehaviour
{
    [SerializeField] private BaseChart m_chart;

    private Title _title;
    private XAxis _xAxis;
    private YAxis _yAxis;

    private void Awake()
    {
        EnsureChart();
    }

    public BaseChart EnsureChart()
    {
        if (m_chart == null)
            m_chart = GetComponent<BaseChart>() ?? GetComponentInChildren<BaseChart>(true);
        return m_chart;
    }

    /// <summary>
    /// 按列表顺序写入 Serie Y 值（饼图扇区顺序与列表一致），立刻刷新。
    /// </summary>
    public void SetSerieValues(int index, List<double> datas, List<string> names = null)
    {
        BaseChart chart = EnsureChart();
        if (chart == null)
        {
            Debug.LogWarning($"[{nameof(XChartSerieBinder)}] 未找到 BaseChart，无法灌数。", this);
            return;
        }

        if (datas == null)
            return;

        Serie serie = chart.GetSerie(index);
        if (serie == null)
        {
            Debug.LogWarning($"[{nameof(XChartSerieBinder)}] GetSerie({index}) 为空，series={chart.series.Count}。", this);
            return;
        }

        serie.dataSortType = SerieDataSortType.None;

        int existing = serie.data.Count;
        for (int i = 0; i < datas.Count; i++)
        {
            if (i < existing)
            {
                // 无动画 overload，避免旧值残留在 GetCurrData
                serie.data[i].UpdateData(1, datas[i]);
                serie.data[i].ignore = false;
                if (names != null && i < names.Count)
                    serie.UpdateDataName(i, names[i]);
            }
            else
            {
                string name = names != null && i < names.Count ? names[i] : null;
                serie.AddYData(datas[i], name);
            }
        }

        for (int i = datas.Count; i < serie.data.Count; i++)
            serie.data[i].ignore = true;

        chart.RefreshChart();

        var sb = new StringBuilder();
        for (int i = 0; i < datas.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            string n = names != null && i < names.Count ? names[i] : $"#{i}";
            sb.Append(n).Append('=').Append(datas[i]);
        }

        Debug.Log($"[{nameof(XChartSerieBinder)}] 已写饼图 [{sb}]", this);
    }

    /// <summary>兼容旧调用：转发给 <see cref="SetSerieValues"/>。</summary>
    public void ResetChartData(int index, List<double> datas, List<string> names = null, bool refresh = false)
    {
        SetSerieValues(index, datas, names);
    }

    public void UpdateYData(int index, List<double> datas, bool resetData = false)
    {
        BaseChart chart = EnsureChart();
        if (chart == null || datas == null)
            return;

        Serie serie = chart.GetSerie(index);
        if (serie == null)
            return;

        int limit = serie.data.Count;

        if (resetData)
        {
            for (var a = 0; a < limit; a++)
            {
                serie.data[a].UpdateData(1, 0);
                serie.data[a].ignore = a >= datas.Count;
            }
        }

        for (var i = 0; i < datas.Count; i++)
        {
            if (i < limit)
                serie.data[i].UpdateData(1, datas[i]);
            else
                serie.AddData(datas[i]);
        }

        chart.RefreshChart();
    }

    public void UpdateXData(int index, List<string> datas)
    {
        BaseChart chart = EnsureChart();
        if (chart == null || datas == null)
            return;

        Serie serie = chart.GetSerie(index);
        if (serie == null)
            return;

        int limit = serie.data.Count;
        for (int i = 0; i < datas.Count; i++)
        {
            if (i < limit)
                serie.UpdateDataName(i, datas[i]);
            else
                chart.AddXAxisData(datas[i]);
        }
    }

    public void UpdateXAxisData(List<string> datas, bool resetAndSet = false)
    {
        BaseChart chart = EnsureChart();
        if (chart == null || datas == null)
            return;

        if (resetAndSet)
        {
            (_xAxis ??= chart.EnsureChartComponent<XAxis>()).data.Clear();
            foreach (var t in datas)
                chart.AddXAxisData(t);
        }
        else
        {
            for (var i = 0; i < datas.Count; i++)
                chart.UpdateXAxisData(i, datas[i]);
        }
    }

    public void UpdateYAxisData(int index, List<string> datas, bool resetAndSet = false)
    {
        BaseChart chart = EnsureChart();
        if (chart == null || datas == null)
            return;

        if (resetAndSet)
        {
            (_yAxis ??= chart.EnsureChartComponent<YAxis>()).data.Clear();
            foreach (var t in datas)
                chart.AddYAxisData(t);
        }
        else
        {
            foreach (var t in datas)
                chart.UpdateYAxisData(index, t);
        }
    }

    public void UpdateSeriesName(List<string> names)
    {
        BaseChart chart = EnsureChart();
        if (chart == null || names == null)
            return;

        Debug.Assert(chart.series.Count == names.Count, "名称数据长度不一致");
        for (int i = 0; i < names.Count; i++)
            chart.GetSerie(i).serieName = names[i];
    }

    public void UpdateTitle(string title)
    {
        BaseChart chart = EnsureChart();
        if (chart == null)
            return;
        (_title ??= chart.EnsureChartComponent<Title>()).text = title;
    }

    private void Reset()
    {
        m_chart = GetComponent<BaseChart>() ?? GetComponentInChildren<BaseChart>();
    }
}
