# Frame 可选依赖（不再使用项目 Scripting Define）

手写 `FRAME_*` 项目宏已全部去掉。可选能力按下面方式启用：

## 1. UPM 自动（versionDefines）

| 程序集 | 检测的包 | 自动宏 | 行为 |
|--------|----------|--------|------|
| `Frame.Core.WebGL` | `com.nonsensicallab.nonsensicalkit.webgl` | `FRAME_NK_WEBGL` | 有包才编译并硬引用 WebBridge；无包不编译 |
| `Frame.Core` / `Frame.Cinemachine` | `com.unity.cinemachine` | `CINEMACHINE_2` / `CINEMACHINE_3` | 按 2.x / 3.x 编译注视与 Solo 相机；未安装则跳过 Cinemachine API |
| `Frame.Core.VLC` | 宿主 `LibVLCSharp.dll`（VLCUnity） | `FRAME_HAS_LIBVLC` | 有 DLL 才编译；由 Editor 自动同步宏，无需手写 |

宿主**不必**在 Player Settings 里填这些宏。

## 2. 随包编译（有插件/程序集则正常，没有则仅该可选程序集报错）

这些 asmdef **无** defineConstraints，缺依赖时只会红在可选程序集上，不拖垮 `Frame.Core`。带 `autoReferenced=false` 的不会被 Assembly-CSharp 自动引用。

| 程序集 | 宿主需提供 | autoReferenced |
|--------|------------|----------------|
| `Frame.Core.Mqtt` / `Frame.Core.Network.Editor` | `Frame/Plugins/MQTTnet.dll`（工程内只能有一份同名 DLL） | true |
| `Frame.BizKit.WMS` | DigitalTwin.Warehouse 等（simulation 已依赖） | true |
| `Frame.BizKit.WMS.Hikvision` | 包内 `HikvisionAgv.Runtime` | true（Sample 适配器要引用） |
| `Frame.BizKit.WMS.EquipmentStatus` | Hikvision + MQTT | false |
| `Frame.BizKit.WMS.ZCalendar` | 包内 `Frame/ThirdParty/ZCalendar` | true |
| Sample `Frame.Core.XCharts` | Sample ThirdParty / UPM 的 XCharts | — |

`LoopScrollRect` 自带 Demo 受 `LOOP_SCROLL_RECT_DEMO` 约束，默认不编译。

不用的可选功能：可删掉对应 DLL、不导入 Sample，或从工程里排除该可选文件夹；无需再维护一长串宏。

## 3. 已废弃

`FRAME_BIZKIT_WMS` / `FRAME_MQTT` / `FRAME_VLC` / `FRAME_HIKVISION` / `FRAME_ZCALENDAR` / `FRAME_XCHARTS` — 请从 Player Settings 中删除。
