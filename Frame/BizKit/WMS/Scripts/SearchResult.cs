using System;
using NonsensicalKit.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SearchResult : MonoBehaviour
{
   [SerializeField] private Button m_btn_result;
   [SerializeField] private TMP_Text m_txt_result;

    private void Awake()
    {
        
        m_btn_result.onClick.AddListener(OnSelect);
    }

    public void SetText(string text)
    {
        m_txt_result.text = text;
    }

    private void OnSelect()
    {
        IOCC.Publish(SearchToolEvent.SelectResultElement,m_txt_result.text);
    }
}
