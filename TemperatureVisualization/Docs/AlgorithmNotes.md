# 关键算法说明

## 1. 反距离加权插值（IDW）

对空间中任意点 \(\mathbf{p}\)，已知传感器集合 \(\{(\mathbf{s}_i, t_i)\}\)，温度估算为：

\[
T(\mathbf{p}) = \frac{\sum_i w_i \cdot t_i}{\sum_i w_i}, \quad w_i = \frac{1}{\max(d_i,\epsilon)^{p}}
\]

其中 \(d_i = \|\mathbf{p} - \mathbf{s}_i\|\)，\(p\) 为距离幂次（默认 2），\(\epsilon\) 防止除零。

实现位置：`TemperatureFieldInterpolator.ComputeIdw`。

### 复杂度

- 单个体素：\(O(N)\)，N 为传感器数量。
- 整个 3D 场：\(O(R^3 \cdot N)\)，R 为分辨率。

### 优化策略

| 策略 | 说明 |
|------|------|
| PC 后台线程 | `Task.Run` 计算 float 缓冲，主线程 `SetPixelData` 上传 |
| WebGL 分帧 | 协程按 Z 层切片，每帧处理 `m_WebGlSlicesPerFrame` 层 |
| 更新节流 | `m_UpdateInterval` 限制重建频率（默认 0.25s） |
| 双缓冲 | 新旧 Texture3D 混合，Shader `_Blend` 实现平滑过渡 |

## 2. Color Ramp 映射

1. `TemperatureColorRamp` 将 `Gradient` 烘焙为 `Texture2D(256,1)`。
2. Shader 中将温度归一化：`(temp - min) / (max - min)`。
3. 采样 `_ColorRamp` 得到颜色与 Alpha。

默认梯度：蓝 → 青 → 绿 → 黄 → 橙 → 紫（雷达云图风格）。

## 3. 切片渲染

- 在体积 AABB 内放置轴对齐 Quad（XY / XZ / YZ）。
- Fragment Shader 将世界坐标转为归一化 UVW，固定一切片轴分量后采样 `Texture3D`。
- 支持同时绘制多个切片平面（P1 多切片联动）。

Shader：`TemperatureVisualization/Slice`。

## 4. 体积 Raymarching

### 流程

1. 渲染体积 Cube（Cull Front，从内部看）。
2. 对每个像素构造相机射线，与 AABB 求交得到 `[tEnter, tExit]`。
3. 在区间内均匀步进，每步：
   - 采样 3D 温度纹理（支持双缓冲 lerp）
   - 映射 Color Ramp 得到颜色
   - 边缘柔化：距边界越近 Alpha 越低
   - 噪声扰动：3D Hash 噪声调制密度
   - Front-to-back 累加颜色

### 参数

- `_StepCount`：步数，越大越细腻，开销线性增加。
- `_DensityScale`：温度对密度的放大系数。
- `_EdgeSoftness`：边界柔化 + 噪声强度。

Shader：`TemperatureVisualization/VolumeRaymarch`。

## 5. Marching Cubes 等温面

1. 在降采样网格（默认 32³）上遍历体素立方体。
2. 8 个角点温度与阈值比较，得到 0–255 配置索引。
3. 查 `EdgeTable` 确定相交边，线性插值得到顶点。
4. 查 `TriTable` 生成三角形，顶点色由 Color Ramp 映射。

实现位置：`TemperatureIsosurfaceRenderer`、`MarchingCubesTables`。

查表数据基于 Paul Bourke / Jason Fisher 公开实现。

## 6. WebGL 降级方案

| 能力 | PC | WebGL |
|------|-----|-------|
| 多线程插值 | `Task.Run` | 主线程协程分帧 |
| Texture 格式 | RFloat（优先） | RHalf fallback |
| Compute Shader | 未使用 | 未使用 |
| Raymarch 步数 | 96 | 48–64 |

编译宏：`#if UNITY_WEBGL && !UNITY_EDITOR` 在 `TemperatureFieldInterpolator` 中切换路径。

## 7. 性能预估

以 64³ 体素、8 传感器、96 步 Raymarch 为例：

- IDW 计算：约 2M 次距离运算 → PC 后台 < 50ms
- GPU Raymarch：取决于屏幕覆盖像素数 × 步数
- 等温面：32³ 网格遍历，约 32K 立方体 → 通常 < 20ms

WebGL 上建议关闭 Combined 模式中的等温面，或降低分辨率，以维持 720p 30 FPS。
