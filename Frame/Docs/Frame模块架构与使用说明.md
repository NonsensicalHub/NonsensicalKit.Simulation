# Frame 模块架构与使用说明

> 目标定位：**Frame 是一套与具体项目业务解耦的通用框架，核心能力是"模板预制体 + JSON 数据"驱动 UI 与数字孪生功能的快速生成**，最终希望做到——新项目只需准备「UI 模板预制体」与「业务 JSON」，即可低代码/零代码拼出一整套项目 UI 与配套运行时功能（弹窗、世界空间标签、网络接入、设备信息、报警、表格图表、检索等）。
>
> 本文基于当前代码库（`Assets/Frame/**` 本体 + `Assets/FrameSample/**` 示例）梳理；Frame 放入宿主已有 package，不单独维护 package.json。覆盖：整体架构、核心引擎（JSON⇄Prefab 绑定系统）、支撑子系统、配套资源，以及朝着"模板+JSON 快速开发新项目"目标的差距分析与建议路线。

---

## 1. 目录总览（Frame 本体 + FrameSample 示例）

- **`Assets/Frame`**：框架本体，放入宿主**已有** package / 工程使用（不单独维护 `package.json`）。
- **`Assets/FrameSample`**：示例（Adapters 可选适配器 + WmsDemo 本项目演示资源）。
- 通用脚本有 asmdef（如 `Frame.Core`、`Frame.BizKit.WMS`）；脚本 `.meta` GUID 随迁移保留。

```
Assets/Frame/
├─ Core/                          ★通用框架（新项目优先只依赖本层）
│  ├─ Scripts/
│  │  ├─ Abstractions/            可选插件接口 + FrameOptionalServices
│  │  ├─ Binding/ Window/ DigitalTwinUI/ Network/ Config/
│  │  ├─ RecordPlayback/ FocusCamera/ Table/ UI/ Component/ VideoPlayer/ SubScene/
│  │  └─ …
│  ├─ Editor/                     JSON Binding 等
│  └─ Frame.Core.asmdef
├─ BizKit/WMS/                    ★仓储/冷链业务件（非仓储可不引用）
│  ├─ Scripts/ … / Editor/
│  └─ Frame.BizKit.WMS.asmdef
├─ （ThirdParty 已迁至 Samples~/FrameSample/ThirdParty）
├─ Docs/                          本文档
└─ README.txt

Assets/FrameSample/
├─ Adapters/
│  ├─ HikvisionAgv/               IAgvStatusTapePlaybackBackend
│  └─ HighlightPlus/              注入 HighlightHandler Func
├─ WmsDemo/
│  ├─ Prefabs/                    RecordPlaybackControl、业务壳等
│  ├─ Resources/                  页面模板、设备/库存管理、注视相机示例等
│  └─ Sprites/
└─ README.md

宿主项目（示例）：
Assets/Resources/Services/        DigitalTwinUIManager、MQTTManager 等（项目配置，非 Frame 自带）
```
### 1.1 本轮已知债务（刻意未拆）

- VLC/Vuplex 实现在 Core；可选海康/高亮适配器在 `Assets/FrameSample/Adapters/`；外部插件仍放宿主 Assets/Plugins。
- **生产密钥勿打进包默认资产**（Authorization / MQTT 密码请本地覆写）。

- `URLS` 内仍含 `GetTwin*` / 海康 RCS 路径方法 → 留在 Core，避免本轮改 Request 调用点。
- `BindingSliderInputField`（含货架文案）仍在 Core `Binding/`，但已无 BizKit 类型引用。
- 已有 Frame.Core / Frame.BizKit.WMS 等 asmdef；持续避免 Core 引用 Biz。FocusCamera 注视相机改为 Inspector 配置预制体。
- **已消除 Core → BizKit 类型依赖**：`BindingSearchTool` 在 BizKit；`URLBaseConfig.SnCodeUpdater` / `TopicPropertyDrawer.ResolveDeviceSnCodes` 由 BizKit Editor 注册。

### 1.2 数据源开关（独立配置）

