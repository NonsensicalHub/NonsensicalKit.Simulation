# SOP：海康 AGV 设备孪生

现场车号与 RCS 地图简称以 **`HikvisionAgvSiteConfig`** 为准（`Assets/Resources/CusConfigs/HikvisionAgvSiteConfig.asset`）。当前默认：一楼 **AA** 车 **12497**；二楼 **BB** 车 **30395 / 30396 / 30397 / 30399 / 30400 / 30402**。车不跨层。

脚本位置：`Assets/Scripts/HikvisionAgv/Network/`（请求基类、状态/任务查询、Bridge、TwinActor；DTO 在 `Network/Models/`）。

菜单 **Tools → 海康AGV → 接口与孪生测试**：直连 RCS 测接口；「驱动孪生」注入模板；「录制回放」按 StatusRequest 同样的一层一次 queryAgvStatus 记全车状态（带时间戳 JSONL），再按时间顺序 `IOCC.Publish` 回放。

## 关键约定

- `queryAgvStatus`：**一层一次请求**，`data[]` 含该层全部车。
  - `reqCode` = 请求流水号（GUID），**不是**车号。
  - 地图列表读 `HikvisionAgvSiteConfig` 各层 `MapShortName`，每个周期按层依次 POST。
- `queryTaskStatus`：**必须带车号**，按需逐车请求（不自动轮询）。
  - `reqCode` = 请求流水号；`agvCode` = 车号。
  - 车号列表读现场配置。
- 状态默认 **0.5s** 绝对节拍轮询；上一拍未结束则跳过本拍。
- 禁用组件 / `SetEnable(false)` 会停掉轮询。

## 1. 前置

1. 一楼/二楼 MapCfg 已导入。`HikvisionMapFrame.MapId` = XML 内部 MapID；RCS 的 AA/BB 填在地图框的 **MapShortName**（以及 `HikvisionAgvSiteConfig`）。离线模拟沿该层路网走。
2. `URLBaseData` 中 Server Key「海康接口」已配置（联通阶段再关离线模拟）。
3. 场景有 `DataHub`。海康 AGV **不依赖** RequestBase 循环 Timer。

## 2. PartConfig（DataHub）

菜单：**Tools → 海康AGV → 生成现场 PartConfig（7台）**

`partID` = 纯数字车号。点位顺序由 `HikvisionStatusPointCodes.All` 约定（Index = `pointConfigs` 下标）。

| Index | Code | 名称 |
|------:|------|------|
| 0 | PosX | 平面X |
| 1 | PosY | 平面Y |
| 2 | RobotDir | 朝向 |
| 5 | Stop | 暂停 |
| 11 | TaskCode | 任务号 |
| 12 | TaskStatus | 任务状态 |
| 13 | TaskTyp | 任务类型 |

## 3. 场景挂载

| 组件 | 说明 |
|------|------|
| `HikvisionAgvStatusRequest` | 地图列表读现场配置；0.5s 一轮；每层一次 POST，拿回该层全部车 |
| `HikvisionTaskStatusRequest` | 按需；车号读现场配置；`RequestOnce()` 逐车请求，或 `RequestVehicle(车号)` |
| `HikvisionDataBridge` | 状态按 `robotCode` → DataHub + MotionPartUpdate；任务按 `agvCode` → 仅 DataHub |

每台车：`HikvisionAgvTwinActor` 的 **m_partID = 车号**，绑本层 `HikvisionMapFrame`。

数据链路：

```
Status: 配置的每层 1 次 POST → data[] 该层多车
  → Bridge 按 robotCode 拆开
  → DataHub.ReceivePoints（一批）+ MotionPartUpdate(车号)
  → TwinActor

Task: 按需，每车 1 次 POST（agvCode=车号）
  → Bridge 按 agvCode 写入 DataHub 任务点
```

## 4. 离线验收

1. Play（默认离线模拟），Status 自动轮询；Task 点「立即拉取」或调 `RequestOnce()`。
2. 一层一次模拟应带出该层全部车号；DataHub 按 partID 可见点位。
3. Bridge「模拟现场全部车辆状态」可单独测分发。

## 5. 联通时

取消 **离线模拟**。`clientCode`/`tokenCode`、`reqTime` 格式按现场文档再调。

## 6. 录制回放（编辑器）

菜单 **Tools → 海康AGV → 接口与孪生测试** → **录制回放**：

1. **录制**：不需要 Play。周期默认 0.5s（上一拍未结束则跳过）。勾选「全部楼层」则按现场配置逐层 POST `queryAgvStatus`，否则只录当前层。每层应答含该层全部车，带 unix 毫秒时间戳，写入所选目录的 `agv-status-yyyyMMdd-HHmmss.jsonl`（默认项目根下 `AgvStatusRecords/`）。失败帧也会写入，回放时跳过。
2. **回放**：加载 JSONL，Play 且场景有 `HikvisionDataBridge` 后按时间戳顺序 `IOCC.Publish(HikvisionAgvStatusRaw, map, data[])`，与运行时 StatusRequest 同一条链路。支持速度、循环、单帧步进。回放前请关掉场景里的 `HikvisionAgvStatusRequest`，避免真车轮询覆盖。

## 7. 当前缺口

- 场景可能尚无 BB 路网与全部 Actor：有 AA MapFrame 时可先验收一楼。
