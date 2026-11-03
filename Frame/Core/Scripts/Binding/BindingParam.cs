using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

//注意:绑定列表时仅Array有效
/// <summary>
/// 参数绑定：自动扫描目标组件上带有 [BindableParam] 特性的字段/属性，
/// 根据 JSON 中的 Key 自动匹配并写入值。
/// 
/// JSON 格式：
/// {
///   "VideoPath": "video1.mp4",
///   "Volume": 0.8,
///   "AutoPlay": true
/// }
/// 
/// 目标脚本示例：
/// public class ElementVideoPlayer : MonoBehaviour
/// {
///     [BindableParam("VideoPath")]
///     private string[] localVideoFilePath;
///     
///     [BindableParam("Volume")]
///     public float volume = 1f;
///     
///     [BindableParam("AutoPlay")]
///     public bool autoPlay = false;
/// }
/// </summary>

namespace Frame.Core.Binding
{
public class BindingParam : BindingToken
{
    /// <summary>批量绑定的特殊标记 Key。</summary>
    public const string BatchModeKey = "*";

    [Tooltip("要修改的目标组件。为空则使用自身。留空或填 '*' 表示批量绑定模式。")]
    [SerializeField] private UnityEngine.Component m_targetComponent;

    [Tooltip("是否作为数组处理。如果目标字段是数组类型，启用此项会将 JSON 数组写入。")]
    [SerializeField] private bool m_asArray;

    public UnityEngine.Component TargetComponent => m_targetComponent != null ? m_targetComponent : this;
    public bool AsArray => m_asArray;

    public override void BindToken(JToken token)
    {
        // Debug.Log($"[BindingParam] Key=\"{Key}\" 绑定");
        //Debug.Log($"[BindingParam] Key=\"{token.ToString()}\" {rootJson.ToString()} ");
        UnityEngine.Component target = TargetComponent;
        if (target == null)
        {
            Debug.LogWarning($"[BindingParam] Key=\"{Key}\" 未设置目标组件");
            return;
        }

        Type targetType = target.GetType();

        // 批量绑定模式：Key 为 "*" 时绑定所有带特性的字段
        if (Key == BatchModeKey && token?.Type == JTokenType.Object)
        {
            JObject obj = (JObject)token;
            BindAllFields(target, targetType, obj);
            return;
        }

        // 单个值绑定：查找匹配的字段
        if (!string.IsNullOrEmpty(Key))
        {
            BindFieldBykey(target, targetType, token);
        }
    }

    /// <summary>
    /// 绑定所有带有 [BindableParam] 特性的字段
    /// </summary>
    private void BindAllFields(UnityEngine.Component target, Type targetType, JObject dataObj)
    {
        // 获取所有带特性的字段和属性
        var members = GetBindableMembers(targetType);

        foreach (var member in members)
        {
            JToken token = JsonBindingHelper.ResolveToken(dataObj, member.Key);
            if (token == null)
            {
                Debug.LogWarning($"[BindingParam] {JsonBindingHelper.FormatMissingKeyMessage(dataObj, member.Key)}");
                continue;
            }

            SetMemberValue(target, member.MemberType, member.Setter, token, m_asArray);
        }
    }

    /// <summary>
    /// 根据 Key 绑定单个字段
    /// </summary>
    private void BindFieldBykey(UnityEngine.Component target, Type targetType, JToken token)
    {
        var members = GetBindableMembers(targetType);
        var member = members.Find(m => m.Key == Key);

        if (member.MemberType == null)
        {
            Debug.LogWarning($"[BindingParam] Key=\"{Key}\" 未在目标组件中找到匹配的字段");
            return;
        }

        SetMemberValue(target, member.MemberType, member.Setter, token, m_asArray);
    }

    /// <summary>
    /// 获取所有带有 [BindableParam] 特性的成员
    /// </summary>
    private List<(string Key, Type MemberType, Action<object, object> Setter)> GetBindableMembers(Type targetType)
    {
        var result = new List<(string, Type, Action<object, object>)>();

        // 获取字段
        foreach (var field in targetType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var attr = field.GetCustomAttribute<BindableParamAttribute>();
            if (attr != null)
            {
                result.Add((attr.Key, field.FieldType, (obj, val) => field.SetValue(obj, val)));
            }
        }

        // 获取属性
        foreach (var prop in targetType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var attr = prop.GetCustomAttribute<BindableParamAttribute>();
            if (attr != null && prop.CanWrite)
            {
                result.Add((attr.Key, prop.PropertyType, (obj, val) => prop.SetValue(obj, val)));
            }
        }

        return result;
    }

    /// <summary>
    /// 设置成员的值
    /// </summary>
    private void SetMemberValue(UnityEngine.Component target, Type memberType, Action<object, object> setter, JToken token, bool asArray)
    {
        try
        {
            object value;

            // 如果目标类型是数组且 JSON 也是数组，自动处理转换
            if (memberType.IsArray && token?.Type == JTokenType.Array)
            {
                JArray array = (JArray)token;
                Array targetArray = Array.CreateInstance(memberType.GetElementType(), array.Count);

                for (int i = 0; i < array.Count; i++)
                {
                    targetArray.SetValue(ConvertValue(array[i], memberType.GetElementType()), i);
                }

                value = targetArray;
            }
            else if (asArray && token?.Type == JTokenType.Array)
            {
                // 显式启用 asArray 模式
                JArray array = (JArray)token;
                Array targetArray = Array.CreateInstance(memberType.GetElementType(), array.Count);

                for (int i = 0; i < array.Count; i++)
                {
                    targetArray.SetValue(ConvertValue(array[i], memberType.GetElementType()), i);
                }

                value = targetArray;
            }
            else
            {
                // 处理单个值
                value = ConvertValue(token, memberType);
            }

            setter(target, value);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BindingParam] 设置值失败：{ex.Message}");
        }
    }


