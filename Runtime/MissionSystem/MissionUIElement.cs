using NonsensicalKit.UGUI.Table;
using TMPro;
using UnityEngine;

namespace NonsensicalKit.Simulation.Mission
{
    public class MissionUIElement : ListTableElement<MissionData>
    {
        [SerializeField] private TextMeshProUGUI m_txt_missionName;
        [SerializeField] private TextMeshProUGUI m_txt_overviewName;

        public override void SetValue(MissionData elementData)
        {
            base.SetValue(elementData);

            m_txt_missionName.text = elementData.Name;
            m_txt_overviewName.text = elementData.Overview;
        }
    }
}
