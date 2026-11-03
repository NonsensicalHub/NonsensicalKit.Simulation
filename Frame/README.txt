【位置】本目录位于 UPM 包 com.nonsensicallab.nonsensicalkit.simulation 的 Frame/ 下（非 Assets/Frame）。
示例在包内 Samples~/FrameSample（Package Manager → Samples → Frame Sample 导入后出现在 Assets/Samples/...）。
宿主工程已通过 manifest file: 引用本包即可使用 Frame.Core / Frame.BizKit.WMS 等程序集。
Frame — 可复用框架（UI + 数字孪生运行时）

目标
- 与具体项目业务解耦的通用能力合集
- 用「模板预制体 + JSON」快速搭建新项目 UI 与配套运行时
- Frame 放入宿主已有 package / 工程中使用（本目录不是独立新建的 UPM 包）

物理分层
1) Assets/Frame/Core/  — 通用框架（新项目应优先只依赖本层）
   - Scripts/
     - Abstractions/（可选插件接口：IAgvStatusTapePlaybackBackend、FrameOptionalServices（仅 AGV）；高亮用 FocusPointManager.HighlightHandler）
     - Binding / Window / Table / UI / Component(WebBrowser)
     - DigitalTwinUI / NetWork / Config / RecordPlayback / VideoPlayer（含 CustomVLCPlayer）
     - SubScene / FocusCamera
   - Editor（JSON Binding 等）
   - 说明：不自带 Resources/Services 服务预制体；由宿主项目提供

2) Assets/Frame/BizKit/WMS/  — 仓储/冷链业务件（非仓储项目可不引用）

3) Assets/Frame/ThirdParty / Docs

4) Assets/FrameSample/  — 示例（不进 Frame.Core 依赖）
   - WindowExamples/：通用 Window 演示（UIManagerExample、CharacterDetailContent）
   - Adapters/HikvisionAgv/：实现 IAgvStatusTapePlaybackBackend
   - Adapters/HighlightPlus/：HighlightPlusFocusHighlight（注入 HighlightHandler Func）
   - WmsDemo/：本项目 WMS 演示用 Prefabs / Resources / Sprites（含 RecordPlaybackControl、业务页面模板等）
   - 适配器使用默认程序集，以便引用无独立 asmdef 的插件；VLC/Vuplex 实现已在 Core

宿主项目需提供的服务预制体
- 放在任意 Resources/Services/ 下，与代码约定一致：
  - Services/DigitalTwinUIManager（[ServicePrefab]）
  - Services/MQTTManager（[ServicePrefab]）
  - 其他项目自有 Service（Timer、DataHub 等）按项目配置
- 注视相机：在场景 FocusPointManager 上配置 m_focusCameraPrefab（或直接指定 m_camera），
  不再从 Resources 加载。示例预制体见 FrameSample/WmsDemo/Resources/注视相机.prefab

可选适配器接线（无反射）
- AGV：挂 HikvisionAgvTapeBackend（可注册到 FrameOptionalServices）或拖到 AgvStatusTapePlayer.m_backendBehaviour
- 高亮：外部给 FocusPointManager.HighlightHandler 赋值，或挂 HighlightPlusFocusHighlight 自动注入
- 缺失时：AGV 播放跳过并 Warning；Focus 需要高亮但未注入 Handler 时 Warning 一次并跳过

录制回放 UI
- 直接使用 FrameSample/WmsDemo/Prefabs/RecordPlaybackControl.prefab（不再用 Editor 脚本生成）

外部插件（宿主提供）
- Assets/Plugins/Vuplex、VLCUnity、MQTT
- HighlightPlus、HikvisionAgv（经 FrameSample/Adapters）
- DOTween / UniTask / Cinemachine / NonsensicalKit

安全提示
- Authorization / MQTT 密码等勿把生产密钥打进默认资产。

已知债务
- URLS 仍含 Twin/海康路径；命名空间未全面统一
- asmdef 见 Core/BizKit；Adapters 故意不设独立 asmdef（插件多在默认程序集）
