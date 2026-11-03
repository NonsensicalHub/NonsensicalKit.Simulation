using System;
using NonsensicalKit.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


namespace Frame.BizKit.WMS
{
/// <summary>检索 IOCC 事件（放在主 BizKit，供结果项与可选 ZCalendar SearchTool 共用）。</summary>
[AggregatorEnum]
public enum SearchToolEvent
{
    InitSearchType,
    Search,
    GetSearchResults,
    SelectResultElement,
    SelectResult,
    FeatureModeChanged,
    FeatureSearch,
}

public class SearchResult : MonoBehaviour
{
   [SerializeField] private Button m_btn_result;
   [SerializeField] private TMP_Text m_txt_result;

    private void Awake()
    {
        m_btn_result.onClick.AddListener(OnSelect);
    }

    public void SetText(string text)
    {
        m_txt_result.text = text;
    }

    private void OnSelect()
    {
        IOCC.Publish(SearchToolEvent.SelectResultElement, m_txt_result.text);
    }
}
}