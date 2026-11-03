using Newtonsoft.Json.Linq;
using UnityEngine;
using Frame.Core.UI;

/// <summary>
/// 绑定 SliderInputField 组合控件（信号 ID + 数值范围）。
/// JSON 优先英文键 signal / max；兼容旧键「绑定信号」「货架切片数量」。
/// </summary>

namespace Frame.Core.Binding
{
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

        if (TryGetString(obj, out var signal, "signal", "绑定信号"))
        {
            m_sliderInputField.SignalID = signal;
        }

        if (TryGetInt(obj, out var max, "max", "货架切片数量"))
        {
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
                ["signal"] = string.Empty,
                ["max"] = 0
            };
        }

        return new JObject
        {
            ["signal"] = m_sliderInputField.SignalID ?? string.Empty,
            ["max"] = Mathf.RoundToInt(m_sliderInputField.MaxValue)
        };
    }

    private static bool TryGetString(JObject obj, out string value, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj.TryGetValue(key, out var token) && token != null && token.Type != JTokenType.Null)
            {
                value = token.Value<string>();
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryGetInt(JObject obj, out int value, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj.TryGetValue(key, out var token) && token != null && token.Type != JTokenType.Null)
            {
                value = token.Value<int>();
                return true;
            }
        }

        value = 0;
        return false;
    }
}
}
