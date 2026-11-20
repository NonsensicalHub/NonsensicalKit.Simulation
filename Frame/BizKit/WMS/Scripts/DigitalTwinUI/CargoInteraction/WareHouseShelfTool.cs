using System;
using Frame.Core.UI;
using NaughtyAttributes;
using NonsensicalKit.Core;
using NonsensicalKit.Core.DagLogicNode;
using UnityEngine;
using UnityEngine.Events;

namespace Frame.BizKit.WMS
{
public class WareHouseShelfTool : NonsensicalMono
{
    [SerializeField, Label("监听仓库")] private string m_signalID = "一号库";

    [SerializeField] private SliderInputField m_sliderInput;

    [SerializeField] private UnityEvent<float> m_onShelfChange = new UnityEvent<float>();
    [SerializeField] private UnityEvent m_onResetToDefault = new UnityEvent();

    private SliderInputField SliderInput
    {
        get
        {
            m_sliderInput ??= IOCC.Get<SliderInputField>();
            if (m_sliderInput == null)
                Debug.LogWarning("Can't find SliderInput field");
            return m_sliderInput;
        }
    }

    private void Awake()
    {
        Subscribe("SliderResetToDefault", ResetToDefault);
        Subscribe<float>("SliderValueChanged", m_signalID, OnShelfChange);
        Subscribe(DagLogicNodeEnum.NodeExit, "库存管理", OnNodeExit);
    }

    private void OnShelfChange(float obj)
    {
        if (Equals(obj, m_sliderInput?.DefaultValue))
        {
            m_onResetToDefault?.Invoke();
            Publish("OnShelfResetToDefault");
            return;
        }
        m_onShelfChange?.Invoke(obj);
        Publish("OnShelfChange");
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        m_sliderInput = null;
    }

    public void ResetToDefault()
    {
        SliderInput?.ResetToDefault();
    }

    public void ChangeShelfLayer(float column)
    {
        m_onShelfChange?.Invoke(column);
        PublishWithID<int>("ShelfColumnChange", m_signalID, (int)column);
        SliderInput?.SetValue(column, false);
    }

    private void OnNodeExit()
    {
        Publish("ResetShelfState"); //通知货架控制器
        m_onResetToDefault?.Invoke();
        Publish("OnShelfResetToDefault");
    }
}
}
