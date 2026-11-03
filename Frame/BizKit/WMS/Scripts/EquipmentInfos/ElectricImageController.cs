using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Frame.Equipment
{
    public class ElectricImageController : MonoBehaviour
    {
        [SerializeField] private GameObject obj;
        [SerializeField] private Image[] imgs;
        [SerializeField] private Sprite[] sprites;


        [SerializeField] private Color _ok;
        [SerializeField] private Color _error;
        [SerializeField] private Color _font_ok;
        [SerializeField] private Color _font_error;


        [SerializeField] private TMP_Text m_electricText;

        public void SetInfo(string info)
        {
            int a = int.Parse(info);
            Set(true);

            for (int i = 0; i < imgs.Length; i++)
            {
                imgs[i].sprite = a > 35 ? sprites[0] : sprites[1];
                m_electricText.color = a > 35 ? _font_ok : _font_error;
            }

            int index = (int)(a / 16.7f) + 1;
            for (int i = 0; i < imgs.Length; i++)
            {
                if (i < index)
                {
                    imgs[i].color = _ok;
                }
                else
                {
                    imgs[i].color = _error;
                }
            }
        }

        private void Set(bool ta)
        {
            obj.SetActive(ta);
        }
    }
}
