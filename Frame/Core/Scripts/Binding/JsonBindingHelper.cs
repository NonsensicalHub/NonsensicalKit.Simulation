using Newtonsoft.Json.Linq;
using UnityEngine;
using System.Collections.Generic;
using System;


namespace Frame.Core.Binding
{
public enum JsonBindingReportLevel
{
    Info,
    Success,
    Warning,
    Error
}

public sealed class JsonBindingReport
{
    public int BoundCount { get; private set; }
    public int MissingCount { get; private set; }
    public int EmptyKeyCount { get; private set; }
    public int WarningCount { get; private set; }
    public int ErrorCount { get; private set; }

    public Action<JsonBindingReportLevel, BindingToken, string> OnLog;

    public void RecordBound(BindingToken binding, string msg = null)
    {
        // 成功只计数，不逐条打日志
        BoundCount++;
    }

    public void RecordMissing(BindingToken binding, string msg)
    {
        MissingCount++;
        RecordWarning(binding, msg);
    }

    public void RecordEmptyKey(BindingToken binding, string msg)
    {
        EmptyKeyCount++;
        RecordWarning(binding, msg);
    }

    public void RecordWarning(BindingToken binding, string msg)
    {
        WarningCount++;
        OnLog?.Invoke(JsonBindingReportLevel.Warning, binding, msg);
    }

    public void RecordError(BindingToken binding, string msg)
    {
        ErrorCount++;
        OnLog?.Invoke(JsonBindingReportLevel.Error, binding, msg);
    }

    public void RecordInfo(BindingToken binding, string msg)
    {
        OnLog?.Invoke(JsonBindingReportLevel.Info, binding, msg);
    }
}

/// <summary>
/// 正向绑定 / 反向收集的共享递归逻辑。
/// 运行时与编辑器均可使用。
/// </summary>
public static class JsonBindingHelper
{
    [ThreadStatic] private static Stack<JsonBindingReport> s_reportStack;

    private static JsonBindingReport CurrentReport =>
        s_reportStack != null && s_reportStack.Count > 0 ? s_reportStack.Peek() : null;

    private static void PushReport(JsonBindingReport report)
    {
        if (report == null) return;
        s_reportStack ??= new Stack<JsonBindingReport>();
        s_reportStack.Push(report);
    }

    private static void PopReport(JsonBindingReport report)
    {
        if (report == null || s_reportStack == null || s_reportStack.Count == 0) return;
        if (ReferenceEquals(s_reportStack.Peek(), report))
            s_reportStack.Pop();
    }

    public static void ReportBindingLog(JsonBindingReportLevel level, BindingToken binding, string message, bool forwardToReport = true)
    {
        string prefix = binding == null ? "[JsonBinding] " : $"[JsonBinding][{binding.GetType().Name}] ";
        string full = prefix + message;

        switch (level)
        {
            case JsonBindingReportLevel.Warning:
                Debug.LogWarning(full, binding);
                break;
            case JsonBindingReportLevel.Error:
                Debug.LogError(full, binding);
                break;
            default:
                Debug.Log(full, binding);
                break;
        }

        if (!forwardToReport) return;
        JsonBindingReport activeReport = CurrentReport;
        if (activeReport == null) return;

        switch (level)
        {
            case JsonBindingReportLevel.Warning:
                activeReport.RecordWarning(binding, message);
                break;
            case JsonBindingReportLevel.Error:
                activeReport.RecordError(binding, message);
                break;
            case JsonBindingReportLevel.Success:
                activeReport.RecordBound(binding, message);
                break;
            default:
                activeReport.RecordInfo(binding, message);
                break;
        }
    }

    // ── 正向：JSON → Prefab ───────────────────────────────────

    /// <summary>
    /// 在 <paramref name="go"/> 下查找所有"路径上第一个" BindingToken，
    /// 按各自 Key 从 <paramref name="dataObj"/> 取值，调用 BindToken。
    /// </summary>
    public static void BindFirstLevel(GameObject go, JObject dataObj)
    {
        BindFirstLevel(go, dataObj, null);
    }

