# BizKit.WMS / Optional

本目录下的子程序集**不是** `Frame.BizKit.WMS` 主集的一部分：

| 子目录 | 程序集 | 缺依赖时 |
|--------|--------|----------|
| `Hikvision/` | `Frame.BizKit.WMS.Hikvision` | 仅本集失败 |
| `ZCalendar/` | `Frame.BizKit.WMS.ZCalendar`（SearchTool） | 仅本集失败 |

主业务请依赖 `Frame.BizKit.WMS`；需要日历搜索或海康配置时再引入对应 Optional，并准备好第三方/插件。
