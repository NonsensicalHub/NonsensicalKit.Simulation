using NonsensicalKit.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DefaultUnitPage : UnitUIPage
{
    [SerializeField] private Image m_img_preview;
    [SerializeField] private TMP_Text m_txt_type;
    [SerializeField] private TMP_Text m_txt_description;

    public override void ShowUnitUI(UnitBase unit)
    {
        OpenSelf();

        m_txt_type.text = unit.Data.TypeName;
        m_txt_description.text = unit.Data.Description;
    }
}