    public static void BindFirstLevel(GameObject go, JObject dataObj, JsonBindingReport report)
    {
        PushReport(report);
        JsonBindingReport activeReport = CurrentReport;

        var bindings = GetFirstLevelComponents<BindingToken>(go.transform);
        try
        {
            foreach (var binding in bindings)
            {
                if (string.IsNullOrEmpty(binding.Key))
                {
                    string msg = $"{go.name} 下某 BindingToken Key 为空，跳过";
                    ReportBindingLog(JsonBindingReportLevel.Warning, binding, msg, false);
                    activeReport?.RecordEmptyKey(binding, "Key 为空，跳过");
                    continue;
                }

                JToken token = ResolveToken(dataObj, binding.Key);
                if (token == null)
                {
                    string missMsg = FormatMissingKeyMessage(dataObj, binding.Key);
                    ReportBindingLog(JsonBindingReportLevel.Warning, binding, missMsg, false);
                    activeReport?.RecordMissing(binding, missMsg);
                    continue;
                }

                try
                {
                    // BindingToken 子类自己负责递归（Array / Pages 内部调用 BindChildren）
                    binding.BindToken(token);
                    activeReport?.RecordBound(binding);
                }
                catch (Exception e)
                {
                    Debug.LogException(e, binding);
                    activeReport?.RecordError(binding, $"Key=\"{binding.Key}\" 绑定异常: {e.Message}");
                }
            }
        }
        finally
        {
            PopReport(report);
        }
    }
    
    /// <summary>
    /// 绑定 <paramref name="go"/> 下所有 BindingToken。
    /// 遇到挂有 BindingPages / BindingArray 的节点时仍绑定该节点本身，但不再深入其子节点
    /// （子树由 Pages/Array 自行管理）。
    /// </summary>
    public static void BindChildren(GameObject go, JObject dataObj)
    {
        JsonBindingReport activeReport = CurrentReport;
        var bindings = GetBindableChildren(go.transform);

        foreach (var binding in bindings)
        {
            if (string.IsNullOrEmpty(binding.Key))
            {
                string msg = $"{go.name} 下{binding.name}的 BindingToken Key 为空，跳过";
                ReportBindingLog(JsonBindingReportLevel.Warning, binding, msg, false);
                activeReport?.RecordEmptyKey(binding, "Key 为空，跳过");
                continue;
            }

            JToken token = ResolveToken(dataObj, binding.Key);
            if (token == null)
            {
                string missMsg = FormatMissingKeyMessage(dataObj, binding.Key);
                ReportBindingLog(JsonBindingReportLevel.Warning, binding, missMsg, false);
                activeReport?.RecordMissing(binding, missMsg);
                continue;
            }

            try
            {
                binding.BindToken(token);
                activeReport?.RecordBound(binding);
            }
            catch (Exception e)
            {
                Debug.LogException(e, binding);
                activeReport?.RecordError(binding, $"Key=\"{binding.Key}\" 绑定异常: {e.Message}");
            }
        }
    }

    /// <summary>
    /// 从 root 出发收集需由 BindingObject 绑定的 BindingToken：
    /// 不含 root 自身；遇到 BindingPages / BindingArray 则收录该节点组件后剪枝。
    /// </summary>
    private static List<BindingToken> GetBindableChildren(Transform root)
    {
        var results = new List<BindingToken>();
        var queue = new Queue<Transform>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            Transform current = queue.Dequeue();

            if (current != root)
            {
                BindingToken[] tokens = current.GetComponents<BindingToken>();
                if (tokens.Length > 0)
                    results.AddRange(tokens);
            }

            // BindingPages / BindingArray 自行管理子树，跳过其子节点
            if (current.GetComponent<BindingPages>() != null
                || current.GetComponent<BindingArray>() != null)
            {
                continue;
            }

            for (int i = 0; i < current.childCount; i++)
                queue.Enqueue(current.GetChild(i));
        }

