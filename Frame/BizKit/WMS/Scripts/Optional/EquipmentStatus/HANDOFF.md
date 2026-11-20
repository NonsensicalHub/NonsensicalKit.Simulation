# 设备管理面板 · 运行状态采集 Handoff

> **给 Agent**：用户要「在设备管理面板展示某类新设备状态」时，先完整阅读本文，再按清单改代码。  
> **给用户**：新增设备前，把文末「用户填写模板」填好发给 Agent 即可。

---

## 1. 目标与边界

### 做什么
- 在线 / 回放时，把 AGV、堆垛机、提升机等设备点位接到 **设备管理** 面板的 `GetEquipmentInfo`。
- 定时刷新当前类型列表；字段释义按机型文档映射成中文。

### 不做什么
- **尽量不改** `Assets/Frame/BizKit/WMS/Scripts/EquipmentInfos/**`（面板 UI 框架）。
- **不破坏** 框架离线 Mock：`EquipmentSimulationDataGenerator` 必须能独立跑（无本目录脚本时 Offline 仍出随机详情）。
- 不改 Hikvision / MQTT Bridge 核心，除非用户明确要求。
- 不要强制改 `DataExample` 场景挂载；组件由用户自己挂。

### 框架可独立运行（硬性）
| 有无本目录脚本 | Offline | Online |
|----------------|---------|--------|
| 无 | Mock 注册 `GetEquipmentInfo`，面板正常 | 无真数据 Handler（框架原本如此） |
| 有 | QueryService 可按策略挂起 Mock 并接管 | QueryService 接管，走 Cache |

Frame → Scripts：**零硬依赖**。Scripts → Frame：可选调用 `SetEquipmentInfoSuspended`。

---

## 2. 架构（必读）

```text
数据源
  AGV HTTP  → Hikvision*Raw 事件
  DDJ/TSJ   → receivedMQTTData（含 RecordDataPlayer 回放）
       ↓
EquipmentStatusCollector   // 只写缓存
       ↓
EquipmentStatusCache       // DeviceCode → pointCode → value
       ↓
面板 Execute("GetEquipmentInfo", {DeviceCode, DeviceName})
       ↓
EquipmentInfoQueryService  // 唯一业务侧 Handler；控制是否挂起 Mock
       ↓
EquipmentStatusFormatter   // 门面：DetectKind → 各机型 Builder
       ↓
EquipmentDetailsElement.RefreshStatus(SimpleInfo + Dictionary)

EquipmentInfoLiveRefresher // 订阅 showEquipment，每秒 RefreshVisible(type)
```

### 关键文件

| 路径 | 职责 |
|------|------|
| `Assets/Scripts/EquipmentStatus/EquipmentStatusCollector.cs` | 订阅数据源 → Cache |
| `Assets/Scripts/EquipmentStatus/EquipmentStatusCache.cs` | 点位缓存 |
| `Assets/Scripts/EquipmentStatus/EquipmentInfoQueryService.cs` | 注册/注销 `GetEquipmentInfo`；挂起 Mock |
| `Assets/Scripts/EquipmentStatus/EquipmentInfoLiveRefresher.cs` | 按类型 1s 刷新 |
| `Assets/Scripts/EquipmentStatus/EquipmentStatusFormatter.cs` | `DetectKind` + `Build` 分流 |
| `Assets/Scripts/EquipmentStatus/Formatting/*Builder.cs` | 各机型字段组装与释义 |
| `Assets/Scripts/EquipmentStatus/Formatting/EquipmentStatusFormatUtil.cs` | 共用工具 / PartConfig 标签 |
| `Assets/Frame/.../EquipmentSimulationDataGenerator.cs` | 框架 Offline Mock（勿删核心逻辑） |
| `Assets/Frame/.../EquipmentInfoManager.cs` | 仅允许保留/使用已有的 `RefreshVisible` |

### 面板契约（勿破坏）

