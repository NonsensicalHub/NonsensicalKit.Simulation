# Frame 审查修复说明（2026-11）

## 已做
- Frame.Core 去掉对 LibVLCSharp / MQTTnet / XCharts 的硬依赖；拆出 `Frame.Core.VLC` / `Frame.Core.Mqtt` / `Frame.Core.XCharts`。
- 海康 / EquipmentStatus / VLC：`autoReferenced=false`。`Frame.BizKit.WMS` 主程序集仍 autoReferenced。
- Twin/海康路径从 `URLS` 迁到 `WmsUrls`（BizKit）。
- 生命周期与空引用：ProfileContentRoll 反订阅、FocusPoint TryGet、DigitalTwinUI Unregister、RequestBase 定时器幂等、WindowContainerManager IOCC 清理、VLC 判空与 LibVLC 引用计数。
- 鉴权：Server 条目可单独填 Authorization；MQTT 测试窗不再持久化密码。
- 源文件补齐 namespace，避免与宿主 Assets 旧脚本 CS0433。
- Cinemachine 2.x / 3.x 用 versionDefines，不再用 Unity 版本号。
- Editor 菜单收进 `Tools/Frame/`。
- `Frame.Core.VLC` 打开 Override References。
- LoopScrollRect Demo 默认不编译。

## 文档路径
README / 本说明以 UPM `Frame/` 为准（不是 `Assets/Frame`）。ZCalendar 在 `Frame/ThirdParty/ZCalendar`；XCharts 在 Sample。

## Unity 验证建议
1. 打开宿主工程，等脚本编译：无 LibVLC 时 `Frame.Core.VLC` 应被跳过（不报 CS0246），Frame.Core / BizKit.WMS 应可通过。
2. 有 MQTT 时：MQTTManager 服务仍可连接；Tools/Frame/MQTT 测试客户端。
3. 打开含 DigitalTwinUIPoint 的场景，进/出子场景，确认标签回收无报错。
4. Twin 请求在线模式：确认 Authorization（全局或 per-server）仍生效。
5. 宿主若仍 CS0433：删除 Assets 里与 Frame 同名的旧脚本（SerializeFieldStringAttribute、CustomButtonEvents 等）。

## 结构
- XCharts 在 `Samples~/FrameSample/ThirdParty`；ZCalendar 留在 `Frame/ThirdParty/ZCalendar`（嵌套 Prefab GUID）。
- `Frame.Core.XCharts` 在 Sample `Optional/XCharts`。
- `SearchTool` 在 `Frame.BizKit.WMS.ZCalendar`。