        return results;
    }

    // ── 反向：Prefab → JSON ───────────────────────────────────
    /// <summary>
    /// 收集 <paramref name="root"/> 下所有"路径上第一个" BindingToken 的值，
    /// 汇总为 JObject 返回。
    /// </summary>
    /// <param name="root">根节点</param>
    public static JObject CollectFirstChildren(GameObject root)
    {
        return CollectFirstChildren(root, null);
    }

    public static JObject CollectFirstChildren(GameObject root, JsonBindingReport report)
    {
        PushReport(report);
        JsonBindingReport activeReport = CurrentReport;
        var result = new JObject();
        var bindings = GetFirstLevelComponents<BindingToken>(root.transform);
        try
        {
            foreach (var binding in bindings)
            {
                if (string.IsNullOrEmpty(binding.Key))
                {
                    ReportBindingLog(JsonBindingReportLevel.Warning, binding, "Key 为空，导出时跳过", false);
                    activeReport?.RecordEmptyKey(binding, "Key 为空，导出时跳过");
                    continue;
                }

                JToken collected;
                try
                {
                    collected = binding.CollectJson();
                }
                catch (Exception e)
                {
                    Debug.LogException(e, binding);
                    activeReport?.RecordError(binding, $"CollectJson 异常: {e.Message}");
                    continue;
                }

                if (collected == null)
                {
                    activeReport?.RecordWarning(binding, $"Key=\"{binding.Key}\" CollectJson 返回 null，已跳过");
                    continue;
                }

                result[binding.Key] = collected;
            }
        }
        finally
        {
            PopReport(report);
        }

        return result;
    }
    
    

    // ── 路径解析 ─────────────────────────────────────────────

    /// <summary>
    /// 描述当前查找所在的 JSON 节点（Newtonsoft Path，根节点为空）。
    /// </summary>
    public static string DescribeJsonNode(JToken node)
    {
        if (node == null) return "(null)";
        string path = node.Path;
        return string.IsNullOrEmpty(path) ? "根节点" : path;
    }

    public static string FormatMissingKeyMessage(JToken searchRoot, string key)
    {
        return $"在 JSON 节点「{DescribeJsonNode(searchRoot)}」下找不到 Key=\"{key}\"";
    }

    /// <summary>
    /// 从 root 出发，按 '/' 分隔的路径取值。
    /// 支持 Object 属性和 Array 下标（纯数字段）。
    /// </summary>
    public static JToken ResolveToken(JToken root, string path)
    {
        if (root == null || string.IsNullOrEmpty(path)) return null;

        path = path.Trim();
        
        JToken current = root;
        foreach (var part in path.Split('/'))
        {
            if (current == null) return null;
            switch (current.Type)
            {
                case JTokenType.Object:
                    current = ((JObject)current)[part];
                    break;
                case JTokenType.Array when int.TryParse(part, out int idx):
                    var arr = (JArray)current;
                    current = idx >= 0 && idx < arr.Count ? arr[idx] : null;
                    break;
                default:
                    return current;
            }
        }
        return current;
    }

    // ── 广度优先：取路径上第一个 T ───────────────────────────

    /// <summary>
    /// 从 root 的子节点开始广度优先搜索，
    /// 找到 T 就收录并剪枝（不再深入该分支），
    /// 没找到则继续向下。
    /// </summary>
    public static List<T> GetFirstLevelComponents<T>(Transform root)
    {
        var results = new List<T>();
        var queue = new Queue<Transform>();
        queue.Enqueue(root);


        while (queue.Count > 0)
        {
            Transform current = queue.Dequeue();
            T[] component = current.GetComponents<T>();

            if (component.Length != 0)
            {
                results.AddRange(component);
                // 剪枝：不继续深入此分支
            }
            else
            {
                for (int i = 0; i < current.childCount; i++)
                    queue.Enqueue(current.GetChild(i));
            }
        }

        return results;
    }
}
}
