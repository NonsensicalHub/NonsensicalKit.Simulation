using Newtonsoft.Json.Linq;
using NonsensicalKit.Core.DagLogicNode;
using UnityEngine;

public class BindingNodeActive : BindingToken
{

    [SerializeField] private DagNodeControlActive m_dagNodeControl;

    public override void BindToken(JToken token)
    {
        if (m_dagNodeControl == null)
        {
            LogWarning($"{name} 缺少 DagNodeControlActive，无法绑定 Key=\"{Key}\"");
            return;
        }
        m_dagNodeControl.NodeId = token.ToString() ?? string.Empty;
    }

    public override JToken CollectJson()
    {
        if (m_dagNodeControl == null)
        {
            LogWarning($"{name} 缺少 DagNodeControlActive");
            return new JValue("未配置组件"); ;
        }
        return new JValue(m_dagNodeControl.NodeId);
    }
}