    public override JToken CollectJson()
    {
        UnityEngine.Component target = TargetComponent;
        if (target == null) return null;

        Type targetType = target.GetType();
        var result = new JObject();

        // 收集所有带特性的字段
        var members = GetBindableMembers(targetType);

        foreach (var member in members)
        {
            try
            {
                // 通过反射获取字段或属性的 getter
                object value = GetMemberValue(target, targetType, member.Key);
                if (value != null)
                {
                    result[member.Key] = ConvertToJson(value);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BindingParam] 获取值失败：{ex.Message}");
            }
        }

        // 如果只有一个 Key，返回对应的值而不是整个对象
        if (!string.IsNullOrEmpty(Key) && result.TryGetValue(Key, out JToken token))
        {
            return token;
        }

        return result;
    }

    /// <summary>
    /// 获取成员的值
    /// </summary>
    private object GetMemberValue(UnityEngine.Component target, Type targetType, string key)
    {
        // 获取字段
        foreach (var field in targetType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var attr = field.GetCustomAttribute<BindableParamAttribute>();
            if (attr != null && attr.Key == key)
            {
                return field.GetValue(target);
            }
        }

        // 获取属性
        foreach (var prop in targetType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var attr = prop.GetCustomAttribute<BindableParamAttribute>();
            if (attr != null && attr.Key == key && prop.CanRead)
            {
                return prop.GetValue(target);
            }
        }

        return null;
    }

    /// <summary>
    /// 将 JToken 转换为目标类型的值
    /// </summary>
    private object ConvertValue(JToken token, Type targetType)
    {
        if (token == null)
        {
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        }

        // 处理字符串类型
        if (targetType == typeof(string))
        {
            return token.Type == JTokenType.Object && token["value"] != null
                ? token["value"].ToString()
                : token.ToString();
        }

        // 处理数值类型
        if (targetType == typeof(int) || targetType == typeof(long))
        {
            return token.Type == JTokenType.Object && token["value"] != null
                ? Convert.ToInt64(token["value"].ToString())
                : Convert.ToInt64(token.ToString());
        }

        if (targetType == typeof(float))
        {
            return token.Type == JTokenType.Object && token["value"] != null
                ? Convert.ToSingle(token["value"].ToString())
                : Convert.ToSingle(token.ToString());
        }

        // 处理布尔类型
        if (targetType == typeof(bool))
        {
            return token.Type == JTokenType.Object && token["value"] != null
                ? Convert.ToBoolean(token["value"].ToString())
                : Convert.ToBoolean(token.ToString());
        }

        // 处理枚举
        if (targetType.IsEnum)
        {
            string enumStr = token.Type == JTokenType.Object && token["value"] != null
                ? token["value"].ToString()
                : token.ToString();
            return Enum.Parse(targetType, enumStr);
        }

        // 处理 Unity 特殊类型（Vector3, Color 等）
        if (targetType == typeof(UnityEngine.Vector3))
        {
            if (token.Type == JTokenType.Object && token["value"] != null)
            {
                string[] parts = token["value"].ToString().Split(',');
                if (parts.Length >= 3)
                {
                    return new UnityEngine.Vector3(
                        float.Parse(parts[0]),
                        float.Parse(parts[1]),
                        float.Parse(parts[2])
                    );
                }
            }

            return UnityEngine.Vector3.zero;
        }

        if (targetType == typeof(UnityEngine.Color))
        {
            if (token.Type == JTokenType.Object && token["value"] != null)
            {
                string[] parts = token["value"].ToString().Split(',');
                if (parts.Length >= 3)
                {
                    return new UnityEngine.Color(
                        float.Parse(parts[0]),
                        float.Parse(parts[1]),
                        float.Parse(parts[2])
                    );
                }
            }

            return UnityEngine.Color.white;
        }

        // 默认转换为字符串
        return token.Type == JTokenType.Object && token["value"] != null
            ? token["value"].ToString()
            : token.ToString();
    }

    /// <summary>
    /// 将对象转换为 JToken
    /// </summary>
    private JToken ConvertToJson(object value)
    {
        if (value == null)
        {
            return JValue.CreateNull();
        }

        // 处理数组类型
        if (value is Array array)
        {
            JArray result = new JArray();
            foreach (var item in array)
            {
                result.Add(item?.ToString() ?? "");
            }

            return result;
        }

        // 处理 Unity 特殊类型
        if (value is UnityEngine.Vector3 vec3)
        {
            return new JValue($"{vec3.x},{vec3.y},{vec3.z}");
        }

        if (value is UnityEngine.Color color)
        {
            return new JValue($"{color.r},{color.g},{color.b},{color.a}");
        }

        // 其他类型直接转为字符串
        return new JValue(value.ToString());
    }
}
}
