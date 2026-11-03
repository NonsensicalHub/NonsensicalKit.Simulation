using System.Collections.Generic;
using NonsensicalKit.UGUI.Table;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TableElement : MonoBehaviour
{
    [SerializeField] private Image m_bgImage;

    [SerializeField] private GameObject m_elementPrefab;
    [SerializeField] private GameObject m_linePrefab;


    private readonly Queue<TMP_Text> _activeTexts = new Queue<TMP_Text>();

    private readonly List<TMP_Text> _texts = new List<TMP_Text>();


    public void SetValue(RowConfig data)
    {
        foreach (var elementObj in _texts)
        {
            _activeTexts.Enqueue(elementObj);
            elementObj.gameObject.SetActive(false);
            elementObj.text = string.Empty;
        }

        _texts.Clear();
        foreach (var element in data.m_Elements)
        {
            var elementObj = GetTMPTexts(element.IsTheLast);
            _texts.Add(elementObj);
            elementObj.gameObject.SetActive(true);
            elementObj.text = element.Value;
            elementObj.transform.parent.GetComponent<RectTransform>().sizeDelta = new Vector2(element.Width, elementObj.rectTransform.sizeDelta.y);
        }
    }

    private TMP_Text GetTMPTexts(bool isLast = false)
    {
        if (_activeTexts.Count > 0)
        {
            return _activeTexts.Dequeue();
        }
        else
        {
            var elementObj = Instantiate(m_elementPrefab, transform);
            if (!isLast && m_linePrefab != null)
            {
                var lineObj = Instantiate(m_linePrefab, transform);
                lineObj.SetActive(true);
            }

            elementObj.SetActive(true);
            return elementObj.GetComponentInChildren<TMP_Text>();
        }
    }
}
