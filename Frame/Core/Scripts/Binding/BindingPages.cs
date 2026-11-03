using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 页面绑定：根据 JArray 动态加载页面预制体并递归绑定内容。
///
/// JSON 结构（单数组，每项同时包含预制体名和内容）：
/// {
///   "Pages": [
///         { "$prefab": "TestPage", "$content": { "文本": "页面1" } },
///         { "$prefab": "TestPage", "$content": { "文本": "页面2" } },
///         { "$prefab": "TestPage", "$content": { "文本": "页面3" } }
///   ]
/// }
///.
/// "$prefab" 字段指定从 Resources 加载的预制体名，"$content"字段作为内容递归绑定。
/// </summary>
public class BindingPages : BindingToken
{
    [Tooltip("页面挂载的父节点。为空则使用自身 Transform。")]
    [SerializeField] protected Transform m_itemContainer;
    
    /// <summary>元素中标识预制体名称的固定字段名。</summary>
    public const string PrefabField = "$prefab";
    
    public Transform ItemContainer => m_itemContainer != null ? m_itemContainer : transform;
    
    // ── 正向绑定 ─────────────────────────────────────────────
    
    public override void BindToken(JToken token)
    {
        if (token?.Type != JTokenType.Array)
        {
            LogWarning($"Key=\"{Key}\" 期望 Array，实际 {token?.Type}");
            return;
        }

        foreach (var element in (JArray)token)
        {
            if (element is not JObject obj)
            {
                LogWarning($"Key=\"{Key}\" 某元素不是 Object，跳过");
                continue;
            }

            string prefabName = obj[PrefabField]?.ToString();
            if (string.IsNullOrEmpty(prefabName))
            {
                LogWarning($"Key=\"{Key}\" 元素缺少 \"{PrefabField}\" 字段，跳过");
                continue;
            }

            var prefab = Resources.Load<GameObject>(prefabName);
            if (prefab == null)
            {
                LogWarning($"Resources.Load 找不到: \"{prefabName}\"");
                continue;
            }

            var go = Instantiate(prefab, ItemContainer, false);

            // $content 子对象作为内容递归绑定
            if (obj["$content"] is JObject content)
                JsonBindingHelper.BindFirstLevel(go, content);
            else
                LogWarning($"Key=\"{Key}\" 元素缺少 \"$content\" 字段，跳过子绑定");
        }
    }

    // ── 反向收集 ─────────────────────────────────────────────

    public override JToken CollectJson()
    {
        var result = new JArray();

        foreach (Transform child in ItemContainer)
        {
            JObject element = new JObject
            {
                [PrefabField] = child.gameObject.name.Replace("(Clone)", "").Trim(),
                ["$content"]  = JsonBindingHelper.CollectFirstChildren(child.gameObject)
            };

            result.Add(element);
        }

        return result;
    }
}
