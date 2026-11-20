using NonsensicalKit.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Frame.BizKit.WMS
{
public class RuleElement : NonsensicalMono
{
    [SerializeField] private TMP_Text m_indexUI;
    [SerializeField] private TMP_InputField m_thresholdDateUI;
    [SerializeField] private Image m_colorUI;

    [SerializeField] private Button m_selectColorBtn;
    [SerializeField] private Button m_deleteBtn;

    private EffEfficientInventoryConfig _thisConfig;

    private void Awake()
    {
        m_thresholdDateUI.onEndEdit.AddListener(OnThresholdDateChange);
        m_selectColorBtn.onClick.AddListener(OnSelectColor);
        m_deleteBtn.onClick.AddListener(OnDelete);
    }

    public void SetValue(EffEfficientInventoryConfig config)
    {
        _thisConfig = config;
        m_indexUI.text = config.Index;
        m_thresholdDateUI.text = config.ThresholdDate;
        m_colorUI.color = config.ToColor();
    }

    private void OnThresholdDateChange(string date)
    {
        m_thresholdDateUI.text = date;
        _thisConfig.ThresholdDate = date;
    }

    private void OnSelectColor()
    {
        if (m_colorUI == null) { return; }

        var backup = m_colorUI.color;
        ColorPicker.Pick(
            backup,
            onConfirm: c =>
            {
                m_colorUI.color = c;
                _thisConfig.SaveColor(c);
            },
            onChanged: c => m_colorUI.color = c,
            onCancel: () => { m_colorUI.color = backup; });
    }

    private void OnDelete()
    {
        if (_thisConfig != null)
        {
            Publish("deleteEfIConfig", _thisConfig);
        }
    }
}
}
