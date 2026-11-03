using NonsensicalKit.Core;
using UnityEngine;

public class MonitoringWindow : NonsensicalMono
{
    [SerializeField] private string m_windowName;
    [SerializeField] private GameObject m_selfWindowContentPrefab;


    [SerializeField] private WindowConfig m_windowConfig;

    private ElementVideoInfo _infoTemp;


    private void Awake()
    {
        Subscribe<ElementVideoInfo>("ShowMonitoringWindow", OnOpenWindow);
    }

    private void OnOpenWindow(ElementVideoInfo info)
    {
        if (info.Equals(_infoTemp)) return;
        
        _infoTemp = info;
        if (IOCC.TryGet<WindowContainerManager>(m_windowName, out var value))
        {
            m_windowConfig.Title = $"{_infoTemp.Serial}_{_infoTemp.Description}";
            value.OpenWindow(m_windowConfig, m_selfWindowContentPrefab, _infoTemp);
        }
    }
}
