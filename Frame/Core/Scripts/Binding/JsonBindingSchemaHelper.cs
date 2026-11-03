using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 根据模板上的 BindingToken 生成 JSON 骨架，并校验 Key / 与 JSON 的匹配情况。
/// 运行时与编辑器均可使用。
/// </summary>

namespace Frame.Core.Binding
{
public static class JsonBindingSchemaHelper
{
    public sealed class KeyIssue
    {
        public BindingToken Binding;
        public string Message;
        public bool IsError;
    }

    public sealed class DiffResult
    {
        public readonly List<string> MissingInJson = new();
        public readonly List<string> ExtraInJson = new();
        public readonly List<string> EmptyKeys = new();
        public readonly List<string> DuplicateKeys = new();
    }

    /// <summary>
    /// 按与 <see cref="JsonBindingHelper.BindFirstLevel"/> 相同的第一层规则，
    /// 生成占位 JSON 骨架（容器会递归展开一层样例）。
    /// </summary>
    public static JObject BuildSkeleton(GameObject root)
    {
        var result = new JObject();
        if (root == null) return result;

        var bindings = JsonBindingHelper.GetFirstLevelComponents<BindingToken>(root.transform);
        var usedKeys = new HashSet<string>();

        foreach (var binding in bindings)
        {
            if (string.IsNullOrEmpty(binding.Key))
                continue;
            if (!usedKeys.Add(binding.Key))
                continue;

            result[binding.Key] = BuildSampleToken(binding);
        }

        return result;
    }

    /// <summary>检查空 Key、同层重复 Key。</summary>
    public static List<KeyIssue> ValidateTemplateKeys(GameObject root)
    {
        var issues = new List<KeyIssue>();
        if (root == null) return issues;

        var all = root.GetComponentsInChildren<BindingToken>(true);
        var empty = new List<BindingToken>();
        foreach (var b in all)
        {
            if (string.IsNullOrEmpty(b.Key))
                empty.Add(b);
        }

        foreach (var b in empty)
        {
            issues.Add(new KeyIssue
            {
                Binding = b,
                IsError = true,
                Message = "Key 为空"
            });
        }

        // 同父路径第一层内不允许重复 Key（与 BindFirstLevel 语义一致）
        CollectDuplicateFirstLevelKeys(root.transform, issues);
        return issues;
    }

    /// <summary>
    /// 对比模板第一层 Key 与 JSON 根对象：模板有而 JSON 无 → Missing；JSON 有而模板无 → Extra。
    /// </summary>
    public static DiffResult DiffAgainstJson(GameObject root, JObject json)
    {
        var diff = new DiffResult();
        if (root == null) return diff;

        var templateKeys = new HashSet<string>();
        var bindings = JsonBindingHelper.GetFirstLevelComponents<BindingToken>(root.transform);
        var seen = new HashSet<string>();

        foreach (var binding in bindings)
        {
            if (string.IsNullOrEmpty(binding.Key))
            {
                diff.EmptyKeys.Add(binding.name);
                continue;
            }

            if (!seen.Add(binding.Key))
                diff.DuplicateKeys.Add(binding.Key);
            templateKeys.Add(binding.Key);
        }

        var jsonKeys = new HashSet<string>();
        if (json != null)
        {
            foreach (var prop in json.Properties())
                jsonKeys.Add(prop.Name);
        }

        foreach (var key in templateKeys)
        {
            if (!jsonKeys.Contains(key))
                diff.MissingInJson.Add(key);
        }

        foreach (var key in jsonKeys)
        {
            if (!templateKeys.Contains(key))
                diff.ExtraInJson.Add(key);
        }

        return diff;
    }

