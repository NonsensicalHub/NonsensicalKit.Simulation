using NonsensicalKit.Core;
using NonsensicalKit.UGUI.Table;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NonsensicalKit.Temp.InteractQueueSystem
{
    public class InteractMenuElement : ListTableElement<InteractMenuInfo>
    {
        [SerializeField] private TextMeshProUGUI m_txt_name;
        [SerializeField] private Button m_btn_click;
        [SerializeField] private GameObject m_selected;


        protected override void Awake()
        {
            base.Awake();
            m_selected.SetActive(false);
            if (m_btn_click!=null)
            {
                m_btn_click.onClick.AddListener(OnButtonClick);
            }
        }

        public override void SetValue(InteractMenuInfo elementData)
        {
            base.SetValue(elementData);
            m_selected.SetActive(elementData.Selected);
            m_txt_name.text = elementData.MenuName;
        }

        private void OnButtonClick()
        {
            Publish("InteractMenuClick",ElementData.index);
        }
    }
}
