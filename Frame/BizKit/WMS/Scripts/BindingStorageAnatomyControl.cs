using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

public class BindingStorageAnatomyControl : BindingToken
{
    [SerializeField] private StorageAnatomyControl m_control;

    public override void BindToken(JToken token)
    {
        if (m_control is null) return;
        if (token.Type == JTokenType.Object)
        {
            if (token is JObject obj)
            {
                int count = 1;
                string quantifier = "层";
                if (obj.TryGetValue("货架解剖数量", out var countToken))
                {
                    count = countToken.ToObject<int>();
                }

                if (obj.TryGetValue("货架解剖量词", out var value))
                {
                    quantifier = value.ToString();
                }

                m_control.ChangeOptions(count, quantifier);
            }
        }
    }
    
    public override JToken CollectJson()
    {
        if (m_control is null)
        {
            LogWarning($"{name} 缺少 StorageAnatomyControl");
            return new JObject
            {
                ["货架解剖数量"] = 0,
                ["货架解剖量词"] = ""
            };
        }

        TMP_Dropdown dropdown = m_control.GetComponentInChildren<TMP_Dropdown>();
        if (dropdown == null)
        {
            LogWarning($"{name} 未找到 TMP_Dropdown");
            return new JObject
            {
                ["货架解剖数量"] = 0,
                ["货架解剖量词"] = ""
            };
        }

        int count = dropdown.options.Count;
        string quantifier = "层";
        if (count > 0)
        {
            string text = dropdown.options[0]?.text ?? string.Empty;
            int i = 0;
            while (i < text.Length && char.IsDigit(text[i])) i++;
            quantifier = i < text.Length ? text.Substring(i) : quantifier;
        }

        return new JObject
        {
            ["货架解剖数量"] = count,
            ["货架解剖量词"] = quantifier
        };
    }
}
