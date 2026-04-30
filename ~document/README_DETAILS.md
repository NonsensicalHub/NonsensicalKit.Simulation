# NonsensicalKit.Simulation 详细介绍

`com.nonsensicallab.nonsensicalkit.simulation` 是模拟实训能力包，提供可组合的训练场景基础系统，覆盖交互、任务、库存、导航、参数化货架与 UI 拖拽等典型能力。

---

## 核心模块一览

### DragSystem

- **模块定位**：统一拖拽交互事件与状态流转。
- **核心入口**：`DragDropSystem`、`DragTarget`、`DropTarget`、`DragIcon`
- **使用方法**：
  1. 通过 `ServiceCore` 获取 `DragDropSystem`。
  2. 在 UI 上触发 `RaiseBeginDrag(...)` / `RaiseDrag(...)` / `RaiseDrop(...)`。
  3. 在目标端按来源和对象类型判定是否接收。

### InteractSystem

- **模块定位**：管理交互目标入队、切换与执行。
- **核心入口**：`InteractQueueSystem`、`TriggerInteractableObject`、`InteractableObject`
- **使用方法**：
  1. 准备交互系统服务与交互菜单 UI。
  2. 可交互对象挂载 `TriggerInteractableObject`（或继承 `InteractableObject`）。
  3. 玩家输入调用 `Switch()` 和 `Interact()` 执行交互。

### MissionSystem

- **模块定位**：推进任务状态并广播任务变化。
- **核心入口**：`MissionSystem`、`MissionConfigurator`、`MissionData`
- **使用方法**：
  1. 创建并配置 `MissionData`（任务 ID、类型、前置关系）。
  2. 场景挂载 `MissionConfigurator` 并注入任务列表。
  3. 在触发点挂载 Listener/Trigger 推进任务节点。

### NetNavigation

- **模块定位**：在路网中进行路径搜索与运动执行。
- **核心入口**：`Net`、`NetPoint`、`NetMover`、`NodePath`
- **使用方法**：
  1. 布置 `NetPoint` 并配置连接关系。
  2. 移动物体挂载 `NetMover` 并绑定 `Net`。
  3. 调用 `Move(...)` 下发目标点或目标位置。

### ParametricModelingShelves

- **模块定位**：动态生成货架并控制货位显隐状态。
- **核心入口**：`ShelvesBuilder`、`ShelvesManager`、`ShelvesPrefabConfig`
- **使用方法**：
  1. 配置货架参数与预制体规则。
  2. 调用 `Rebuild()` 完成结构生成。
  3. 运行时通过 `LayerIntervals` / `SetLoadsVisible(...)` 调整展示。

### UnitAndItem

- **模块定位**：管理物品配置、库存实体和物流对象。
- **核心入口**：`InventorySystem`、`InventoryConfigurator`、`ItemEntity`、`InventoryEntity`
- **使用方法**：
  1. 用 `InventoryConfigurator` 初始化 `ItemData[]`。
  2. 初始化阶段注册 `InventoryData` 到 `InventorySystem`。
  3. 通过 `StoreItem(...)` / `TakeItem(...)` / `MoveItem(...)` 驱动库存行为。