- 资产：`Assets/Resources/Configs/TwinDataSourceConfig.asset`（`Mode`: Offline / Online）
- 运行时：`TwinDataSource.TryInit(config)`；业务用 `IsOffline` / `IsOnline`
- Mock：继承 `MockDataProviderBase`，仅离线（或 `m_forceEnable`）时启用
- **不要**再往 `URLBaseConfig` 里加数据源开关
---

## 2. 核心引擎一：JSON ⇄ Prefab 双向绑定系统（Binding）

这是实现"模板 + JSON 快速出 UI"的**关键机制**，设计思想：

- 美术/UI 同学先搭好一份**模板预制体**（普通 UGUI 层级），在需要动态赋值的节点上挂 `BindingXxx` 组件，并填写 **Key**（JSON 路径，多级用 `/` 分隔）。
- 策划/后端提供一份 **JSON**，字段名与模板上的 Key 对应。
- 通过代码（运行时）或编辑器工具（离线一次性生成），把 JSON 的值"灌入"模板节点，生成**成品预制体/成品 UI**。
- 同一套机制还支持**反向**：从一个已经填好数据的预制体，反向导出一份 JSON（用于把现有 UI"沉淀"为数据模板）。

### 2.1 基类：`BindingToken`

路径：`Assets/Frame/Core/Scripts/Binding/BindingToken.cs`

```csharp
public abstract class BindingToken : MonoBehaviour
{
    [Tooltip("绑定路径，多级用 '/' 分隔。顶层示例: Title；嵌套示例: Head/Title")]
    [SerializeField] protected string m_key;

    public string Key => m_key;
```
所有绑定组件继承自它，统一约定两个方法：
- `BindToken(JToken token)`：**正向**，把取到的 JSON 值写入自身（Text/Sprite/Dropdown/子节点生成…）。
- `CollectJson()`：**反向**，把自身当前状态序列化为 `JToken`，用于导出 JSON。

### 2.2 绑定组件矩阵

| 组件 | 作用 | JSON 形态 | 双向 |
|---|---|---|---|
| `BindingText` | `TMP_Text` 文本 | 字符串 或 `{ "value": "..." }` | ✅ |
| `BindingInputField` | `TMP_InputField` 文本 | 字符串 或 `{ "value": "..." }` | ✅ |
| `BindingButtonText` | `Button` 子节点文案 | 字符串 或 `{ "value": "..." }` | ✅ |
| `BindingSprite` | `Image` 精灵（`Resources.Load`） | 字符串 或 `{ "value": "路径" }` | ✅（记录绑定路径；未绑定时回退 sprite 名） |
| `BindingRawImage` | `RawImage` 纹理（`Resources.Load`） | 字符串 或 `{ "value": "路径" }` | ✅ |
| `BindingColor` | `Graphic` / `TMP_Text` 颜色 | `"#RRGGBBAA"` 或 `{ "r","g","b","a" }` / `{ "value": "#..." }` | ✅ |
| `BindingFillAmount` | `Image.fillAmount` | `0~1` number 或 `{ "value": 0.75 }` | ✅ |
| `BindingToggle` | `Toggle.isOn` | bool / 0\|1 / `"true"`，或 `{ "value": ... }` | ✅ |
| `BindingSlider` | `Slider` 数值 | number，或 `{ "value", "min"?, "max"? }` | ✅ |
| `BindingDropDown` | `TMP_Dropdown` 选项 | 字符串数组 | ✅ |
| `BindingObject` | 容器：递归绑定子节点 | JSON Object | ✅ |
| `BindingArray` | 列表：实例化 `itemPrefab` | JSON Array（元素为 Object） | ✅ |
| `BindingPages` | 多页：`$prefab` + `$content` | `[{ "$prefab", "$content" }, ...]` | ✅ |
| `BindingSliderInputField` | 滑条+输入框组合 | `{ "绑定信号", "货架切片数量" }` | ✅ |
| `BindingSearchTool` | `SearchTool` 检索类型 | `[{ "Text", "Type" }]` | ✅ |
| `BindingWeb` | 内嵌浏览器 | `{ "Url", "UrlType" }` 或字符串 URL | ✅ |
| `BindingNodeActive` / `Switcher` / `Max` / `ActiveType` | DAG 逻辑节点 | 字符串 / 字符串数组 | ✅ |

