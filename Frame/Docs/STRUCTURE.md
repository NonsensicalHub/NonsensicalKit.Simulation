# Frame 目录结构

```
Frame/
  Core/                 # 通用框架 → 程序集 Frame.Core（+ Mqtt / VLC 可选）
    Scripts/
      Abstractions/
      Binding/          # BindingToken 族 + BindableParam / JsonBinding*
      Component/
      Config/
      DigitalTwinUI/
      FocusCamera/      # 含 Solo 相机、CameraControl 配置
      Network/
      RecordPlayback/
      SubScene/
      Table/
      UI/
        Controls/       # DetailLabel*、ButtonDOMove、SliderInputField 等
      Utils/
        EditMode/       # EditModeParticleController
      VideoPlayer/
      Window/
    Editor/
  BizKit/
    WMS/                # 仓储业务 → Frame.BizKit.WMS（非仓储勿依赖）
      Scripts/
        Optional/
          Hikvision/    # Frame.BizKit.WMS.Hikvision
          ZCalendar/    # Frame.BizKit.WMS.ZCalendar（SearchTool）
  Docs/

Samples~/FrameSample/   # Package Manager「Frame Sample」
  ThirdParty/           # XCharts、ZCalendar（非 Frame API；原 Frame/ThirdParty）
  Optional/
    XCharts/            # Frame.Core.XCharts（XChartSerieBinder）
  Adapters/
  WmsDemo/
  WindowExamples/
  Scenes/
```

## 依赖方向（允许）
`Sample / Optional` → `BizKit.WMS` → `Core` →（宿主 / NonsensicalKit / 插件 DLL）

## 禁止
- `Frame.Core` 引用 `Frame.BizKit.*`
- 把仓储业务类型上提到 Core
- 在未 Import Sample 且未自备第三方时，假定 XCharts / ZCalendar 一定存在

## 命名空间
- 目录 Network/（原 NetWork）→ Frame.Core.Network / Frame.BizKit.WMS.Network；MQTT → Frame.Core.Mqtt。
- 详见各 asmdef 的 
ootNamespace 与源文件 
amespace。
