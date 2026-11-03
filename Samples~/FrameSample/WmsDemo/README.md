# WmsDemo — 本项目 WMS 演示资源

演示用 Prefabs / Resources / Sprites，不是 Frame 运行时硬依赖。

| 路径 | 说明 |
|------|------|
| `Prefabs/RecordPlaybackControl.prefab` | 录制回放控制 UI，场景直接引用 |
| `Prefabs/` 其他 | 楼层切换、盘点、货物信息等业务件 |
| `Resources/` | 页面模板、设备管理/库存管理 UI、注视相机示例预制体等 |
| `Sprites/` | 演示用图集 |

注视相机请拖到场景中 `FocusPointManager.m_focusCameraPrefab`，不要依赖 `Resources.Load("Services/注视相机")`。
