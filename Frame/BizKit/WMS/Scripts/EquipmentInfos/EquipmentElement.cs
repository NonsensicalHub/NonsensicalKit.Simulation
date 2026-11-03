using NonsensicalKit.Core;
using NonsensicalKit.Core.DagLogicNode;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Frame.Equipment
{
    public class EquipmentElement : NonsensicalMono
    {
        [SerializeField] private string _type;
        [SerializeField] private GameObject _status_Select;
        [SerializeField] private GameObject _status_Normal;

        [SerializeField] private Image m_icon;
        [SerializeField] private TMP_Text m_text_UnSelect;
        [SerializeField] private TMP_Text m_text_Select;

        private void Awake()
        {
            Subscribe<string>("changeEquipmentOptions", ChangeEquipmentOptions);
            Subscribe((int)DagLogicNodeEnum.NodeEnter, "设备管理", OnEnter);
        }

        private void OnEnter()
        {
            Select(false);
        }

        public void Init(string deviceName, Sprite sprite, string etype)
        {
            m_text_Select.text = deviceName;
            m_text_UnSelect.text = deviceName;
            m_icon.sprite = sprite;
            _type = etype;
        }

        public void Select(bool tar = true)
        {
            _status_Select.SetActive(tar);
            _status_Normal.SetActive(!tar);
        }

        public void Unselect()
        {
            Select(false);
        }

        public void ChangeEquipment()
        {
            Publish("changeEquipmentOptions", _type);
        }

        private void ChangeEquipmentOptions(string type)
        {
            if (type == _type)
            {
                Select();
                Publish("showEquipment", _type);
            }
            else
            {
                Unselect();
            }
        }
    }
}
