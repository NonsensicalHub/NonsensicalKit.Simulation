using System;
using System.Collections.Generic;
using System.Linq;
using NaughtyAttributes;
using NonsensicalKit.Core;
using NonsensicalKit.UGUI.Table;
using UnityEngine;

public class TableControl : NonsensicalMono
{
    [SerializeField] private ScrollTable m_scrollTable;
    [SerializeField] private RectTransform m_titleContent;
    [SerializeField] private RectTransform m_tableRectTransform;
    [SerializeField] private string m_subscribeTableID;

    [SerializeField, Label("单独标题行"), BindableParam("是否单独标题行")]
    private bool m_separateHeaderRow;

    [SerializeField, Label("标题行高"), BindableParam("标题行高")]
    private float m_titleRowHeight = 30f;

    [BindableParam("默认单元格宽度"), SerializeField]
    private float m_defaultElementWidth = 100f;

    [BindableParam("默认单元格高度"), SerializeField]
    private float m_defaultElementHeight = 30f;

    [SerializeField, Label("指定列宽"), BindableParam("是否指定列宽")]
    private bool m_isFixedColumnWidth = true;

    [SerializeField, Label("列宽"), ShowIf("m_isFixedColumnWidth"), BindableParam("列宽")]
    private float[] m_columnWidths;

    [Header("Element")]
    [SerializeField] private TableElement m_titleElement;

    private RowConfig _titleConfig;
    private Vector2 _mDefaultElementSize = new Vector2(100f, 30f);


    private void Awake()
    {
        _mDefaultElementSize = new Vector2(m_defaultElementWidth, m_defaultElementHeight);
        InitScrollView();

        m_titleElement.gameObject.SetActive(m_separateHeaderRow);

        if (string.IsNullOrEmpty(m_subscribeTableID))
        {
            m_subscribeTableID = "defaultTable";
        }

        EnsureSubscribed();
    }

    private void OnEnable()
    {
        PublishWithID("RefreshTableData", m_subscribeTableID);
    }


    #region 订阅

    public void ChangeSubscribeTableID(string subscribeTableID)
    {
        if (string.IsNullOrEmpty(subscribeTableID))
        {
            Debug.LogWarning("设置表格ID为NUll!");
            return;
        }

        if (m_subscribeTableID.Contains(subscribeTableID)) return;
        Unsubscribe<List<List<string>>, bool>("showTable", m_subscribeTableID, AssemblyTableData);
        m_subscribeTableID = subscribeTableID;
        EnsureSubscribed();
    }

    private void EnsureSubscribed()
    {
        Subscribe<List<List<string>>, bool>("showTable", m_subscribeTableID, AssemblyTableData);
    }

    #endregion

    private void InitScrollView()
    {
        if (m_columnWidths.Length == 0)
        {
            Debug.LogWarning("列宽列表为空，将使用默认Cell宽度计算容器");
        }

        m_scrollTable.SetDefaultSize(_mDefaultElementSize.x, _mDefaultElementSize.y);
    }

    /// <summary>
    /// 组装表格
    /// </summary>
    /// <param name="data">表格数据,外层是一行的数据</param>
    /// <param name="firstIsTitle">首行是否标题</param>
    private void AssemblyTableData(List<List<string>> data, bool firstIsTitle = true)
    {
        bool useAverageWidth = false;
        float averageColumnWidth = 0f;
        List<float> columnWidths = new List<float>();
        if (data == null || data.Count == 0)
        {
            Debug.LogWarning("表格数据为空");
            return;
        }

        CalculateColumnWidths();

        int columnCount = data[0].Count;
        m_titleElement.gameObject.SetActive(firstIsTitle && m_separateHeaderRow);
        if (firstIsTitle && m_separateHeaderRow)
        {
            var titleData = data[0];
            data.RemoveAt(0);
            columnCount = titleData.Count;
            _titleConfig = new RowConfig
            {
                IsTitle = true,
                Index = 0,
                m_Elements =
                {
                    Capacity = titleData.Count
                }
            };
            for (var i = 0; i < titleData.Count; i++)
            {
                _titleConfig.m_Elements.Add(new ElementConfig
                {
                    Value = titleData[i],
                    IsTheLast = i == titleData.Count - 1,
                    Width = useAverageWidth ? averageColumnWidth : columnWidths[i]
                });
            }

            m_titleContent.sizeDelta = useAverageWidth ? new Vector2(averageColumnWidth * titleData.Count + (m_scrollTable.BorderSize.x * titleData.Count + 1), m_titleRowHeight) : new Vector2(columnWidths.Sum() + (m_scrollTable.BorderSize.x * titleData.Count + 1), m_titleRowHeight);

            m_titleElement.SetValue(_titleConfig);
        }

        // 仅有表头、无数据行时（例如在线历史占位）仍要刷空表，避免 data[0] 越界。
        if (data.Count == 0)
        {
            var empty = new Array2<string>(Mathf.Max(columnCount, 1), 0);
            if (m_isFixedColumnWidth && m_columnWidths != null && m_columnWidths.Length == columnCount)
                m_scrollTable.SetTableData(empty, m_columnWidths.ToList());
            else
                m_scrollTable.SetTableData(empty);
            return;
        }

        Array2<string> datas = new Array2<string>(data[0].Count, data.Count);

        for (var index = 0; index < data.Count; index++)
        {
            for (var i = 0; i < data[index].Count; i++)
            {
                datas[i, index] = data[index][i];
            }
        }

        if (m_isFixedColumnWidth && m_columnWidths.Length == data[0].Count)
        {
            m_scrollTable.SetColumnWidths(m_columnWidths.ToList());
            m_scrollTable.SetTableData(datas, m_columnWidths.ToList());
        }
        else
        {
            m_scrollTable.SetTableData(datas);
        }
        //ShakeWindow();

        return;

        void CalculateColumnWidths()
        {
            if (data.Count > 0 && data[0] != null)
            {
                int columnCount = data[0].Count;

                if (columnCount == 0)
                {
                    Debug.LogWarning("表格数据列为空");
                    return;
                }

                if (m_isFixedColumnWidth)
                {
                    if (m_columnWidths.Length != columnCount)
                    {
                        Debug.LogWarning($"表格元素宽度个数不匹配！期望：{columnCount}, 实际：{m_columnWidths.Length}");
                        useAverageWidth = true;
                        averageColumnWidth = _mDefaultElementSize.x;
                    }
                    else
                    {
                        // 预计算每列的实际宽度
                        columnWidths.Capacity = columnCount;
                        useAverageWidth = false;
                        foreach (var widthRatio in m_columnWidths)
                        {
                            columnWidths.Add(widthRatio);
                        }
                    }
                }
                else
                {
                    useAverageWidth = true;
                    averageColumnWidth = _mDefaultElementSize.x;
                }
            }
        }
    }

    private void ShakeWindow()
    {
        m_tableRectTransform.sizeDelta += new Vector2(0.1f, 0.1f);
        NonsensicalInstance.Instance.DelayDoIt(0.1f, () =>
        {
            m_tableRectTransform.sizeDelta -= new Vector2(0.1f, 0.1f);
        });
    }
}

public class RowConfig
{
    public bool IsTitle;
    public int Index;
    public List<ElementConfig> m_Elements = new List<ElementConfig>();
}

public class ElementConfig
{
    public bool IsTheLast;
    public string Value;
    public float Width;
}
