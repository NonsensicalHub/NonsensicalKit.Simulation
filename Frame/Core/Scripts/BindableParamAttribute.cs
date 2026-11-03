using System;
using UnityEngine;

/// <summary>
/// 用于标记可被 JSON 绑定的字段或属性。
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class BindableParamAttribute : PropertyAttribute
{
    public string Key { get; }

    public BindableParamAttribute(string key)
    {
        Key = key;
    }
}
