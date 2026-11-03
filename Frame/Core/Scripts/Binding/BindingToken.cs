using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 所有 JSON 绑定组件的基类。
/// 正向：JSON → Prefab  (BindToken)
/// 反向：Prefab → JSON  (CollectJson)
/// </summary>
public abstract class BindingToken : MonoBehaviour
{
    [Tooltip("绑定路径，多级用 '/' 分隔。顶层示例: Title；嵌套示例: Head/Title")]
    [SerializeField] protected string m_key;

    public string Key => m_key;

    protected void LogInfo(string message)
    {
        JsonBindingHelper.ReportBindingLog(JsonBindingReportLevel.Info, this, message);
    }

    protected void LogWarning(string message)
    {
        JsonBindingHelper.ReportBindingLog(JsonBindingReportLevel.Warning, this, message);
    }

    protected void LogError(string message)
    {
        JsonBindingHelper.ReportBindingLog(JsonBindingReportLevel.Error, this, message);
    }

    // ── 正向：将 JSON 数据写入 UI ────────────────────────────
    /// <summary>
    /// 将对应的 JToken 数据绑定到本节点。
    /// 对于容器类型（Array / Pages），实现内部应负责：
    ///   1. 生成子 GameObject
    ///   2. 调用 <see cref="JsonBindingHelper.BindFirstLevel"/> 递归绑定子节点
    /// </summary>
    public abstract void BindToken(JToken token);

    // ── 反向：从 UI 收集数据写回 JSON ───────────────────────
    /// <summary>
    /// 将本节点当前状态序列化为 JToken，用于反向生成 JSON。
    /// 基础实现返回 null，子类按需重写。
    /// </summary>
    public abstract JToken CollectJson();

    /// <summary>
    /// 生成 JSON 骨架占位。Core 的 Schema 工具只认识本基类；
    /// BizKit 绑定组件通过重写此方法提供样例，避免 Core 引用业务类型。
    /// </summary>
    public virtual JToken BuildSampleJson()
    {
        try
        {
            return CollectJson() ?? new JValue("");
        }
        catch
        {
            return new JValue("");
        }
    }
}
