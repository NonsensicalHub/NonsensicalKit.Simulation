【位置】本目录位于 UPM 包 com.nonsensicallab.nonsensicalkit.simulation 的 Frame/ 下（非 Assets/Frame）。
示例与第三方库在包内 Samples~/FrameSample（Package Manager → Samples → Frame Sample 导入后出现在 Assets/Samples/...）。
宿主工程已通过 manifest file: 引用本包即可使用 Frame.Core；仓储项目再引用 Frame.BizKit.WMS。

Frame — 可复用框架（UI + 数字孪生运行时）

分层一览（由内到外）
1) Frame/Core/           — 通用框架（新项目应优先只依赖本层）
2) Frame/BizKit/WMS/     — 仓储/冷链业务件（非仓储不要依赖）
3) Samples~/FrameSample/ — 示例 + ThirdParty（XCharts、ZCalendar）+ 可选图表 binder
（Frame/ 下不再包含 ThirdParty。）

详见 Docs/STRUCTURE.md。

1) Frame/Core/  — 通用框架
   - Scripts/
     - Abstractions/（可选插件接口：IAgvStatusTapePlaybackBackend、FrameOptionalServices；高亮用 FocusPointManager.HighlightHandler）
     - Binding / Window / Table / UI(/Controls) / Component(WebBrowser)
     - DigitalTwinUI / Network / Config / RecordPlayback / VideoPlayer
     - SubScene / FocusCamera / Utils/EditMode
   - 可选程序集（缺对应插件时该 asmdef 编不过，但不拖垮 Frame.Core）：
     - Frame.Core.Mqtt（MQTTnet.dll）
     - Frame.Core.VLC（LibVLCSharp.dll）
   - Editor（JSON Binding 等）
   - 说明：不自带 Resources/Services 服务预制体；由宿主项目提供
   - 说明：XCharts binder（Frame.Core.XCharts）已迁到 Frame Sample 的 Optional/XCharts；需自行 Import Sample 或自备 Xcharts

2) Frame/BizKit/WMS/  — 仓储/冷链业务件（详见 BizKit/WMS/README.md）
   - 主程序集不依赖 ZCalendar / XCharts / 海康
   - Optional/Hikvision → Frame.BizKit.WMS.Hikvision（需宿主 HikvisionAgv.Runtime）
   - Optional/ZCalendar → Frame.BizKit.WMS.ZCalendar（SearchTool；需 Sample ThirdParty 的 ZCalendar）
   - Twin/海康 HTTP 路径见 WmsUrls（不在 Core.URLS）

3) Samples~/FrameSample/
   - ThirdParty/Xcharts、ThirdParty/ZCalendar（非 Frame API）
   - Optional/XCharts（XChartSerieBinder）
   - Adapters、WmsDemo、WindowExamples、Scenes

宿主项目需提供的服务预制体
- 放在任意 Resources/Services/ 下，与代码约定一致：
  - Services/DigitalTwinUIManager（[ServicePrefab]）
  - Services/MQTTManager（[ServicePrefab]，需 Frame.Core.Mqtt + MQTTnet）
  - 其他项目自有 Service（Timer、DataHub 等）按项目配置
- 注视相机：在场景 FocusPointManager 上配置 m_focusCameraPrefab（或直接指定 m_camera）

可选适配器接线（无反射）
- AGV：挂 HikvisionAgvTapeBackend 或拖到 AgvStatusTapePlayer.m_backendBehaviour
- 高亮：外部给 FocusPointManager.HighlightHandler 赋值
- 缺失时：AGV 播放跳过并 Warning；Focus 需要高亮但未注入 Handler 时 Warning 一次并跳过

安全提示
- Authorization / MQTT 密码等勿把生产密钥打进默认资产。
- URLBase 支持全局 Authorization + 每 Server Key 独立 Authorization。
- MQTT 测试客户端不再把密码写入 EditorPrefs。

已知债务
- 命名空间已统一为 Frame.Core.* / Frame.BizKit.WMS.* / Frame.Sample.*（类名与 SerializeField 未改；回 Unity 验证编译与预制体引用）
- BindingSliderInputField 仍兼容旧 JSON 键「货架切片数量」；新数据请用 signal/max
- Adapters 故意不设独立 asmdef（插件多在默认程序集）

命名空间
- Core：Frame.Core.*（按目录）；可选 Frame.Core.Mqtt / Frame.Core.VLC；Sample 图表 Frame.Core.XCharts
- BizKit：Frame.BizKit.WMS.*；可选 Hikvision / ZCalendar
- 旧 Framework.Core / Frame.VideoPlayer / Frame.Equipment / Frame.RealTimeAlarm / CusAttribute 已迁入
