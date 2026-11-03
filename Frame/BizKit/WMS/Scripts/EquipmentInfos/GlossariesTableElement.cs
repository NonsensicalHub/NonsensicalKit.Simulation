using NonsensicalKit.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Frame.Equipment
{
    public class GlossariesTableElement : NonsensicalMono
    {
        [SerializeField] private Image _bg;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text description_status;
        [SerializeField] private TMP_Text description_info;
        [SerializeField] private TMP_Text description_electric;
        [SerializeField] private ElectricImageController electriccontroller;
        [SerializeField] private EquipmentStatusImage equipmentStatusImage;

        public void SetValue(GlossariesTableElementData elementData)
        {
            title.text = elementData.title;
            _bg.enabled = this.transform.GetSiblingIndex() % 2 == 0;
            switch (elementData._type)
            {
                case GlossariesInfoType.status:
                    description_status.text = elementData.description;
                    equipmentStatusImage.EquipmentID = elementData.deviceID;
                    Set(true);
                    //PublishWithID("setEquipmentStatus", "1002", _littleTitle.text, info?.ErrorState == 0 ? 0 : 2);
                    break;
                case GlossariesInfoType.info:
                    description_info.text = elementData.description;
                    Set(false);
                    break;
                case GlossariesInfoType.electricQuantity:
                    description_status.gameObject.SetActive(false);
                    description_info.gameObject.SetActive(false);

                    if (elementData.description.Contains("%"))
                    {
                        description_electric.text = elementData.description;
                    }
                    else
                    {
                        description_electric.text = elementData.description + "%";
                    }

                    electriccontroller.SetInfo(elementData.description.Replace("%", ""));

                    break;

                default:
                    break;
            }
        }

        private void Set(bool ta)
        {
            description_status.gameObject.SetActive(ta);
            description_info.gameObject.SetActive(!ta);
        }
    }
}