> 通用 UGUI（文本、输入、按钮文案、图、色、进度、开关、滑条、下拉、列表、分页）与业务组合控件均已覆盖；新增控件时继续按「一控件一 `BindingXxx`」扩展即可。

### 2.3 递归引擎：`JsonBindingHelper`

`Assets/Frame/Core/Scripts/JsonBindingHelper.cs` 是整个绑定体系的算法核心，提供：

- **`BindFirstLevel(go, dataObj)`**：广度优先查找 `go` 下"每条路径上第一个" `BindingToken`（找到即剪枝，不再往下找同分支），按其 `Key` 从 JSON 取值并调用 `BindToken`。容器组件（`BindingArray`/`BindingPages`）内部自己再调用 `BindFirstLevel` 递归其生成的子节点，从而支持任意深度嵌套。
- **`BindChildren(go, dataObj)`**：给 `BindingObject` 使用，遍历所有子孙 `BindingToken`（遇到 `BindingPages`/`BindingArray` 收录后剪枝，交给它们自行管理子树）。
- **`CollectFirstChildren(root)`**：反向版本，收集第一层 `BindingToken` 的 `CollectJson()` 结果，拼成 `JObject`。
- **`ResolveToken(root, path)`**：按 `/` 分隔路径在 `JObject`/`JArray` 中取值，支持数组下标。
- **`JsonBindingReport`**：绑定过程的统计/日志系统（成功数、缺失 Key 数、空 Key 数、异常数），供编辑器工具展示。

这套"剪枝式广度优先 + 容器自管理子树"的设计，使得**模板可以任意嵌套**（列表里套分页、分页里套列表都可行），且新增一种 UI 控件时只需新增一个 `BindingXxx : BindingToken` 子类，不需要改动核心引擎——**扩展性是这个体系的最大优点**。

### 2.4 编辑器工具：`JsonBindingEditorWindow`

`Assets/Frame/Core/Editor/JsonBindingEditorWindow.cs`，菜单 `Tools/JSON Binding Editor`，是面向**美术/策划**的可视化生产工具，功能：

1. **分析**：列出全部 `BindingToken`，并校验空 Key / 第一层重复 Key。
2. **由模板生成 JSON 骨架**：按 Binding 类型写入占位值，输出 `*_Skeleton.json`，可直接改成业务数据。
3. **校验 模板↔JSON**：对比第一层 Key（缺失 / 多余 / 空 / 重复）。
4. **正向「生成绑定预制体」**：选源模板 + JSON → 生成成品预制体（首次复制 / 再次原地更新保留 GUID；绑定后移除 BindingToken）。
5. **反向「导出 JSON」**：从仍带 Binding 的预制体导出当前数据 JSON。
6. 日志面板 + `EditorPrefs` 持久化输入项。

推荐工作流：**搭模板 → 生成骨架 → 填数据 → 校验 → 生成绑定预制体**。

### 2.5 JSON Schema 约定

实现：`Assets/Frame/Core/Scripts/JsonBindingSchemaHelper.cs`

1. JSON **根必须是 Object**；字段名 = Binding 的 `Key`（多级用 `/`）。
2. 容器占位：
   - `BindingArray` → `[ { /* itemPrefab 第一层 Key */ } ]`
   - `BindingPages` → `[ { "$prefab": "Resources预制体名", "$content": {} } ]`
   - `BindingObject` → `{ /* 子 Binding Key */ }`
3. 标量占位见 §2.2（`""` / `0` / `false` / `"#FFFFFFFF"` / `"Resources/路径"` 等）。

---

## 3. 核心引擎二：通用窗口系统（Window）

