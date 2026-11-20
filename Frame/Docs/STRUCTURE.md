# Frame 目录结构

```
Frame/
  Core/                 # 通用框架 → 程序集 Frame.Core（+ Mqtt / VLC / Cinemachine / WebGL 可选）
    Scripts/
      Abstractions/
      Binding/
      Component/
      Config/
      DigitalTwinUI/
      FocusCamera/
      Network/
      RecordPlayback/
      SubScene/
      Table/
      UI/Controls/
      Utils/
      VideoPlayer/
      Window/
    Editor/             # 菜单在 Tools/Frame/
  BizKit/
    WMS/                # 仓储业务 → Frame.BizKit.WMS（非仓储勿依赖）
      Scripts/
        Optional/
          Hikvision/        # Frame.BizKit.WMS.Hikvision（autoReferenced=false）
          HikvisionAgv/     # HikvisionAgv.Runtime
          EquipmentStatus/  # autoReferenced=false
          ZCalendar/        # Frame.BizKit.WMS.ZCalendar（SearchTool）
  ThirdParty/
    ZCalendar/          # 随包加载；Sample 预制体嵌套引用此处 GUID
  Docs/

Samples~/FrameSample/   # Package Manager「Frame Sample」
  ThirdParty/           # XCharts
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
- 在未 Import Sample 且未自备 XCharts 时，假定图表 binder 一定存在

## 命名空间
- 源文件带 `namespace`（asmdef 的 rootNamespace 只影响编辑器新建脚本）。
- Core：`Frame.Core.*`；MQTT：`Frame.Core.Mqtt`；VLC：`Frame.Core.VLC`
- BizKit：`Frame.BizKit.WMS.*`；海康站点：`Frame.BizKit.WMS.Hikvision`；日历检索：`Frame.BizKit.WMS.ZCalendar`
- 海康运行时：`HikvisionAgv`；设备状态组装：`EquipmentStatus`

## 可选依赖
见 [OPTIONAL_DEFINES.md](OPTIONAL_DEFINES.md)。已取消手写 FRAME_* 项目宏；WebGL / Cinemachine 用 versionDefines。
