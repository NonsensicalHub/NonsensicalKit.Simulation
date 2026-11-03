using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 绑定 Graphic / TMP_Text 颜色。
/// JSON："#RRGGBB" / "#RRGGBBAA"，或 { "r","g","b","a" } / { "value": "#..." }
/// </summary>
public class BindingColor : BindingToken
{
    [SerializeField] private Graphic m_graphic;
    [SerializeField] private TMP_Text m_tmpText;

    private void Reset()
    {
        m_graphic = GetComponent<Graphic>();
        m_tmpText = GetComponent<TMP_Text>();
    }

    public override void BindToken(JToken token)
    {
        if (!TryReadColor(token, out Color color))
        {
            LogWarning($"{name} 无法解析颜色: {token}");
            return;
        }

        if (m_tmpText != null)
        {
            m_tmpText.color = color;
        }

        if (m_graphic != null)
        {
            m_graphic.color = color;
        }

        if (m_tmpText == null && m_graphic == null)
        {
            LogWarning($"{name} 缺少 Graphic 或 TMP_Text");
        }
    }

    public override JToken CollectJson()
    {
        Color color = Color.white;
        if (m_tmpText != null)
        {
            color = m_tmpText.color;
        }
        else if (m_graphic != null)
        {
            color = m_graphic.color;
        }
        else
        {
            LogWarning($"{name} 缺少 Graphic 或 TMP_Text");
        }

        return new JValue("#" + ColorUtility.ToHtmlStringRGBA(color));
    }

    private static bool TryReadColor(JToken token, out Color color)
    {
        color = Color.white;
        if (token == null)
        {
            return false;
        }

        if (token.Type == JTokenType.Object)
        {
            if (token["value"] != null)
            {
                return TryParseColorString(token["value"].ToString(), out color);
            }

            float r = token["r"]?.Value<float>() ?? 1f;
            float g = token["g"]?.Value<float>() ?? 1f;
            float b = token["b"]?.Value<float>() ?? 1f;
            float a = token["a"]?.Value<float>() ?? 1f;

            // 兼容 0~255 写法
            if (r > 1f || g > 1f || b > 1f || a > 1f)
            {
                r /= 255f;
                g /= 255f;
                b /= 255f;
                if (token["a"] != null && a > 1f)
                {
                    a /= 255f;
                }
            }

            color = new Color(r, g, b, a);
            return true;
        }

        return TryParseColorString(token.ToString(), out color);
    }

    private static bool TryParseColorString(string s, out Color color)
    {
        color = Color.white;
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        s = s.Trim();
        if (!s.StartsWith("#"))
        {
            s = "#" + s;
        }

        return ColorUtility.TryParseHtmlString(s, out color);
    }
}
