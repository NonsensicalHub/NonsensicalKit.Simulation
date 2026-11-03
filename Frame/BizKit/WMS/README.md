# BizKit.WMS — 仓储 / 冷链业务件

**非仓储项目请不要依赖本层。** 新项目优先只引用 `Frame.Core`。

## 用途
仓储数字孪生业务：货位/设备信息、实时报警、Twin HTTP（`WmsUrls`）、业务 DigitalTwinUI 点位、搜索工具等。

## 程序集
| 程序集 | 路径 | 说明 |
|--------|------|------|
| `Frame.BizKit.WMS` | `WMS/` | 主业务程序集；依赖 `Frame.Core`，**不**依赖 ZCalendar / XCharts / 海康 |
| `Frame.BizKit.WMS.Editor` | `Scripts/Editor/` | 编辑器工具 |
| `Frame.BizKit.WMS.Hikvision` | `Scripts/Optional/Hikvision/` | 可选：海康 AGV 站点配置（需宿主 `HikvisionAgv.Runtime`） |
| `Frame.BizKit.WMS.ZCalendar` | `Scripts/Optional/ZCalendar/` | 可选：`SearchTool` 日期范围（需 `ZCalendar`，见 Sample ThirdParty） |

## 可选依赖从哪来
- **ZCalendar / XCharts**：不再随 `Frame/` 携带。导入 Package Manager 中的 **Frame Sample** 后，位于 `Samples~/FrameSample/ThirdParty/`（拷贝到工程 `Assets/Samples/...`）。也可由宿主自行提供同名程序集。
- **HikvisionAgv**：宿主插件，不在本包 Sample 内。

## 边界
- `Frame.Core` **不得**引用 BizKit。
- 业务 URL、冷链/仓储特化逻辑留在本层；通用 Binding / Window / MQTT / Focus 在 Core。
