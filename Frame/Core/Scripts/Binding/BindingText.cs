using System;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

/// <summary>
/// 将 JToken 的字符串值绑定到 TMP_Text 组件。
/// JSON 格式：普通字符串 或 { "value": "..." }
/// </summary>
public class BindingText : BindingToken
{
    [SerializeField] private TMP_Text m_txt_text;

    private void Reset()
    {
        m_txt_text = GetComponent<TMP_Text>();
    }

    public override void BindToken(JToken token)
    {
        if (m_txt_text == null)
        {
            LogWarning($"{gameObject}.m_txt_text == null)");
            return;
        }

        string text = token?.Type == JTokenType.Object
            ? token["value"]?.ToString()
            : token?.ToString();

        m_txt_text.text = text ?? "";
    }

    public override JToken CollectJson()
    {
        return new JValue(m_txt_text.text);
    }
}