    public static JToken BuildSampleToken(BindingToken binding)
    {
        if (binding == null)
            return JValue.CreateNull();

        switch (binding)
        {
            case BindingText:
            case BindingInputField:
            case BindingButtonText:
                return new JValue("");

            case BindingSprite:
            case BindingRawImage:
                return new JValue("Resources/路径");

            case BindingColor:
                return new JValue("#FFFFFFFF");

            case BindingFillAmount:
                return new JValue(0.5f);

            case BindingToggle:
                return new JValue(false);

            case BindingSlider:
                return new JObject
                {
                    ["value"] = 0f,
                    ["min"] = 0f,
                    ["max"] = 1f
                };

            case BindingDropDown:
                return new JArray("选项A", "选项B");

            case BindingWeb:
                return new JObject
                {
                    ["Url"] = "",
                    ["UrlType"] = "StreamingAssets"
                };

            case BindingSliderInputField:
                return new JObject
                {
                    ["绑定信号"] = "",
                    ["max"] = 1
                };

            case BindingNodeActive:
            case BindingNodeSwitcher:
            case BindingNodeActiveType:
                return new JValue("NodeId");

            case BindingNodeMax:
                return new JArray("NodeIdA", "NodeIdB");

            case BindingArray array:
                return BuildArraySkeleton(array);

            case BindingPages:
                return new JArray
                {
                    new JObject
                    {
                        [BindingPages.PrefabField] = "Resources预制体名",
                        ["$content"] = new JObject()
                    }
                };

            case BindingObject obj:
                return BuildObjectSkeleton(obj);

            default:
                return binding.BuildSampleJson();
        }
    }

    private static JArray BuildArraySkeleton(BindingArray array)
    {
        var result = new JArray();
        var prefab = array.ItemPrefab;
        if (prefab == null)
        {
            result.Add(new JObject());
            return result;
        }

        // 用模板预制体上的第一层 Binding 生成一条样例元素
        result.Add(BuildSkeleton(prefab));
        return result;
    }

    private static JObject BuildObjectSkeleton(BindingObject obj)
    {
        GameObject schemaRoot = obj.SchemaRoot;
        var result = new JObject();
        var used = new HashSet<string>();
        var queue = new Queue<Transform>();
        queue.Enqueue(schemaRoot.transform);

        while (queue.Count > 0)
        {
            Transform current = queue.Dequeue();
            if (current != schemaRoot.transform)
            {
                foreach (var token in current.GetComponents<BindingToken>())
                {
                    if (string.IsNullOrEmpty(token.Key) || !used.Add(token.Key))
                        continue;
                    result[token.Key] = BuildSampleToken(token);
                }
            }

            if (current.GetComponent<BindingPages>() != null
                || current.GetComponent<BindingArray>() != null)
            {
                continue;
            }

            for (int i = 0; i < current.childCount; i++)
                queue.Enqueue(current.GetChild(i));
        }

        return result;
    }

    private static void CollectDuplicateFirstLevelKeys(Transform root, List<KeyIssue> issues)
    {
        var bindings = JsonBindingHelper.GetFirstLevelComponents<BindingToken>(root);
        var map = new Dictionary<string, List<BindingToken>>();

        foreach (var b in bindings)
        {
            if (string.IsNullOrEmpty(b.Key)) continue;
            if (!map.TryGetValue(b.Key, out var list))
            {
                list = new List<BindingToken>();
                map[b.Key] = list;
            }

            list.Add(b);
        }

        foreach (var kv in map)
        {
            if (kv.Value.Count <= 1) continue;
            foreach (var b in kv.Value)
            {
                issues.Add(new KeyIssue
                {
                    Binding = b,
                    IsError = true,
                    Message = $"第一层重复 Key=\"{kv.Key}\" (共 {kv.Value.Count} 处)"
                });
            }
        }

        // 递归容器：Array 的 itemPrefab、Pages 不做深层强制；Object 子树再检一层
        foreach (var b in bindings)
        {
            if (b is BindingObject bindingObject)
            {
                GameObject childRoot = bindingObject.SchemaRoot;
                var childBindings = JsonBindingHelper.GetFirstLevelComponents<BindingToken>(childRoot.transform);
                var childMap = new Dictionary<string, int>();
                foreach (var cb in childBindings)
                {
                    if (string.IsNullOrEmpty(cb.Key)) continue;
                    childMap.TryGetValue(cb.Key, out int c);
                    childMap[cb.Key] = c + 1;
                }

                foreach (var cb in childBindings)
                {
                    if (string.IsNullOrEmpty(cb.Key)) continue;
                    if (childMap[cb.Key] > 1)
                    {
                        issues.Add(new KeyIssue
                        {
                            Binding = cb,
                            IsError = true,
                            Message = $"BindingObject 内重复 Key=\"{cb.Key}\""
                        });
                    }
                }
            }
        }
    }
}
}
