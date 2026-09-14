# 路网扩展 Handoff

停留（顶升移栽）仍在 `PathNode` 字段上，PathMove / Timeline 逻辑不改。这里只说明**怎么给路网加新业务能力**，不要把业务类型写进本目录。

## 目录与耦合

- 路网核心：`Packages/com.nonsensicallab.nonsensicalkit.simulation/ScriptAnimation/Runtime/Path/`
- 路网编辑器：`Packages/com.nonsensicallab.nonsensicalkit.simulation/ScriptAnimation/Editor/Path/`
- 业务实现（例如海康）：独立文件夹，只引用 `NonsensicalKit.ScriptAnimation.Runtime`，**禁止**路网核心 `using` 业务命名空间

路网认识的只有：

- `IPathNodeFeature`：挂在节点上的数据模块
- `IPathFeatureIndex` + `PathFeatureIndexRegistry`：路网级字典索引
- `PathFeatureStore`：Bind 后的只读缓存

## 给节点加一种能力

1. 在业务程序集写一个可序列化类，实现 `IPathNodeFeature`，带无参构造。
2. 可选：`[PathNodeFeatureMenu("分组/显示名")]`，会出现在 PathNode Inspector「添加扩展模块」。
3. 不要做成第二个 MonoBehaviour，不要在 PathMove 热路径里 `GetFeature`。

```csharp
[Serializable]
[PathNodeFeatureMenu("示例/工位码")]
public sealed class StationCodeFeature : IPathNodeFeature
{
    public string Code;
}
```

挂载：场景里选 PathNode → 添加扩展模块，或代码 `node.SetFeature(new StationCodeFeature { Code = "A-01" })`。

同类型只保留一份。`PathNetwork.BindNodesNetwork()`（Awake / OnValidate / 收集子节点 / 导入后）会 Rebuild 缓存。

## 需要按 ID 反查时

热路径禁止遍历全部节点。实现 `IPathFeatureIndex`，加载时自注册：

```csharp
public sealed class StationCodeIndex : IPathFeatureIndex
{
    readonly Dictionary<string, PathNode> _byCode = new Dictionary<string, PathNode>();

    public void Rebuild(IReadOnlyList<PathNode> nodes)
    {
        _byCode.Clear();
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var feature = node != null ? node.GetFeature<StationCodeFeature>() : null;
            if (feature != null && !string.IsNullOrEmpty(feature.Code))
                _byCode[feature.Code] = node;
        }
    }

    public bool TryGetNode(string code, out PathNode node) =>
        _byCode.TryGetValue(code, out node);
}

// Runtime
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
static void Register() => PathFeatureIndexRegistry.Register<StationCodeIndex>();

// Editor 下 OnValidate 也要用：另做 InitializeOnLoad 再 Register 一次（幂等）
```

查询：

```csharp
if (network.FeatureStore.TryGetIndex<StationCodeIndex>(out var index) &&
    index.TryGetNode("A-01", out var node))
{
    Vector3 world = node.Position;
}
```

`GetFeature` 只允许出现在 Rebuild / 编辑器 / 导入。每帧、每条 PathMove 采样不要扫模块列表。

需要按节点下标批量读时：先 `store.GetArray<T>()` 一次，再按下标取。

## 不要做的事

- 改 `PathNode` 加业务字段（停留除外，已在节点上）
- 在 `PathNetwork` 里写死某种 Feature / Dictionary
- 使用时 `GetComponent` 找扩展
- 每帧 `BindNodesNetwork` / `Rebuild`

## 停留与 Timeline

`m_pauseOnPass`、`PauseDuration`、`TryBuildWorldPoints` 填 pauses、PathMove 换向 35° 过滤均保持原样。扩展模块与停留正交，一个节点可以同时有停留和海康点位。
