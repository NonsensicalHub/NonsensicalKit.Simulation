# 温度场可视化（Temperature Visualization）

立体仓库温度场可视化：离散传感器 → IDW 插值 → 3D 温度场 → 体积云图 / 切片 / 等温面渲染。

## 快速开始（Demo）

1. 打开 URP 场景，菜单 **Temperature Visualization → Build Demo UI In Scene** 生成控制面板。
2. 创建空物体并挂载 **`TemperatureVisualizationDemoSetup`**（位于 `Demo/`）。
3. 运行 Play：自动创建体积盒、传感器、体积渲染，并绑定场景中的 UI。
4. **默认模式为 Volume（整个立方体云图）**，切片默认关闭。
5. 右键拖动旋转视角，滚轮缩放；切片位置用 UI 滑块调节。

## 目录结构

```
TemperatureVisualization/
├── Scripts/          # 核心库（可复用、可扩展）
│   ├── Data/
│   ├── Interpolation/
│   ├── Rendering/
│   └── Interaction/
├── Demo/             # 演示场景专用（UI、摄像机、一键搭建）
├── Shaders/
└── Docs/
```

**核心与 Demo 分离**：业务集成时只引用 `Scripts/` + `Shaders/`；`Demo/` 仅用于快速体验。

## 核心 API 速览

```csharp
// 手动设置传感器温度（锁定后不受模拟覆盖）
sensorManager.SetTemperatureManual("sensor_0", 32.5f);

// 外部批量注入
sensorManager.InjectTemperatures(data);

// 切换可视化模式
controller.Mode = VisualizationMode.Volume;
```

## 平台

- Windows PC：后台线程 IDW，推荐 64³
- WebGL：分帧主线程，推荐 32–64³

详见 [Docs/SetupGuide.md](Docs/SetupGuide.md)。
