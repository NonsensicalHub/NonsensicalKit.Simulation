using System;
using NonsensicalKit.Core;
using NonsensicalKit.UGUI;
using TMPro;
using UnityEngine;

public class DetailLabelElement : NonsensicalMono
{
    [SerializeField] private TMP_Text m_txt_name;
    [SerializeField] private TMP_Text m_txt_name2;
    [SerializeField] private ToggleButton m_toggle;

    private int _index;
    
    private void Awake()
    {
        m_toggle.m_OnValueChanged.AddListener(OnToggleButtonSwitch);
    }

    public void Init(string text,int index)
    {
        m_txt_name.text = text;
        m_txt_name2.text = text;
        _index = index;
    }

    public void On()
    {
        m_toggle.IsOn = true;
    }

    private void OnToggleButtonSwitch(bool isOn)
    {
        if (isOn)
        {
            Publish(DetailLabelEvent.SelectName,  _index);
        }
    }
}
