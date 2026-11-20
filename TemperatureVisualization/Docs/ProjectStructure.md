# 项目结构说明

## 核心 vs Demo 分离

| 目录 | 命名空间 | 用途 |
|------|----------|------|
| `Scripts/` | `TemperatureVisualization` | 可复用核心库 |
| `Demo/` | `TemperatureVisualization.Demo` | 演示场景、UI、摄像机、一键搭建 |
| `Shaders/` | — | URP 着色器 |
| `Docs/` | — | 文档 |

集成到自己的项目时，**只引用 `Scripts/` + `Shaders/`**，不必包含 `Demo/`。

## 核心目录

```
Scripts/
├── Data/
│   ├── TemperatureSensor.cs
│   └── TemperatureSensorManager.cs      # 增删、模拟、手动覆盖
├── Interpolation/
│   └── TemperatureFieldInterpolator.cs  # IDW、节流、单纹理实时更新
├── Rendering/
│   ├── TemperatureVolumeBounds.cs
│   ├── TemperatureColorRamp.cs
│   ├── TemperatureSliceRenderer.cs      # 切片渲染
│   ├── TemperatureVolumeRenderer.cs
│   ├── TemperatureIsosurfaceRenderer.cs
│   ├── TemperatureVisualizationController.cs
│   └── MarchingCubesTables.cs
```

## Demo 目录

```
Demo/
├── TemperatureVisualizationDemoSetup.cs   # 一键搭建
├── TemperatureDemoCameraController.cs     # 轨道摄像机
└── UI/
    └── TemperatureDemoControlPanel.cs     # 演示 UI（含传感器调温）
```

## 数据流

```
SensorManager → Interpolator → Texture3D
                    ↓
Controller → VolumeRenderer / SliceRenderer / IsosurfaceRenderer
                    ↑
              DemoControlPanel（仅 Demo）
```

## 默认行为（修复后）

- **默认模式**：`Volume`（整个立方体云图，非固定裁切面）
- **切片默认关闭**，通过 UI 勾选后叠加显示
- **模拟默认关闭**（Demo），避免覆盖手动调温
- **实时单纹理更新**，避免双缓冲闪烁
- **插值/模拟节流**，减少卡顿
