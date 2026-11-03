using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using NonsensicalKit.Core;
using NonsensicalKit.UGUI;
using NonsensicalKit.UGUI.Table;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Frame.BizKit.WMS;

/// <summary>检索类型。业务专用项（温度/盘点）保留以兼容现有面板；新项目请优先用 String/DataRange + 事件扩展。</summary>

namespace Frame.BizKit.WMS.ZCalendar
{
public enum SearchType
{
    String,
    DataRange,

    [InspectorName("温度显示")]
    TemperatureVisualization,

    [InspectorName("高效盘点")]
    EfficientInventory
}

[System.Serializable]
public class SearchInfo
{
    public SearchType Type;
    public string Text;
}


public class SearchTool : NonsensicalMono
{
    [SerializeField] private TMP_InputField m_ipf_search;
    [SerializeField] private Button m_btn_search;
    [SerializeField] private ScrollView_MK2 m_resultView;

    [SerializeField] private GameObject m_searchBar;
    [SerializeField] private GameObject m_searchResults;
    [SerializeField] private Button m_btn_showSearchBar;

    [SerializeField] private Image m_img_background;
    [SerializeField] private TMP_Dropdown m_drd_searchType;
    [SerializeField] private ZCalendarHandle m_dateRangeSelector;

    [SerializeField] private List<SearchInfo> m_searchInfos;
    private List<string> _results = new();
    private bool _isShow;

    private void Awake()
    {
        m_btn_search.onClick.AddListener(Search);
        //m_btn_showSearchBar.onClick.AddListener(SwitchBar);
        m_drd_searchType.onValueChanged.AddListener(OnDropdownValueChanged);

        m_resultView.SetUpdateFunc(OnResultUpdate);
        m_resultView.SetItemCountFunc(() => _results.Count);


        Subscribe<List<SearchInfo>>(SearchToolEvent.InitSearchType, InitSearchType);

        Subscribe<List<string>>(SearchToolEvent.GetSearchResults, OnGetResults);

        Subscribe<string>(SearchToolEvent.SelectResultElement, OnSelectResult);
    }

    private void OnEnable()
    {
        m_searchBar.SetActive(false);
        m_dateRangeSelector.ChangeSelf(false);
        m_searchResults.SetActive(false);
        _isShow = false;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        m_btn_search.onClick.RemoveAllListeners();
    }

    private void OnSelectResult(string str)
    {
        m_searchResults.SetActive(false);
        var info = m_searchInfos[m_drd_searchType.value];

        Publish(SearchToolEvent.SelectResult, info.Text, str);
    }

    private void OnResultUpdate(int index, RectTransform rect)
    {
        rect.GetComponent<SearchResult>().SetText(_results[index]);
    }

    private void InitSearchType(List<SearchInfo> infos)
    {
        m_searchInfos = infos;
        var names = from info in infos
            select info.Text;

        m_drd_searchType.InitDropDown(names);
    }

    public void SetSearchTypeInfos(List<SearchInfo> infos)
    {
        if (infos == null || infos.Count == 0)
        {
            Debug.LogWarning($"{nameof(SearchTool)} 初始化搜索条件为空");
            return;
        }

        InitSearchType(infos);
    }

    /// <summary>供 BindingSearchTool 反向导出当前检索类型列表。</summary>
    public IReadOnlyList<SearchInfo> GetSearchTypeInfos() => m_searchInfos;

    private void OnGetResults(List<string> result)
    {
        if (result.Count == 0)
        {
            SearchCargoFalse();
        }
        else
        {
            _results = result;
            m_searchResults.SetActive(true);
            m_resultView.UpdateData(false);
        }
    }

    public void SwitchBar()
    {
        _isShow = !_isShow;
        if (_isShow)
        {
            m_searchBar.SetActive(true);
            OnDropdownValueChanged(m_drd_searchType.value);
        }
        else
        {
            m_searchBar.SetActive(false);
            m_searchResults.SetActive(false);
            m_dateRangeSelector.gameObject.SetActive(false);

            Publish(SearchToolEvent.FeatureModeChanged, SearchType.String, string.Empty);
        }
    }

    private void OnDropdownValueChanged(int arg0)
    {
        if (m_searchInfos == null)
        {
            return;
        }

        var info = m_searchInfos[arg0];
        string libID = string.Empty;
        //TODO: 获取当前所在库区名称,为了适配检索的子功能能正确查找所属组件
        //GetLibID();

        if (info.Type == SearchType.DataRange)
        {
            m_dateRangeSelector.gameObject.SetActive(true);
            m_dateRangeSelector.ChangeSelf(true);
            m_dateRangeSelector.SetTime = (v) => m_ipf_search.text = v;
        }
        else
        {
            CloseDateRangeSelector();
        }

        Publish(SearchToolEvent.FeatureModeChanged, info.Type, libID);
        return;

        void CloseDateRangeSelector()
        {
            m_dateRangeSelector.ChangeSelf(false);
            if (m_dateRangeSelector != null)
            {
                m_dateRangeSelector.SetTime = (v) => v;
            }
        }
    }

    private void Search()
    {
        if (string.IsNullOrEmpty(m_ipf_search.text)) return;

        m_dateRangeSelector.ChangeSelf(false);
        var info = m_searchInfos[m_drd_searchType.value];
        if (info.Type == SearchType.DataRange)
        {
            if (m_dateRangeSelector == null)
            {
                Debug.LogWarning("缺少日期组件");
                return;
            }

            var date = m_dateRangeSelector.GetDatesWithTime();
            //未主动选择日期
            if (date.Contains("0001-01-01")) date = "";
            var temps = date.Split(new char[] { '+' });

            if (temps.Length > 1)
            {
                if (temps[0].Equals(temps[1])) //日期相同
                {
                    var a = m_dateRangeSelector.GetDate()[..11];
                    date = $"{a} 00:00:00+{a} 23:59:59";
                }
                else if (string.IsNullOrEmpty(temps[1]))
                {
                    var a = m_dateRangeSelector.GetDate()[..11].ToString();
                    date = $"{a} 00:00:00+{a} 23:59:59";
                }
            }

            Publish(SearchToolEvent.Search, info.Text, date);
        }
        else if (info.Type == SearchType.EfficientInventory)
        {
            Publish(SearchToolEvent.FeatureSearch, info.Type);
        }
        else
        {
            Publish(SearchToolEvent.Search, info.Text, m_ipf_search.text);
        }
    }
 
    private void SearchCargoFalse()
    {
        this.transform.DOShakePosition(1, strength: 5, fadeOut: true);
        DOTween.To(() => m_img_background.color, c => m_img_background.color = c, Color.red, 1f)
            .SetTarget(m_img_background)
            .OnComplete(() =>
            {
                DOTween.To(() => m_img_background.color, c => m_img_background.color = c, Color.white, 0.5f)
                    .SetTarget(m_img_background);
            });
    }
}
}
