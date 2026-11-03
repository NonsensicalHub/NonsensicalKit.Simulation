using NaughtyAttributes;
using NonsensicalKit.Core;
using UnityEngine;
using Frame.Core.Window;


namespace Frame.BizKit.WMS.RealTimeAlarm
{
public class RealTimeAlarmWindow : NonsensicalMono
{
    [SerializeField] private string m_windowName;
    [SerializeField] private GameObject m_selfWindowContentPrefab;


    [SerializeField] private WindowConfig m_windowConfig;
    

    [Button]
    public void OnOpenWindow()
    {
        if (IOCC.TryGet<WindowContainerManager>(m_windowName, out var value))
        {
            value.OpenWindow(m_windowConfig, m_selfWindowContentPrefab, null);
        }
    }
}
}
