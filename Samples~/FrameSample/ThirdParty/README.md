# ThirdParty（随 Frame Sample，非 Frame API）

本目录原在 `Frame/ThirdParty`，已迁到 Sample，避免框架本体看起来像「自带第三方库」。

| 子目录 | 程序集 | 用途 |
|--------|--------|------|
| `XCharts/` | `XCharts.Runtime` / `XCharts.Editor` | 图表 |
| `ZCalendar/` | `ZCalendar`（及 LoopScrollRect） | 日历 / 日期范围 |

## 使用方式
1. Package Manager → **Frame Sample** → Import
2. 资源会出现在工程 `Assets/Samples/.../FrameSample/ThirdParty/`
3. 需要图表绑定时一并使用同级 `Optional/XCharts`（`Frame.Core.XCharts` / `XChartSerieBinder`）
4. 需要 WMS `SearchTool` 时确保 `ZCalendar` 已编译，并引用 `Frame.BizKit.WMS.ZCalendar`

**不是** `Frame.Core` 的公共 API；可替换为宿主自有同名程序集。
