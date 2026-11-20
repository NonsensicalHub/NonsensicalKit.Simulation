> 本目录为 UPM Sample（`Samples~/FrameSample`）。需在 Package Manager 中 Import **Frame Sample** 后才会复制到工程 `Assets/Samples/...`。

# FrameSample — Frame 示例

本目录是 **Frame 的示例与随附第三方库**，与 `Frame/`（框架本体）分离，不进入 `Frame.Core` 的硬依赖。

| 子目录 | 内容 |
|--------|------|
| `ThirdParty/` | **XCharts、ZCalendar**（原 `Frame/ThirdParty`，非 Frame API） |
| `Optional/XCharts/` | `Frame.Core.XCharts` / `XChartSerieBinder`（需 `XCharts.Runtime`） |
| `Adapters/HikvisionAgv/` | `IAgvStatusTapePlaybackBackend` 实现（依赖宿主 HikvisionAgv） |
| `Adapters/HighlightPlus/` | 向 `FocusPointManager.HighlightHandler` 注入 HighlightPlus 实现 |
| `WmsDemo/` | WMS 演示：Prefabs / Resources / Sprites |
| `WindowExamples/` | 通用 Window 演示（非 WMS） |
| `Scenes/` | `FrameWindowExample.unity` 等 |

## 与宿主 Services 的分工

- **DigitalTwinUIManager / MQTTManager** 等：由**宿主项目**放在 `Resources/Services/`，Frame 代码通过 `ServicePrefab("Services/...")` 加载。
- **注视相机**：在场景 `FocusPointManager` 上配置 `m_focusCameraPrefab`（示例资源：`WmsDemo/Resources/注视相机.prefab`）。

## 录制回放

直接拖用 `WmsDemo/Prefabs/RecordPlaybackControl.prefab`。

## 业务可选程序集提醒

- WMS `SearchTool` 在 `Frame.BizKit.WMS.ZCalendar`，需要本 Sample 的 `ThirdParty/ZCalendar`（或宿主自备）。
- 图表 binder 在本目录 `Optional/XCharts`，需要 `ThirdParty/XCharts`。
