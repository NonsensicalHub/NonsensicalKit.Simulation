using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 绑定 Slider 数值（可选手动设置 min/max）。
/// JSON：number，或 { "value": 0.5, "min"?: 0, "max"?: 1 }
/// </summary>

namespace Frame.Core.Binding
{
public class BindingSlider : BindingToken
{
    [SerializeField] private Slider m_slider;

    private void Reset()
    {
        m_slider = GetComponent<Slider>();
    }

    public override void BindToken(JToken token)
    {
        if (m_slider == null)
        {
            LogWarning($"{name} 缺少 Slider");
            return;
        }

        if (token == null)
        {
            return;
        }

        if (token.Type == JTokenType.Object)
        {
            if (token["min"] != null && float.TryParse(token["min"].ToString(), out float min))
            {
                m_slider.minValue = min;
            }

            if (token["max"] != null && float.TryParse(token["max"].ToString(), out float max))
            {
                m_slider.maxValue = max;
            }

            if (TryReadFloat(token["value"] ?? token, out float value))
            {
                m_slider.value = value;
            }

            return;
        }

        if (TryReadFloat(token, out float v))
        {
            m_slider.value = v;
        }
    }

    public override JToken CollectJson()
    {
        if (m_slider == null)
        {
            LogWarning($"{name} 缺少 Slider");
            return new JValue(0f);
        }

        // 若范围非默认 0~1，导出完整对象，便于往返
        if (!Mathf.Approximately(m_slider.minValue, 0f) || !Mathf.Approximately(m_slider.maxValue, 1f))
        {
            return new JObject
            {
                ["value"] = m_slider.value,
                ["min"] = m_slider.minValue,
                ["max"] = m_slider.maxValue
            };
        }

        return new JValue(m_slider.value);
    }

    private static bool TryReadFloat(JToken token, out float value)
    {
        value = 0f;
        if (token == null || token.Type == JTokenType.Null)
        {
            return false;
        }

        if (token.Type is JTokenType.Integer or JTokenType.Float)
        {
            value = token.Value<float>();
            return true;
        }

        return float.TryParse(token.ToString(), out value);
    }
}
}
