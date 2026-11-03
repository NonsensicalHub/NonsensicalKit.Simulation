using System;
using Newtonsoft.Json.Linq;
using NonsensicalKit.Core.DagLogicNode;
using UnityEngine;

public class BindingNodeActiveType : BindingToken
{
    [SerializeField] private DagNodeControlActive m_dagNodeControl;

    public override void BindToken(JToken token)
    {
        if (m_dagNodeControl == null)
        {
            LogWarning($"{name} 缺少 LogicNodeControlActive，无法绑定 Key=\"{Key}\"");
            return;
        }

        if (Enum.TryParse<DagNodeCheckType>(token.ToString(), out var type))
        {
            m_dagNodeControl.CheckType = type;
        }
    }

    public override JToken CollectJson()
    {
        if (m_dagNodeControl == null)
        {
            LogWarning($"{name} 缺少 LogicNodeControlActive");
            return new JValue("未配置组件");
        }

        return new JValue(m_dagNodeControl.CheckType);
    }
}
