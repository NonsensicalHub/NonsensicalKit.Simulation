# ThirdParty（随 Frame Sample，非 Frame API）

| 子目录 | 程序集 | 用途 |
|--------|--------|------|
| `XCharts/` | `XCharts.Runtime` / `XCharts.Editor` | 图表 |

## ZCalendar 已迁出 Sample
日历组件在包本体 **`Frame/ThirdParty/ZCalendar`**（程序集 `ZCalendar`），随包加载，**不再**随 Frame Sample 拷贝。

原因：Sample 内父预制体（`高效盘点` / `SearchTool`）嵌套引用 `Zcalendar.prefab`（GUID `22bc9fe…`）。若该 prefab 也在 Sample 里，整包 Import 时父资源可能在子 GUID 尚未按 meta 登记前就导入，Editor 会报 `Missing Prefab with guid: 22bc9fe…`（日志里甚至出现过同一路径被临时登记成其它 GUID）。放到包本体后 GUID 常驻，Import Sample 只导入引用方，嵌套可解析。

## 使用方式
1. Package Manager → **Frame Sample** → Import（只需 XCharts 等 Sample 资源时）
2. ZCalendar / `SearchTool`：直接用包内 `Frame/ThirdParty/ZCalendar` + `Frame/BizKit/WMS/Scripts/Optional/ZCalendar`
3. 图表绑定：同级 `Optional/XCharts`（`Frame.Core.XCharts`）

**不是** `Frame.Core` 的公共 API；XCharts 可替换为宿主自有同名程序集。