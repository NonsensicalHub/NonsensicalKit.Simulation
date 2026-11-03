using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 数组绑定：根据 JArray 动态生成子项预制体，并递归绑定每个子项。
/// 每个数组元素必须是 JObject，对应子预制体上的 BindingToken。
/// </summary>
public class BindingArray : BindingToken
{
    [Tooltip("每个数组元素对应的子项预制体。")]
    [SerializeField] protected GameObject m_itemPrefab;

    [Tooltip("子项挂载的父节点。为空则使用自身 Transform。")]
    [SerializeField] protected Transform m_itemContainer;

    public GameObject ItemPrefab => m_itemPrefab;
    public Transform ItemContainer => m_itemContainer != null ? m_itemContainer : transform;

    // ── 正向绑定 ─────────────────────────────────────────────

    public override void BindToken(JToken token)
    {
        if (token?.Type != JTokenType.Array)
        {
            LogWarning($"Key=\"{Key}\" 期望 Array，实际 {token?.Type}");
            return;
        }

        JArray array = (JArray)token;
        var children = SpawnItems(array.Count);

        for (int i = 0; i < children.Count; i++)
        {
            if (array[i] is JObject elementObj)
                JsonBindingHelper.BindFirstLevel(children[i], elementObj);
            else
                LogWarning($"Key=\"{Key}\" 第 {i} 项不是 Object，跳过子绑定");
        }
    }

    // ── 反向收集 ─────────────────────────────────────────────

    public override JToken CollectJson()
    {
        var result = new JArray();
        Transform container = ItemContainer;

        foreach (Transform child in container)
        {
            // 跳过预制体模板本身（通常处于 inactive 状态）
            if (child.gameObject == m_itemPrefab) continue;

            result.Add(JsonBindingHelper.CollectFirstChildren(child.gameObject));
        }

        return result;
    }

    // ── 内部工具 ─────────────────────────────────────────────

    /// <summary>按 count 生成子 GameObject 列表。</summary>
    private List<GameObject> SpawnItems(int count)
    {
        var list = new List<GameObject>();
        m_itemPrefab.SetActive(true);

        for (int i = 0; i < count; i++)
        {
            var go = Instantiate(m_itemPrefab, ItemContainer, false);
            list.Add(go);
        }

        m_itemPrefab.SetActive(false);
        return list;
    }
}