**入参** `Dictionary<string,string>` 必须含：

- `DeviceCode`
- `DeviceName`

**出参** `(SimpleInfo, Dictionary<string,string>)`：

- `SimpleInfo.Quest` / `CompletionStatus` / `Color` / `Status`（0 正常 / 1 中间 / 2 故障）
- Dictionary **必须含** `DeviceCode`；其余 key 为明细行标题（中文）

设备列表示例 ID 来自 `EquipmentElementIconConfig`（如 `TSJ_01`、`DDJ_08`、`12497`），必须与报文 / PartConfig 的 `DeviceCode`/`partID` 一致。

---

## 3. 新增一类设备：Agent 操作清单

按顺序执行，缺信息先问用户（用文末模板）。

### Step A — 确认数据从哪来
- [ ] **MQTT**（类似堆垛机/提升机）：主题类型字符串？`DeviceCode` 前缀？是否已有 `receivedMQTTData`？
- [ ] **HTTP/其它事件**：事件名、DTO 字段、是否已有 Bridge？
- [ ] **仅 Config/静态**：无实时流则只做占位或读本地配置。

### Step B — Collector 接入（有实时流时）
在 `EquipmentStatusCollector` 增加订阅或在现有 `OnMqttReceived` 中放行新 `TopicType`：
- 写入 `Cache.SetDeviceName` + `Cache.SetPoints` / `SetPoint`
- **不要**在 Collector 里拼 UI 文案

### Step C — Kind 与分流
1. `Formatting/EquipmentKind.cs` 增加枚举值（如 `Conveyor`）
2. `EquipmentStatusFormatter.DetectKind`：按 `DeviceCode` 前缀 / 白名单识别
3. 新建 `Formatting/XxxEquipmentStatusBuilder.cs`
4. `EquipmentStatusFormatter.Build` 的 `switch` 增加分支

### Step D — Builder 实现规范
- 用 `EquipmentStatusFormatUtil`（`IsTruthy` / `OrDash` / `MakeSimple` / `TryAddLabeled`）
- 主字段手写中文 key；次要点位可用 `TryAddLabeled` + PartConfig `pointName`
- `Status` 灯：故障 → 2；忙碌/中间 → 1；空闲/正常 → 0
- 枚举释义写在 Builder 旁或同目录 `*Maps`，注释标明来源文档路径

### Step E — 面板配置（通常不用改代码）
- `EquipmentElementIconConfig`：类型名、`CustomIDStrings` 与真实 `DeviceCode` 对齐
- PartConfig：`Assets/Resources/PartConfigs/{资源名}.asset`（AGV 为 `AGV_{车号}`，其它多为 `{DeviceCode}`）

### Step F — 自测
- [ ] Offline 无本业务脚本时，Mock 仍出数（框架）
- [ ] Online / 回放有数据时，点对应类型 Tab，详情与灯正确
- [ ] `LiveRefresher` 下数值会变
- [ ] 打开设备类型后 `DeviceCode` 与缓存键一致（常见坑）

### 禁止
- 大改 `EquipmentDetailsElement` / 用 `FindObjectsByType` 每秒扫场景
- 在 Frame Mock 里写死业务机型逻辑
- 删除或掏空 `EquipmentSimulationDataGenerator` 的 Offline Handler 能力

---

## 4. QueryService 与 Mock 协作（谁回答「设备详情」）

面板每次 `Execute("GetEquipmentInfo")` 只能有一边在回答。  
`EquipmentInfoQueryService` 负责在「真数据」和「框架随机 Mock」之间切换：

| 场景 | 谁回答 | 面板看到什么 |
|------|--------|--------------|
| Twin **在线** | QueryService（默认） | Collector 缓存的真点位 |
| **RecordDataPlayer 回放**（`MQTTUseOfflineData=true`） | QueryService（默认） | 回放灌进 Cache 的真点位 |
| Twin **离线** 且 **没在回放** | 框架 `EquipmentSimulationDataGenerator` | 随机占位文案 |

