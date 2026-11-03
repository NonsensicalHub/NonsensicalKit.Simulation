using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 绑定 Toggle.isOn。
/// JSON：bool / 0|1 / "true"|"false"，或 { "value": ... }
/// </summary>

namespace Frame.Core.Binding
{
public class BindingToggle : BindingToken
{
    [SerializeField] private Toggle m_toggle;

    private void Reset()
    {
        m_toggle = GetComponent<Toggle>();
    }

    public override void BindToken(JToken token)
    {
        if (m_toggle == null)
        {
            LogWarning($"{name} 缺少 Toggle");
            return;
        }

        m_toggle.isOn = ReadBool(token);
    }

    public override JToken CollectJson()
    {
        if (m_toggle == null)
        {
            LogWarning($"{name} 缺少 Toggle");
            return new JValue(false);
        }

        return new JValue(m_toggle.isOn);
    }

    private static bool ReadBool(JToken token)
    {
        if (token == null)
        {
            return false;
        }

        JToken value = token.Type == JTokenType.Object ? token["value"] : token;
        if (value == null || value.Type == JTokenType.Null)
        {
            return false;
        }

        switch (value.Type)
        {
            case JTokenType.Boolean:
                return value.Value<bool>();
            case JTokenType.Integer:
            case JTokenType.Float:
                return !Mathf.Approximately(value.Value<float>(), 0f);
            case JTokenType.String:
                string s = value.ToString();
                if (bool.TryParse(s, out bool b))
                {
                    return b;
                }

                if (int.TryParse(s, out int i))
                {
                    return i != 0;
                }

                return !string.IsNullOrEmpty(s);
            default:
                return false;
        }
    }
}
}
