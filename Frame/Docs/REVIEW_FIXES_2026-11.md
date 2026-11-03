# Frame 审查修复说明（2026-11）

## 已做
- Frame.Core 去掉对 LibVLCSharp / MQTTnet / XCharts 的硬依赖；拆出 `Frame.Core.VLC` / `Frame.Core.Mqtt` / `Frame.Core.XCharts`。
- BizKit.WMS：`autoReferenced=false`；海康配置迁到 `Frame.BizKit.WMS.Hikvision`。
- Twin/海康路径从 `URLS` 迁到 `WmsUrls`（BizKit）。
- 生命周期与空引用：ProfileContentRoll 反订阅、FocusPoint TryGet、DigitalTwinUI Unregister、RequestBase 定时器幂等、WindowContainerManager IOCC 清理、VLC 判空与 LibVLC 引用计数。
- 鉴权：Server 条目可单独填 Authorization；MQTT 测试窗不再持久化密码。

## 文档路径
README / 本说明以 UPM `Frame/` 为准（不是 `Assets/Frame`）。asmdef 已存在；剩余债务见 README「已知债务」。

## Unity 验证建议
1. 打开宿主工程，等脚本编译：无 MQTTnet/LibVLC 时仅对应可选 asmdef 报错，Frame.Core / BizKit.WMS 应可通过。
2. 有 MQTT 时：MQTTManager 服务仍可连接；Tools/Frame/MQTT 测试客户端。
3. 打开含 DigitalTwinUIPoint 的场景，进/出子场景，确认标签回收无报错。
4. Twin 请求在线模式：确认 Authorization（全局或 per-server）仍生效。

## 结构精简（同轮续）
- Core/Scripts 根目录散件归入 Binding、UI/Controls、FocusCamera、Utils/EditMode（保留 .meta GUID）。
- `Frame/ThirdParty` 整迁 `Samples~/FrameSample/ThirdParty`；Frame 根不再带第三方库。
- `Frame.Core.XCharts`（XChartSerieBinder）迁到 Sample `Optional/XCharts`。
- `SearchTool` 迁到 `BizKit.WMS.Scripts/Optional/ZCalendar` → `Frame.BizKit.WMS.ZCalendar`；主 BizKit asmdef 去掉 ZCalendar 引用。
- 新增 `BizKit/WMS/README.md`、`Docs/STRUCTURE.md`、Sample ThirdParty/Optional README。

## 命名空间与 Network 拼写（续）
- 目录 NetWork → Network（Windows 两步改名，保留 meta GUID）。
- 统一 namespace：Frame.Core.* / Frame.BizKit.WMS.* / Frame.Sample.*；Framework.Core、Frame.VideoPlayer、Frame.Equipment、Frame.RealTimeAlarm、CusAttribute 已迁入。
- BindingSearchTool 随 SearchTool 留在 Frame.BizKit.WMS.ZCalendar。
- asmdef 已填 ootNamespace。
