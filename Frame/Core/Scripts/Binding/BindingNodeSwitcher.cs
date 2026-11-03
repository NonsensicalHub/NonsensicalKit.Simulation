using Newtonsoft.Json.Linq;
using NonsensicalKit.Core.DagLogicNode;
using UnityEngine;

public class BindingNodeSwitcher : BindingToken
{
    [SerializeField] private DagNodeSwitcher m_dagNodeSwitcher;

    public override void BindToken(JToken token)
    {
        if (m_dagNodeSwitcher == null)
        {
            LogWarning($"{name} 缺少 DagNodeSwitcher，无法绑定 Key=\"{Key}\"");
            return;
        }

        m_dagNodeSwitcher.TargetNodeID = token.ToString() ?? string.Empty;
    }

    public override JToken CollectJson()
    {
        if (m_dagNodeSwitcher == null)
        {
            LogWarning($"{name} 缺少 DagNodeSwitcher");
            return new JValue("未配置组件");
        }

        return new JValue(m_dagNodeSwitcher.TargetNodeID);
    }
}