- **`WindowConfig`**：窗口外观配置（标题、图标、宽高、是否可全屏、是否单例、关闭回调）。
- **`WindowContainer`**：窗口容器 MonoBehaviour，负责标题栏、关闭按钮、全屏切换、内容加载；内容预制体需实现 `IWindowContent`（`OnContentInit(args)` / `OnContentClose()`），容器与内容解耦。
- **`WindowContainerManager`**：全局窗口管理器（通过 `IOCC` 服务定位），支持**单例窗口**（同一预制体已开则复用/置顶）与**多实例窗口**、窗口池复用、多窗口层级焦点管理。
- **`IWindowContent` / `IWindowHandleReceiver`**：内容侧接口，`WindowHandle` 用于多开窗口时区分各自的事件频道，避免消息串台。

意义：任何"模板+JSON"生成出来的成品预制体，只要实现 `IWindowContent`，就能直接被 `WindowContainerManager.OpenWindow(config, prefab, args)` 弹出成一个标准弹窗——**窗口壳与内容彻底解耦**，新项目新增窗口不需要写窗口相关的通用逻辑。

---

## 4. 核心引擎三：数字孪生世界空间 UI（DigitalTwinUI）

用于在 3D 场景里的任意挂点上生成"悬浮标签/热点"，与业务解耦：

- **`DigitalTwinUIPoint`**（挂在 3D 场景节点上）：声明 `Type`（对应哪种标签模板）+ `ID`，`Start` 时向 `DigitalTwinUIManager` 注册。
- **`DigitalTwinUIManager`**（`ServicePrefab`，对象池管理）：按 `Type` 配置了一组标签 UI 预制体，`Register` 时从对象池取实例、调用其 `IDigitalTwinUI.Init(pointObj, info)` 完成世界空间挂载。
- **`UIPoint`**：挂在标签 UI 预制体上，通过 `IconID` 与 3D 挂点解耦通信，暴露 `OnClick/OnHoverEnter/OnHoverExit` 三个 `UnityEvent`，方便策划在 Inspector 里直接连交互逻辑，无需写代码。
- **`IDigitalTwinUI`**：标签预制体需实现的初始化接口。

意义：新项目只要在场景里摆点、配置好 `Type→Prefab` 映射，就能自动跑出一批数据标签点位系统，不用为每个新项目重写"3D 转屏幕坐标 + 显隐管理 + 对象池"的逻辑。

---

## 5. 网络与配置中心（Network / Config）

这一层是把 UI 和"真实数据源"对接起来的桥梁，同样走**配置驱动**：

- **`URLBaseConfig`（ScriptableObject）**：项目级 URL / MQTT 配置资产，核心字段：
  - `Servers: List<WarehouseServerEntry>`：**多后台 Key → BaseUrl** 映射。
  - `MqttTopics: List<MQTTConfig>`：MQTT 订阅主题配置，并有编辑器按钮一键"更新 SN Code / 添加 Replay 回放监听"。
- **`TwinDataSourceConfig`（ScriptableObject）**：**独立的离线/在线开关**（与 URL 解耦）。默认离线；切换为在线后 Twin 请求与 OnlineBinder 生效，Mock 提供者自动跳过。
- **`TwinDataSource`（静态）**：`TryInit(ConfigService)` 读取上述配置；业务侧用 `IsOffline` / `IsOnline` 分流。
- **`MockDataProviderBase`**：Mock 生成器基类，仅离线（或 `m_forceEnable`）时启用。
- **`URLS`（静态类）**：运行时从 `URLBaseConfig` 初始化，按 `serverKey` 拼接完整接口地址（内置仓储 Twin 接口路径仍偏本项目业务）。
- **`MQTTManager`（`ServicePrefab`）**：PC 端用 `MQTTnet`（WebSocket/TCP 可切换），WebGL 端走 `NonsensicalKit.WebGL` 桥接；支持自动重连、主题记录与重连后自动重订阅。
- **`MqttDataBridge` + `MqttPayloadParser`**：把 MQTT 报文解析为统一的 `MotionPartUpdate`/`DataHub` 点位更新，驱动数字孪生动画（输送线、堆垛机、提升机等）。
- **`RequestBase`**：HTTP 轮询请求的通用基类，内置计时器循环调用、开关控制，子类只需实现 `GetData(id)`。

