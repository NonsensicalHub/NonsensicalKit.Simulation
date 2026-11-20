using NaughtyAttributes;
using NonsensicalKit.Core;
using UnityEngine;

namespace Frame.BizKit.WMS
{
public class GameObjectControl : NonsensicalMono
{
    [SerializeField] private bool m_useSignal;

    [ShowIf("m_useSignal"), SerializeField]
    private string m_subscribeShowSignal;

    [ShowIf("m_useSignal"), SerializeField]
    private string m_subscribeHideSignal;

    [ShowIf("m_useSignal"), SerializeField]
    private string m_subscribeSwitchSignal;

    [SerializeField] private GameObject[] m_gameObjects;

    private void Awake()
    {
        if (!m_useSignal) return;
        if (!string.IsNullOrEmpty(m_subscribeShowSignal))
            Subscribe(m_subscribeShowSignal, OnShow);
        if (!string.IsNullOrEmpty(m_subscribeHideSignal))
            Subscribe(m_subscribeHideSignal, OnHide);
        if (!string.IsNullOrEmpty(m_subscribeSwitchSignal))
            Subscribe<bool>(m_subscribeSwitchSignal, OnSwitch);
    }


    public void OnShow()
    {
        OnSwitch(true);
    }

    public void OnHide()
    {
        OnSwitch(false);
    }

    public void OnSwitch(bool isOn)
    {
        foreach (var o in m_gameObjects)
        {
            o.SetActive(isOn);
        }
    }
}
}
