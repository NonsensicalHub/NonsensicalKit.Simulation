# NonsensicalKit.Simulation

`com.nonsensicallab.nonsensicalkit.simulation` 是模拟实训能力包，提供可组合的训练场景基础系统，覆盖交互、任务、库存、导航、参数化货架与 UI 拖拽等典型能力，并包含 Timeline 脚本动画（ScriptAnimation）与电影运镜（CineShot）模块。
需要纯虚拟模拟功能时导入这个包。

## 文档说明

- 包级说明见 `Documentation~/README_DETAILS.md`
- ScriptAnimation 模块说明见 `Documentation~/ScriptAnimation/README.md`
- CineShot 模块说明见 `CineShot/Docs/使用说明.md`

---

## 示例

- `Sample Training`：交互队列、任务推进、库存 UI 与子场景协同示例。
- `ParametricModelingShelves`：货架参数化生成、层间距调整与货位显隐示例。
- `WarehouseSimulation`：出入库仿真。
- `ScriptAnimation`：Timeline 脚本动画综合演示（路网、叉车、堆垛机、机械臂、开箱、缠膜；含 OpenBox / FilmWrapping 子场景）。
## CineShot

- 运行时/编辑器：`CineShot/`（`NonsensicalKit.CineShot.Core`、`NonsensicalKit.CineShot.Output`、`NonsensicalKit.CineShot.Editor` 等）
- 主模板：`CineShot/Templates/Shots`；镜头库新建写到宿主 `Assets/Resources/CineShot/Templates/`
- 演示不在包内，宿主工程见 `Assets/NonsensicalSamples/NonsensicalKit.Simulation/CineDemo`
- 宿主需自备：Cinemachine（2.8 或 3.x）、Timeline（包已依赖）；URP 仅景深需要

## Frame（DaTang）

- 运行时/工具：`Frame/`（`Frame.Core`、`Frame.BizKit.WMS` 等 asmdef）
- 示例：Package Manager → **NonsensicalKit.Simulation** → Samples → **Frame Sample**（`Samples~/FrameSample`）
- 宿主需自备：Cinemachine、DOTween、UniTask、TextMeshPro 等；`Resources/Services`（MQTTManager、DigitalTwinUIManager）由项目提供，不在包内
- ThirdParty：`Frame/ThirdParty` 含 XCharts、ZCalendar（随 Frame 进包）