---

## 6. 通用业务组件集

这些组件"半通用"——机制通用，但字段/文案上仍带有当前项目（仓储/冷链）色彩，是未来抽象为纯模板时需要重点梳理的部分：

- **`EquipmentInfos`**：设备信息展示（`EquipmentInfoManager` + `EquipmentDetailsElement` + `EquipmentElement`），配合 `EquipmentElementIconConfig`（图标类型配置 ScriptableObject）与 `EquipmentImageConfig`，按"设备类型 → 图标/名称/数量"批量生成图标点位，点击后订阅式获取详情（`GetEquipmentInfo` handler，天然支持"离线模拟数据生成器"和"真实接口"两套实现切换，见 `EquipmentSimulationDataGenerator`）。
- **`RealTimeAlarm`**：实时报警滚动列表 + 详情弹窗（`RealTimeAlarmTable`/`RealTimeAlarmElement`/`WarningDetail`），带自动轮播效果；同样配有 `RealTimeAlarmSimulationDataGenerator` 模拟数据源。
- **`Table` + `XChartSerieBinder`**：`TableControl` 是通用滚动表格（列宽/表头分离/订阅式刷新），`XChartSerieBinder` 封装第三方 `XCharts`，用于快速出折线/柱状/饼图。
- **`SearchTool`**：通用检索栏组件，检索类型（`SearchType`）、结果列表、日期范围选择器均可配置/可扩展，并通过事件总线（`SearchToolEvent`）与具体业务面板解耦——**可通过 `BindingSearchTool` 直接用 JSON 配置检索类型**，是"配置驱动"的典型示例。
- **货架/库位**：`ShelfSliceController`、`StorageAnatomyControl`、`BindingStorageAnatomyControl`——货架切片可视化的专用控制器（业务相关性较强）。

---

## 7. 配套资源、宿主 Services 与示例

- **宿主 `Resources/Services/`**（不在 Frame 包内）：项目自行放置 `DigitalTwinUIManager`、`MQTTManager` 等，与 `[ServicePrefab("Services/...")]` 约定一致；**不要**指望 Frame 目录自带这些预制体。
- **注视相机**：在场景 `FocusPointManager` 上配置 `m_focusCameraPrefab`（或指定场景内 `m_camera`）。示例资源：`Assets/FrameSample/WmsDemo/Resources/注视相机.prefab`。
- **`Assets/FrameSample/WmsDemo/`**：演示用 Prefabs / Resources / Sprites（含 `RecordPlaybackControl.prefab`、业务页面模板、`Sprites/ai image` 皮肤等）。录制回放 UI 直接引用该 Prefab，无需 Editor 生成脚本。
- **`Assets/FrameSample/Adapters/`**：可选 `HikvisionAgv` / `HighlightPlus` 接口实现；VLC / Vuplex 实现已在 Core（仍依赖宿主 `Assets/Plugins`）。
- **`Samples~/FrameSample/ThirdParty/`**：`XCharts`（图表库）、`ZCalendar`（日历选择控件）；不再放在 `Frame/` 下。图表 binder 见同 Sample 的 `Optional/XCharts`。

---

## 8. 一次完整的"模板 + JSON 出 UI"流程（现状可实现）

```mermaid
flowchart LR
    A[UI 搭建模板预制体<br/>挂 BindingXxx 组件并填 Key] --> B[策划/后端提供 JSON<br/>字段名对应 Key 路径]
    B --> C[Tools/JSON Binding Editor<br/>选模板 + 选JSON]
    C -->|正向: 生成绑定预制体| D[输出成品预制体<br/>已移除 BindingToken]
    C -->|反向: 导出 JSON| E[从已有预制体<br/>反推 JSON 结构]
    D --> F[挂到 WindowContainerManager<br/>作为弹窗内容 IWindowContent]
    D --> G[挂到 DigitalTwinUIManager<br/>作为世界空间标签 IDigitalTwinUI]
    D --> H[运行时也可直接调用<br/>JsonBindingHelper.BindFirstLevel<br/>做"热更"式数据刷新]
```

