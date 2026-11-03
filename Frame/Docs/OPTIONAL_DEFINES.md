# Frame 可选依赖（不再使用项目 Scripting Define）

手写 `FRAME_*` 项目宏已全部去掉。可选能力按下面方式启用：

## 1. UPM 自动（versionDefines）

| 程序集 | 检测的包 | 自动宏 | 行为 |
|--------|----------|--------|------|
| `Frame.Core.WebGL` | `com.nonsensicallab.nonsensicalkit.webgl` | `FRAME_NK_WEBGL` | 有包才编译并硬引用 WebBridge；无包不编译 |

宿主**不必**在 Player Settings 里填这个宏。

## 2. 随包编译（有插件/程序集则正常，没有则仅该可选程序集报错）

这些 asmdef **无** defineConstraints，缺依赖时只会红在可选程序集上，不拖垮 `Frame.Core`：

| 程序集 | 宿主需提供 |
|--------|------------|
| `Frame.Core.Mqtt` / `Frame.Core.Network.Editor` | `MQTTnet.dll` |
| `Frame.Core.VLC` | `LibVLCSharp.dll` |
| `Frame.BizKit.WMS` | DigitalTwin.Warehouse 等（simulation 已依赖） |
| `Frame.BizKit.WMS.Hikvision` | 工程内 `HikvisionAgv.Runtime` |
| `Frame.BizKit.WMS.ZCalendar` | Sample ThirdParty 的 `ZCalendar`（或自备） |
| Sample `Frame.Core.XCharts` | Sample ThirdParty / UPM 的 XCharts |

不用的可选功能：可删掉对应 DLL、不导入 Sample，或从工程里排除该可选文件夹；无需再维护一长串宏。

## 3. 已废弃

`FRAME_BIZKIT_WMS` / `FRAME_MQTT` / `FRAME_VLC` / `FRAME_HIKVISION` / `FRAME_ZCALENDAR` / `FRAME_XCHARTS` — 请从 Player Settings 中删除（石家庄工程已清）。
