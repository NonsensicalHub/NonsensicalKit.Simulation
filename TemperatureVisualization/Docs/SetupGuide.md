# 配置步骤文档

## 环境要求

- Unity 2021.3+
- Universal Render Pipeline（URP 12.x）
- TextMeshPro

## 方式一：Demo 一键搭建（推荐）

1. 新建或打开 URP 场景。
2. 菜单 **Temperature Visualization → Build Demo UI In Scene** 生成控制面板（直接写入当前场景）。
3. 创建空物体，挂载 **`TemperatureVisualizationDemoSetup`**（`Demo/TemperatureVisualizationDemoSetup.cs`）。
4. 运行 Play。

Demo 会自动创建：

- 核心组件（`TemperatureVisualizationController` 等）
- 默认 **Volume 体积云图**（整个立方体）
- 绑定场景中已有的 **`TemperatureDemoUI`** 控制面板
- 轨道摄像机（右键旋转、滚轮缩放）
- 切片位置通过 UI 滑块调节

### 更新 Demo UI

UI 直接保存在场景中，不使用 Prefab。修改控件布局后重新执行菜单 **Build Demo UI In Scene**（会替换同名 `TemperatureDemoUI` 对象）。

### 可视化模式说明

| 模式 | Inspector 名称 | 说明 |
|------|----------------|------|
| Slice | 切片 — 正交剖面 | 仅渲染 XY/XZ/YZ 切片平面 |
| Volume | 体积 — 立方体云图 | Raymarching 体积渲染（默认） |
| Isosurface | 等温面 — 温度等值面 | Marching Cubes 等温面网格 |
| Combined | 组合 — 体积+切片叠加 | 体积与切片可同时显示 |

## 方式二：仅集成核心库

将以下组件挂到同一根节点：

| 组件 | 说明 |
|------|------|
| `TemperatureVolumeBounds` | 立方体体积 |
| `TemperatureSensorManager` | 传感器数据 |
| `TemperatureFieldInterpolator` | IDW 插值 |
| `TemperatureColorRamp` | 色标 |
| `TemperatureVisualizationController` | 总控 |
| `TemperatureSliceRenderer` | 切片（子物体，可选） |
| `TemperatureVolumeRenderer` | 体积（子物体） |
| `TemperatureIsosurfaceRenderer` | 等温面（子物体，可选） |

在 `TemperatureVisualizationController` 中串联引用，或调用 `Initialize(...)`。

### 推荐默认配置

- `Mode` = **Volume**
- 切片 `ShowSliceXY/XZ/YZ` = **false**
- `TemperatureSensorManager.EnableSimulation` = 按需开启
- 手动调温使用 `SetTemperatureManual(id, temp)`

## 传感器手动调温

```csharp
var manager = GetComponent<TemperatureSensorManager>();
manager.SetTemperatureManual("sensor_0", 35f);  // 锁定该传感器
manager.ClearManualOverride("sensor_0");       // 恢复模拟
```

Demo UI 中：选择传感器 → 拖动「传感器温度」滑块。

## 切片位置

1. 勾选「显示 XY/XZ/YZ 切片」之一。
2. 使用左侧 UI 中的「XX 切片位置」滑块调节。

## WebGL 注意

- 插值器使用分帧主线程，分辨率建议 ≤64。
- 关闭等温面或降低 `GridResolution` 以保帧率。
- `LiveUpdateMode` 默认开启，避免纹理双缓冲闪烁。

## 性能调参

| 参数 | 位置 | 建议 |
|------|------|------|
| 体素分辨率 | Interpolator | PC 64³ / WebGL 32–48³ |
| 重建间隔 | Interpolator `m_MinRebuildInterval` | ≥0.35s |
| Raymarch 步数 | VolumeRenderer | PC 96 / WebGL 48–64 |
| 模拟通知间隔 | SensorManager | 0.5s |