为什么回放也要接管：回放常会把 Twin 切成 Offline；若不接管，Mock 会重新注册，面板变成随机数，孪生在动、面板不动。

Inspector（默认都勾选）：

| 字段 | 直白含义 |
|------|----------|
| `m_overrideMockWhenOnline` | 在线时要不要由本组件接管（一般保持勾选） |
| `m_overrideMockDuringMqttPlayback` | 回放时要不要由本组件接管（一般保持勾选；取消则回放时回到随机 Mock） |

挂载提示（用户自行挂，Agent 不要强行改场景）：
- `EquipmentStatusCollector`
- `EquipmentInfoQueryService`（引用 Collector + 可选 SimulationGenerator）
- `EquipmentInfoLiveRefresher`（可选）

---

## 5. 现有机型速查

| Kind | DeviceCode 识别 | 数据入口 | Builder | 释义要点 |
|------|-----------------|----------|---------|----------|
| Agv | 数字车号 / HikvisionAgvSite | StatusRaw + TaskRaw | `AgvEquipmentStatusBuilder` | 机器人 status 见 `HikvisionAgv/Docs/机器人工作状态枚举说明.md`；taskStatus：1已创建 2正在执行 5取消/完成 9已结束；taskTyp 见 `任务类型说明.md` |
| Stacker | `DDJ*` | MQTT TopicType 堆垛机 | `StackerEquipmentStatusBuilder` | RunStop→空闲/非空闲；AutoManual 1自动0手动；Fault 1故障0正常 |
| Lifter | `TSJ*` | MQTT TopicType 提升机 | `LifterEquipmentStatusBuilder` | LiftStatus 1升2降3静；Floor 0/50/100→1/2/3层；Process 1~5 流程文案 |

回放链路：`RecordDataPlayer` → `MQTTMessage` → Aggregator → `receivedMQTTData` → Collector。

---

## 6. 用户填写模板（复制给 Agent）

```markdown
## 新增设备面板展示

### 基本信息
- 设备类型中文名：
- 面板 Tab 类型名（IconConfig.Type，需已有或一并配置）：
- DeviceCode 规则（前缀/列表）：例 TSJ_01 / DDJ_08 / 12497
- 是否已在 EquipmentElementIconConfig 配好 ID：是 / 否

### 数据源
- 类型：MQTT / HTTP / 其它 IOCC 事件 / 仅离线占位
- MQTT TopicType 或主题：
- 事件名（若非 MQTT）：
- 是否走 RecordDataPlayer 回放：是 / 否
- 样例 JSON（贴一条完整报文）：

### 点位与释义（必填要展示的字段）
| PointCode | 面板显示名 | 取值释义（枚举/公式） | 是否主字段 |
|-----------|------------|----------------------|------------|
|           |            |                      |            |

### 状态灯规则
- Status=2（红）条件：
- Status=1（黄）条件：
- Status=0（绿）条件：

### SimpleInfo
- Quest（任务信息）用哪个点位/文案：
- CompletionStatus（右侧摘要）用哪个：

### 约束
- 不要大改 Frame/EquipmentInfos
- 保持 EquipmentSimulationDataGenerator 离线可独立运行
- 新代码放 Assets/Scripts/EquipmentStatus/
```

---

## 7. Agent 完工自检

- [ ] 新 Kind + Builder + DetectKind + Collector（如需）已接上  
- [ ] 出参含 `DeviceCode`；灯 0/1/2 符合用户规则  
- [ ] 未破坏 Offline Mock；QueryService 挂起逻辑仍正确  
- [ ] 未强制改用户场景挂载  
- [ ] 用用户样例 JSON/回放路径口头说明如何验证  

---

*文档位置：`Assets/Scripts/EquipmentStatus/HANDOFF.md`。变更架构时请同步更新本文第 2、5 节。*
