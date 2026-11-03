using System;
using TMPro;
using UnityEngine;

public class WarningDetailCell : MonoBehaviour
{
    [SerializeField] private TMP_Text m_text;
    [SerializeField] private TMP_Text m_text2;

    private void Reset()
    {
        m_text = this.transform.GetChild(0).GetComponent<TMP_Text>();
        m_text2 = this.transform.GetChild(1).GetComponent<TMP_Text>();
    }

    public void SetText((string , string ) info)
    {
        m_text.text = info.Item1;
        m_text2.text = info.Item2;
    }
}
