using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

/// <summary>
/// 绑定 TMP_InputField 文本。
/// JSON：字符串 或 { "value": "..." }
/// </summary>

namespace Frame.Core.Binding
{
public class BindingInputField : BindingToken
{
    [SerializeField] private TMP_InputField m_inputField;

    private void Reset()
    {
        m_inputField = GetComponent<TMP_InputField>();
    }

    public override void BindToken(JToken token)
    {
        if (m_inputField == null)
        {
            LogWarning($"{name} 缺少 TMP_InputField");
            return;
        }

        string text = token?.Type == JTokenType.Object
            ? token["value"]?.ToString()
            : token?.ToString();

        m_inputField.text = text ?? string.Empty;
    }

    public override JToken CollectJson()
    {
        if (m_inputField == null)
        {
            LogWarning($"{name} 缺少 TMP_InputField");
            return new JValue(string.Empty);
        }

        return new JValue(m_inputField.text ?? string.Empty);
    }
}
}
