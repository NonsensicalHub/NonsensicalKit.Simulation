using Frame.BizKit.WMS;
using Frame.BizKit.WMS.ZCalendar;
using NonsensicalKit.Core;
using UnityEngine;

namespace Frame.BizKit.WMS.ZCalendar
{
/// <summary>
/// 把 SearchTool 的检索类型路由到温度可视化 / 高效盘点（与 SearchTool 解耦的可选胶水）。
/// </summary>
public class SearchToolFeatureRouter : NonsensicalMono
{
    [SerializeField] private TemperatureVisualManager m_temperatureVisualManager;
    [SerializeField] private EffEfficientInventoryManager m_efficientInventoryManager;

    private void Awake()
    {
        Subscribe<SearchType, string>(SearchToolEvent.FeatureModeChanged, OnModeChanged);
        Subscribe<SearchType>(SearchToolEvent.FeatureSearch, OnFeatureSearch);
    }

    private void OnModeChanged(SearchType type, string libID)
    {
        switch (type)
        {
            case SearchType.TemperatureVisualization:
                m_efficientInventoryManager?.Close();
                m_temperatureVisualManager?.Open(libID);
                break;
            case SearchType.EfficientInventory:
                m_temperatureVisualManager?.Close();
                m_efficientInventoryManager?.Open(libID);
                break;
            default:
                CloseFeatures();
                break;
        }
    }

    private void OnFeatureSearch(SearchType type)
    {
        if (type == SearchType.EfficientInventory)
        {
            m_efficientInventoryManager?.Search();
        }
    }

    private void CloseFeatures()
    {
        m_temperatureVisualManager?.Close();
        m_efficientInventoryManager?.Close();
    }
}
}
