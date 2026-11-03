using System.Collections;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NonsensicalKit.Core.DagLogicNode;
using UnityEngine;

public class BindingNodeMax : BindingToken
{
    [SerializeField] private DagNodeControlMax m_dagControlMax;

    public override void BindToken(JToken token)
    {
        if (m_dagControlMax == null)
        {
            LogWarning($"{name} 缺少 DagNodeControlMax，无法绑定 Key=\"{Key}\"");
            return;
        }

        if (token.Type == JTokenType.Array)
        {
            var array = token as JArray;
            foreach (var VARIABLE in array)
            {
                m_dagControlMax.AddCondition(VARIABLE.ToString(), "ChildSelect");
            }
        }
    }

    public override JToken CollectJson()
    {
        if (m_dagControlMax == null)
        {
            LogWarning($"{name} 缺少 DagNodeControlMax");
            return new JArray();
        }

        var result = new JArray();
        var field = m_dagControlMax.GetType().GetField("m_conditions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field?.GetValue(m_dagControlMax) is not IEnumerable conditions)
        {
            LogWarning($"{name} DagNodeControlMax 无可读取条件列表");
            return result;
        }

        foreach (var condition in conditions)
        {
            string nodeId = ExtractNodeId(condition);
            if (!string.IsNullOrEmpty(nodeId))
            {
                result.Add(nodeId);
            }
        }

        return result;
    }

    private static string ExtractNodeId(object condition)
    {
        if (condition == null) return string.Empty;
        if (condition is string str) return str;

        var type = condition.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        string[] fieldOrPropertyNames = { "NodeId", "nodeId", "m_nodeId", "TargetNodeID", "m_targetNodeID" };

        foreach (string name in fieldOrPropertyNames)
        {
            var property = type.GetProperty(name, flags);
            if (property != null && property.PropertyType == typeof(string))
            {
                return property.GetValue(condition) as string ?? string.Empty;
            }

            var field = type.GetField(name, flags);
            if (field != null && field.FieldType == typeof(string))
            {
                return field.GetValue(condition) as string ?? string.Empty;
            }
        }

        return string.Empty;
    }
}
