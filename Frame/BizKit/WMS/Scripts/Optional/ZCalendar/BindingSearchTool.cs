using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Frame.Core.Binding;

/// <summary>
/// 绑定 SearchTool 的检索类型列表。
/// JSON：[{ "Text": "...", "Type": 0 或 "枚举名" }, ...]
/// </summary>

namespace Frame.BizKit.WMS.ZCalendar
{
public class BindingSearchTool : BindingToken
{
    [SerializeField] private SearchTool m_searchTool;

    private void Reset()
    {
        m_searchTool = GetComponent<SearchTool>();
    }

    public override void BindToken(JToken token)
    {
        if (m_searchTool == null)
        {
            LogWarning($"{name} 缺少 SearchTool，无法绑定");
            return;
        }

        if (token == null || token.Type != JTokenType.Array)
        {
            LogWarning($"{name} SearchTool 绑定数据应为数组");
            return;
        }

        var infos = new List<SearchInfo>();
        foreach (var item in token)
        {
            if (item == null || item.Type != JTokenType.Object)
            {
                continue;
            }

            var obj = (JObject)item;
            var text = obj["Text"]?.ToString();
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            var type = SearchType.String;
            var typeToken = obj["Type"];
            if (typeToken != null)
            {
                if (typeToken.Type == JTokenType.Integer)
                {
                    type = (SearchType)typeToken.Value<int>();
                }
                else if (typeToken.Type == JTokenType.String)
                {
                    var typeStr = typeToken.ToString();
                    if (int.TryParse(typeStr, out var intType))
                    {
                        type = (SearchType)intType;
                    }
                    else if (System.Enum.TryParse(typeStr, true, out SearchType enumType))
                    {
                        type = enumType;
                    }
                }
            }

            infos.Add(new SearchInfo
            {
                Text = text,
                Type = type
            });
        }

        m_searchTool.SetSearchTypeInfos(infos);
    }

    public override JToken CollectJson()
    {
        var result = new JArray();
        if (m_searchTool == null)
        {
            LogWarning($"{name} 缺少 SearchTool");
            return result;
        }

        var infos = m_searchTool.GetSearchTypeInfos();
        if (infos == null)
        {
            return result;
        }

        foreach (var info in infos)
        {
            if (info == null || string.IsNullOrEmpty(info.Text))
            {
                continue;
            }

            result.Add(new JObject
            {
                ["Text"] = info.Text,
                ["Type"] = info.Type.ToString()
            });
        }

        return result;
    }

    public override JToken BuildSampleJson()
    {
        return new JArray
        {
            new JObject { ["Text"] = "示例检索", ["Type"] = "String" }
        };
    }
}
}
