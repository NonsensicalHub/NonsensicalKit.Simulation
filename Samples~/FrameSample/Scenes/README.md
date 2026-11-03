# FrameSample Scenes

## FrameWindowExample.unity

最小 Window 演示（不接 MQTT / WMS）。

### 怎么用

1. 打开 `Assets/FrameSample/Scenes/FrameWindowExample.unity`
2. Play
3. 点击屏幕中央按钮 **「打开角色详情」**
4. 弹出带标题栏的窗口；点关闭按钮关掉

### 场景里有什么

| 对象 | 作用 |
|------|------|
| Main Camera | 纯色背景相机 |
| FrameWindowExampleBootstrap | 运行时创建 Canvas、EventSystem、`WindowContainerManager`、按钮与内容模板 |

窗口壳：`Resources.Load("设备管理/Window/Window")`  
→ 实际资源 `FrameSample/WmsDemo/Resources/设备管理/Window/Window.prefab`

### 不需要

- 不需要 `Resources/Services`（MQTT / DigitalTwinUIManager）
- 不需要 AGV / Highlight 适配器

### 若打不开窗

- Console 报 Resources.Load 失败：确认 WmsDemo 下 Window 壳 prefab 仍在
- 报未找到 `WindowContainerManager`：看 Bootstrap 是否启用、IOCC 是否被其它系统清掉
- TMP 字体粉块：工程需有 TextMesh Pro 默认/中文字体（本工程有 TextMesh Pro Chinese）

### 可选：Editor 再生成一版带预制体引用的场景

菜单 **Tools → FrameSample → Build Window Example Scene**（工程未被其它 Unity 占用时可跑；会写 `CharacterDetailContent.prefab` 并覆盖本场景为序列化版）。
