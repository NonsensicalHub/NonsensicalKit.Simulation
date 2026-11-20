using System.Collections.Generic;
using UnityEngine;
using XCharts.Runtime;

namespace Frame.Sample.XCharts
{

//在xchart中,序列中的数据即serie.data[i] (serieData)是一个数据列表,描述的是一个数据点
//所以在使用xchart更新数据的时候,其实更新的是一个数据点的数据
//因而有了在文档中常出现的 维度(dimension) 的概念,用维度去控制修改何处的参数
//通常数据点意义 0:序号 1:X 2:Y(名称) ,这些维度数据在不同类型表格中意义不一,
//在某些表格中,图例,标签,坐标轴等都可以用维度数据表示,而有些需修改X/YAxis 坐标轴去实现
public class ChartHelper : MonoBehaviour
{
    [SerializeField] private BaseChart m_chart;

    private Title _title;
    private XAxis _xAxis;
    private YAxis _yAxis;

    /// <summary>
    /// 重新添加数据
    /// </summary>
    /// <param name="index">序列号 Serie</param>
    /// <param name="datas">需要更新的数据 </param>
    /// <param name="names">需要更新的数据名称</param>
    /// <param name="refresh">是否刷新表格</param>
    public void ResetChartData(int index, List<double> datas, List<string> names = null, bool refresh = false)
    {
        m_chart.GetSerie(index).data.Clear();

        if (names != null && names.Count == datas.Count)
        {
            Debug.LogWarning("数据长度不一致", this);
        }

        var i = 0;
        foreach (var value in datas)
        {
            if (names != null)
            {
                m_chart.GetSerie(index).AddData(new List<double>() { value }, names[i++]);
            }
            else
            {
                m_chart.GetSerie(index).AddData(value);
            }
        }

        if (refresh) m_chart.RefreshChart();
    }

    /// <summary>
    /// 更新Y数据
    /// </summary>
    /// <summary>
    /// 更新Y数据
    /// </summary>
    /// <param name="index">序列索引</param>
    /// <param name="datas">Y轴数据列表</param>
    /// <param name="resetData">是否重置数据（清除原有数据状态）</param>
    public void UpdateYData(int index, List<double> datas, bool resetData = false)
    {
        var serie = m_chart.GetSerie(index);
        var limit = serie.data.Count;

        if (resetData)
        {
            // 重置原有数据为 0，并设置 ignore 状态
            for (var a = 0; a < limit; a++)
            {
                serie.UpdateData(a, 1, 0);
                serie.data[a].ignore = a >= datas.Count;
            }
        }

        // 更新或新增数据
        for (var i = 0; i < datas.Count; i++)
        {
            if (i < limit)
            {
                serie.UpdateData(i, 1, datas[i]);
            }
            else
            {
                serie.AddData(datas[i]);
            }
        }
    }


    /// <summary>
    /// 更新X数据(数据名称)
    /// </summary>
    /// <param name="index"></param>
    /// <param name="datas"></param>
    public void UpdateXData(int index, List<string> datas)
    {
        var limit = m_chart.GetSerie(index).data.Count;

        for (int i = 0; i < datas.Count; i++)
        {
            if (i < limit)
            {
                m_chart.GetSerie(index).UpdateDataName(i, datas[i]);
            }
            else
            {
                m_chart.AddXAxisData(datas[i]);
            }
        }
    }

    public void UpdateXAxisData(List<string> datas, bool resetAndSet = false)
    {
        if (resetAndSet)
        {
            (_xAxis ??= m_chart.EnsureChartComponent<XAxis>()).data.Clear();
            foreach (var t in datas)
            {
                m_chart.AddXAxisData(t);
            }
        }
        else
        {
            for (var i = 0; i < datas.Count; i++)
            {
                m_chart.UpdateXAxisData(i, datas[i]);
            }
        }
    }

    public void UpdateYAxisData(int index, List<string> datas, bool resetAndSet = false)
    {
        if (resetAndSet)
        {
            (_yAxis ??= m_chart.EnsureChartComponent<YAxis>()).data.Clear();
            foreach (var t in datas)
            {
                m_chart.AddYAxisData(t);
            }
        }
        else
        {
            foreach (var t in datas)
            {
                m_chart.UpdateYAxisData(index, t);
            }
        }
    }

    //更改序列数据名称,在某些情况下可自动生成图例名称
    public void UpdateSeriesName(List<string> names)
    {
        Debug.Assert(m_chart.series.Count == names.Count, "名称数据长度不一致"); //条件不成立抛出异常
        for (int i = 0; i < names.Count; i++)
        {
            m_chart.GetSerie(i).serieName = names[i];
        }
    }

    public void UpdateTitle(string title)
    {
        (_title ??= m_chart.EnsureChartComponent<Title>()).text = title;
    }

    private void Reset()
    {
        m_chart = GetComponent<BaseChart>() ?? GetComponentInChildren<BaseChart>();
    }
}
}
