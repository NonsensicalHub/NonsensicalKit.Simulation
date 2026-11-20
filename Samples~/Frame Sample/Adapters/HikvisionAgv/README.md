# HikvisionAgv 适配器

实现 `Frame.Core.Abstractions.IAgvStatusTapePlaybackBackend`，供 `AgvStatusTapePlayer` 使用。

## 接线

1. 场景保留 `AgvStatusTapePlayer`
2. 添加本目录组件 `HikvisionAgvTapeBackend`（可与 Player 同物体）
3. 默认 `m_autoRegister=true` → 注册到 `FrameOptionalServices.AgvTape`；或拖到 Player 的 `m_backendBehaviour`
4. 可选指定 `m_statusRequest`；空则运行时查找

路径：`Assets/FrameSample/Adapters/HikvisionAgv/`