即：**离线一次性生成**（编辑器工具，产出干净预制体，适合"页面结构/静态图文"类模板）与**运行时动态绑定**（直接调用 `JsonBindingHelper`，适合列表、分页等"数据量随后台变化"的场景）两条路径都已经打通。

---

## 9. 差距分析（距离"模板+JSON 快速开发新项目"的理想状态还差什么）

| 现状 | 差距/风险 | 建议 |
|---|---|---|
| ~~绑定组件类型有限~~ | **已补齐** Toggle / Slider / InputField / FillAmount / Color / ButtonText / RawImage | 后续按项目需要再加（如 Scrollbar） |
| ~~部分组件反向导出未实现~~ | **已补齐** `BindingSliderInputField` / `BindingSearchTool` / `BindingSprite` / `BindingWeb` 的 `CollectJson` | — |
| ~~没有 JSON Schema / Key 校验工具~~ | **已完成**：骨架生成 + 模板↔JSON Key 校验（见 §2.4 / §2.5） | 后续可再补正式 JSON Schema 文件或 CI 校验 |
| ~~`EquipmentInfos`、`RealTimeAlarm` 等与 Core 混在同一目录~~ | **已完成物理分层**；Core 不再引用 BizKit 类型 | 下轮：引入 asmdef 强制隔离；将 `URLS` 的 Twin 路径迁出 Core |
| ~~离线模拟与在线切换未统一~~ | **已完成**：`TwinDataSourceConfig` 独立配置 + `MockDataProviderBase`；Mock 仅离线启用 | 新 Mock 继续继承 `MockDataProviderBase` |
| 编辑器工具已支持"原地更新保留 GUID" | 但缺少批量处理（比如一个目录下多个模板+多个 JSON 批量生成）、缺少命令行/CI 集成 | 若要支撑"新项目快速起量"，可加一个批处理入口（读取一个映射清单，批量跑生成），便于团队规模化复用 |
| 暂无面向新项目的"脚手架"入口 | 目前更多是"组件库"形态，新项目仍需人工挑选、拼装、接线（服务注册、窗口配置等） | 可考虑做一个项目初始化模板（Package/Template 工程）或一份 Checklist 文档，标准化"新项目接入 Frame"的步骤 |
| 文档现状：仅 `README.txt` 简述目录结构 | 使用流程、Key 规则、各 BindingXxx 的 JSON 范例散落在代码注释里 | 本文档可作为起点；建议后续为每个 `BindingXxx` 补充一份最小 JSON 示例 + 截图，沉淀到 `Docs/` 下，方便非程序同学自助使用 |

---

## 10. 面向目标的建议路线图

1. ~~**分层瘦身**~~：**已完成**目录级 Core / BizKit.WMS 拆分，并消除 Core → BizKit 类型依赖。下轮补 asmdef、把 `URLS` Twin 路径迁出 Core。
2. ~~**补全 Binding 组件矩阵**~~：**已完成**通用控件双向绑定 + 既有组件 CollectJson 补全（见 §2.2）。剩余按需扩展即可。
3. ~~**JSON Schema 化**~~：**已完成**骨架生成、Key 校验与文档约定（§2.4 / §2.5）。
4. ~~**Mock/在线开关标准化**~~：**已完成**独立 `TwinDataSourceConfig`（不放在 URLBase）+ `MockDataProviderBase`。
5. **批量生成与脚手架**：编辑器工具增加批处理模式，并沉淀一份"新项目接入 Frame 模块"的操作手册/模板工程，把当前"人工挑组件拼装"的过程标准化、可复制。
6. **文档与示例常态化**：以本文件为基础，后续每次新增 BindingXxx / 新增通用组件时同步补充到 `Assets/Frame/Docs/`，避免再次出现"只能翻代码找用法"的情况。

---

*本文档基于 `Assets/Frame/**` 现有代码梳理生成，若后续模块有调整，建议同步更新本文件对应章节。*
