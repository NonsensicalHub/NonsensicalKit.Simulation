using Newtonsoft.Json.Linq;
using UnityEngine;

public class BindingObject : BindingToken
{
    [SerializeField] private GameObject m_root;

    /// <summary>Schema / 收集用根节点：优先配置的 root，否则自身。</summary>
    public GameObject SchemaRoot => m_root != null ? m_root : gameObject;

    public override void BindToken(JToken token)
    {
        if (token.Type == JTokenType.Object)
        {
            JsonBindingHelper.BindChildren(m_root, token as JObject);
        }
    }

    public override JToken CollectJson()
    {
        if (m_root == null)
        {
            LogWarning($"{name} 缺少 root，使用当前节点收集");
            return JsonBindingHelper.CollectFirstChildren(gameObject);
        }

        return JsonBindingHelper.CollectFirstChildren(m_root);
    }
}
