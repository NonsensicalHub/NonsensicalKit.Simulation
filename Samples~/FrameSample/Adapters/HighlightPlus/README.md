# HighlightPlus 高亮注入

向 `FocusPointManager.HighlightHandler`（`Func<GameObject, Action>`）赋值，用 HighlightPlus 开关 `HighlightEffect.highlighted`。

## 接线

1. `FocusPointManager` 所在物体上挂本组件（或手动在其它脚本里给 `HighlightHandler` 赋值）
2. 注视点物体上需有 `HighlightEffect`

## 外部自行注入示例

```csharp
focusPointManager.HighlightHandler = go =>
{
    var fx = go.GetComponent<HighlightEffect>();
    if (fx == null) return null;
    fx.highlighted = true;
    return () => { if (fx != null) fx.highlighted = false; };
};
```
