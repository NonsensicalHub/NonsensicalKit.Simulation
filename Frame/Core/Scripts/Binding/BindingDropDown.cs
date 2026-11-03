using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NonsensicalKit.UGUI;
using TMPro;
using UnityEngine;

public class BindingDropDown : BindingToken
{
    [SerializeField] private TMP_Dropdown m_dropdown;


    public override void BindToken(JToken token)
    {
        if (token.Type == JTokenType.Array)
        {
            var array = token as JArray;
            try
            {
                m_dropdown.InitDropDown(array.ToObject<List<string>>());
            }
            catch (Exception e)
            {
                LogError($"下拉框数据绑定异常: {e.Message}");
                throw;
            }
        }
    }

    public override JToken CollectJson()
    {
        if (m_dropdown == null)
        {
            LogWarning($"{name} 缺少 TMP_Dropdown");
            return new JArray();
        }

        var options = new JArray();
        foreach (var optionData in m_dropdown.options)
        {
            options.Add(optionData?.text ?? string.Empty);
        }

        return options;
    }
}
