> 本目录为 UPM Sample（`Samples~/FrameSample`）。需在 Package Manager 中 Import **Frame Sample** 后才会复制到工程 `Assets/Samples/...`。

# FrameSample — Frame 示例

本目录是 **Frame 的示例内容**，与 `Assets/Frame`（框架本体）同级，不进入 `Frame.Core` 程序集依赖。

| 子目录 | 内容 |
|--------|------|
| `Adapters/HikvisionAgv/` | `IAgvStatusTapePlaybackBackend` 实现（依赖宿主 HikvisionAgv） |
| `Adapters/HighlightPlus/` | 向 `FocusPointManager.HighlightHandler` 注入 HighlightPlus 实现 |
| `WmsDemo/` | 本项目 WMS 演示：Prefabs / Resources / Sprites（业务页面、RecordPlaybackControl、注视相机示例预制体等） |
| `WindowExamples/` | 通用 Window 演示：`UIManagerExample`、`CharacterDetailContent`（非 WMS） |

## 与宿主 Services 的分工

- **DigitalTwinUIManager / MQTTManager** 等：由**宿主项目**放在 `Resources/Services/`，Frame 代码通过 `ServicePrefab("Services/...")` 加载。
- **注视相机**：在场景 `FocusPointManager` 上配置 `m_focusCameraPrefab`（示例资源：`WmsDemo/Resources/注视相机.prefab`）。


      param($m)
      # skip complex
      $m.Value
    录制回放

直接拖用 `WmsDemo/Prefabs/RecordPlaybackControl.prefab`。

## 示例场景

- `Scenes/FrameWindowExample.unity`：最小 Window 演示（点按钮打开角色详情窗）。说明见 `Scenes/README.md`。
