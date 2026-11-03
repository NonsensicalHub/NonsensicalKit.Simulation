using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 绑定 Image.fillAmount（Filled 类型进度/占比）。
/// JSON：0~1 number，或 { "value": 0.75 }
/// </summary>
public class BindingFillAmount : BindingToken
{
    [SerializeField] private Image m_image;

    private void Reset()
    {
        m_image = GetComponent<Image>();
    }

    public override void BindToken(JToken token)
    {
        if (m_image == null)
        {
            LogWarning($"{name} 缺少 Image");
            return;
        }

        if (!TryReadFloat(token, out float value))
        {
            return;
        }

        m_image.fillAmount = Mathf.Clamp01(value);
    }

    public override JToken CollectJson()
    {
        if (m_image == null)
        {
            LogWarning($"{name} 缺少 Image");
            return new JValue(0f);
        }

        return new JValue(m_image.fillAmount);
    }

    private static bool TryReadFloat(JToken token, out float value)
    {
        value = 0f;
        if (token == null)
        {
            return false;
        }

        JToken v = token.Type == JTokenType.Object ? token["value"] : token;
        if (v == null || v.Type == JTokenType.Null)
        {
            return false;
        }

        if (v.Type is JTokenType.Integer or JTokenType.Float)
        {
            value = v.Value<float>();
            return true;
        }

        return float.TryParse(v.ToString(), out value);
    }
}
