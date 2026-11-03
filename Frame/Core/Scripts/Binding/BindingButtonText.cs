using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 绑定 Button 上的文案（自身或子节点 TMP_Text）。
/// JSON：字符串 或 { "value": "..." }
/// </summary>
public class BindingButtonText : BindingToken
{
    [SerializeField] private Button m_button;
    [SerializeField] private TMP_Text m_label;

    private void Reset()
    {
        m_button = GetComponent<Button>();
        m_label = GetComponentInChildren<TMP_Text>(true);
    }

    public override void BindToken(JToken token)
    {
        EnsureLabel();
        if (m_label == null)
        {
            LogWarning($"{name} 找不到 Button 文案用的 TMP_Text");
            return;
        }

        string text = token?.Type == JTokenType.Object
            ? token["value"]?.ToString()
            : token?.ToString();

        m_label.text = text ?? string.Empty;
    }

    public override JToken CollectJson()
    {
        EnsureLabel();
        if (m_label == null)
        {
            LogWarning($"{name} 找不到 Button 文案用的 TMP_Text");
            return new JValue(string.Empty);
        }

        return new JValue(m_label.text ?? string.Empty);
    }

    private void EnsureLabel()
    {
        if (m_label != null)
        {
            return;
        }

        if (m_button != null)
        {
            m_label = m_button.GetComponentInChildren<TMP_Text>(true);
        }
        else
        {
            m_label = GetComponentInChildren<TMP_Text>(true);
        }
    }
}
