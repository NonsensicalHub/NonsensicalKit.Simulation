using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 绑定 SliderInputField 组合控件（信号 ID + 数值范围）。
/// JSON：{ "绑定信号": "...", "货架切片数量": n }
/// </summary>
public class BindingSliderInputField : BindingToken
{
    [SerializeField] private SliderInputField m_sliderInputField;

    private void Reset()
    {
        m_sliderInputField = GetComponent<SliderInputField>();
    }

    public override void BindToken(JToken token)
    {
        if (m_sliderInputField == null)
        {
            LogWarning($"{name} 缺少 SliderInputField");
            return;
        }

        if (token == null || token.Type != JTokenType.Object || token is not JObject obj)
        {
            LogWarning($"{name} 期望 Object，实际 {token?.Type}");
            return;
        }

        if (obj.TryGetValue("绑定信号", out var signal))
        {
            m_sliderInputField.SignalID = signal.Value<string>();
        }

        if (obj.TryGetValue("货架切片数量", out var value))
        {
            int max = value.Value<int>();
            m_sliderInputField.SetRange(1, max);
            m_sliderInputField.DefaultValue = max;
        }
    }

    public override JToken CollectJson()
    {
        if (m_sliderInputField == null)
        {
            LogWarning($"{name} 缺少 SliderInputField");
            return new JObject
            {
                ["绑定信号"] = string.Empty,
                ["货架切片数量"] = 0
            };
        }

        return new JObject
        {
            ["绑定信号"] = m_sliderInputField.SignalID ?? string.Empty,
            ["货架切片数量"] = Mathf.RoundToInt(m_sliderInputField.MaxValue)
        };
    }
}
