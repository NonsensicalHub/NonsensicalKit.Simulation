# ScriptAnimation

兼容 Timeline 的脚本动画模块（PlayableDirector 驱动）。
用于数字孪生场景中的路网移动、叉车取放货、堆垛机货位双轴运动、六轴机械臂点到点 IK；可与官方 Cinemachine Track 同 Timeline 对齐。

命名空间：NonsensicalKit.ScriptAnimation（Runtime）/ .Editor / .Demo
主轨：ScriptMovementTrack（移动/车体）、ScriptDedicatedTrack（机械臂 / 特效 / 世界旋转锁定等专用），均继承 ScriptAnimTrackBase。
机械臂、Fade/Blink、OpenBox、FilmWrap、WorldRotationLock 等均挂在 ScriptDedicatedTrack 上（业务上一条轨实例只用一种 Clip）。
程序集：NonsensicalKit.ScriptAnimation.Runtime / .Editor / .Demo（Demo 不 autoReferenced，不进入产品程序集）


依赖
----
必需：
  - Unity Timeline（com.unity.timeline）
  - NonsensicalKit.Core.Runtime（Int4 等）
  - NonsensicalKit.DigitalTwin.Warehouse（WarehouseManager / 货位解析；无仓库时可用 CellPositionTable）

可选：
  - Cinemachine（镜头轨，本模块不封装）

宿主工程当前为 Unity 6（6000.x）。模块未单独声明最低版本。

包内路径
--------
模块：`Packages/com.nonsensicallab.nonsensicalkit.simulation/ScriptAnimation/`
示例：`Samples~/ScriptAnimation/`（Package Manager → Samples 导入）
文档：`Documentation~/ScriptAnimation/`


目录
----
Runtime/
  Core/       ScriptAnimActor / PathMoveActor / ScriptAnimPoint / AxisMotionProfile / 时长协议
  Path/       PathNetwork + PathNode + PathQuery
  Forklift/   （已并入 Actors/ForkliftAnim）
  LatentAgv/  （已并入 Actors/LatentAgvAnim）
  Actors/     ForkliftAnim / LatentAgvAnim / CtuAnim / ShuttleAnim 等
  Stacker/    StackerAnim / CellPositionTable / WarehouseCellResolver
  RobotArm/   RobotArmAnim / RobotArmIk（五轴见 RobotArm5）
  OpenBox/    OpenBoxAnim / FoldableCarton / CartonRigConfig（生成折痕节点树）
  FilmWrap/   FilmWrapAnim + FilmWrap.shader；缠膜机路径/喷嘴/膜带（WrapPath 等）
  Timeline/
    ScriptAnim/   ScriptAnimTrackBase + ScriptMovementTrack / ScriptDedicatedTrack
    PathMove/     PathMoveClip
    Teleport/     TeleportClip
    Forklift/     ForkliftClip
    LatentAgv/    LatentAgvClip
    Ctu/          CtuClip
    Shuttle/      ShuttleClip
    Stacker/      StackerClip / StackerForkClip
    RobotArm/     RobotArmClip
    OpenBox/      OpenBoxClip
    FilmWrap/     FilmWrapClip
Editor/       路网按钮、Clip 时长同步、Clip/Track 自定义 Inspector、Demo 菜单、CartonRigConfig 手柄
Samples~/ScriptAnimation/  独立程序集 NonsensicalKit.ScriptAnimation.Demo（Package Manager 导入）
  Materials/  统一 Demo 场景生成的材质与网格
  Scripts/    机械臂 Rig 演示驱动（非 Timeline）
  OpenBox/、FilmWrapping/  独立演示场景与脚本


快速用法
--------
1. 场景挂 PathNetwork，子物体挂 PathNode → Inspector「收集子节点 / 自动链接邻近节点」
2. 取放货（叉车/CTU）/ 直线移动 / 机械臂途经点：空物体挂 ScriptAnimPoint（Scene 中可见橙色 Gizmo）；潜伏车取放不配货点
3. 挂组件：
     纯路径移动 → PathMoveActor
     叉车       → ForkliftAnim（指定 Fork）
     潜伏车     → LatentAgvAnim（指定 Platform）
     堆垛机     → StackerAnim（指定 WarehouseManager 或 CellPositionTable；可选 Travel/Lift/Fork）
     机械臂     → RobotArmAnim（六轴；轴点父子链配置，连杆由子轴点自动测量）
     开箱       → OpenBoxAnim / FoldableCarton（可用 CartonRigConfig 生成折痕树）
     缠膜       → FilmWrapAnim（挂到已有膜模型根节点；圈数/螺距在组件上配）
       缠膜机演示 → Samples~/ScriptAnimation/FilmWrapping（WrapPath + 喷嘴 + 膜带网格，独立场景）
4. 新建 Timeline，添加 ScriptMovementTrack / ScriptDedicatedTrack，绑定上述组件
5. 在轨上添加 Clip，配置参数；Track Inspector 用「按速度刷新全轨时长」
6. 镜头用官方 Cinemachine Track，与脚本轨同 Timeline 对齐

菜单：
  Tools → ScriptAnimation → 创建或打开 Demo 场景
  Tools → ScriptAnimation → 打开 OpenBox 演示场景
  Tools → ScriptAnimation → 打开缠膜机演示场景


组件 ↔ Clip 绑定
----------------
Clip              需要绑定的组件
-----------------------------------------------
PathMoveClip      PathMoveActor（或 ForkliftAnim / LatentAgvAnim / CtuAnim / ShuttleAnim）
BezierCornerClip  PathMoveActor（或派生车体）
ReverseUTurnClip  PathMoveActor（或派生车体）
ThreePointTurnClip PathMoveActor（或派生车体）
RotateClip        PathMoveActor（或派生车体）
TeleportClip      PathMoveActor（或派生车体）
ForkliftClip      ForkliftAnim
LatentAgvClip     LatentAgvAnim
CtuClip           CtuAnim
ShuttleClip       ShuttleAnim
StackerClip       StackerAnim
StackerForkClip   StackerAnim（且需指定 Fork）
RobotArmClip      RobotArmAnim（ScriptDedicatedTrack）
RobotArm5Clip     RobotArm5Anim（ScriptDedicatedTrack）
OpenBoxClip       OpenBoxAnim（含 FoldableCarton）
FilmWrapClip      FilmWrapAnim
WorldRotationLockClip WorldRotationLockAnim（另建 ScriptDedicatedTrack）
Fade / Blink 等   对应 *Anim（ScriptDedicatedTrack）

绑错类型会打 Warning，物体不会动。
若场景曾用 ForkliftAnim + MotionStyle=举升 AGV 跑潜伏时序：请改绑 LatentAgvAnim 并换用 LatentAgvClip。


Clip 能力摘要
-------------
PathMove
  - ExposedReference：Network / StartNode / EndNode
  - 沿路网 A* 折线移动；可覆盖移动/旋转速度、DestinationOffset；Actor 可设 PathOffset
  - 移动类型：边走边转 / 先转后移 / 只移不转（侧移等，朝向保持进入 Clip 时的值）
  - 特种机动（同轨混排）：RotateClip / ThreePointTurnClip / BezierCornerClip / ReverseUTurnClip
  - PathNode「经过时停留」：勾选后经过该节点原地停留（默认 1s，模拟顶升移栽），时长自动计入 Clip

Teleport
  - ExposedReference：TargetNode
  - 瞬移到节点（+ DestinationOffset）；FacingMode：FaceNode / KeepPrevious / CustomYaw
  - 默认占位 10 帧，可自由拖拽时长；仅首帧写入位姿（便于循环复用）
  - AutoSyncDuration 默认关闭，避免刷新全轨时长时被重置

Forklift
  - ExposedReference：Station（ScriptAnimPoint 货点）
  - 取放流程：旋转 → 开始行驶高度调货叉 → 前进 → 动作货叉 → 后退 → 货叉到结束行驶高度
  - 四个高度：开始行驶 / 结束行驶 / 放货插入 / 载货抬起
  - Mode：PickUp / PutDown；ReverseFacing：倒车取放（车头背对货点，与 PathMove 同语义）
  - 货物显隐请另用 Activation 等轨

LatentAgv（潜伏车 / 举升 AGV）
  - 无货点 / 接近距离：车体先由 PathMove 等到货下方，本 Clip 只原地升降平台
  - 时序：开始行驶高度 → 动作高度（取 Place→Lift / 放 Lift→Place）→ 结束行驶高度
  - 四个高度：开始行驶 / 结束行驶 / 放货插入 / 载货抬起
  - Mode：PickUp / PutDown；货物显隐请另用 Activation 等轨

DirectMove
  - ExposedReference：StartNode / EndNode（ScriptAnimPoint；也可改用世界坐标）
  - A→B 直线移动，不寻路、不改朝向

Stacker
  - 无场景 ExposedReference；起终点为 Int4 货位坐标（层/列/排/深）
  - 行走（XZ）与升降（Y）同时梯形加减速，总时长 = max(行走, 升降)
  - 有 WarehouseManager：经 GetRuntimeBinData 取世界位置
  - 无仓库：用场景上的 CellPositionTable（坐标 → 世界位置）

StackerFork
  - 机身不动，仅货叉沿本地轴运动；通常接在 StackerClip 到位之后

RobotArm
  - 定死六轴（J1–J6）：在 RobotArmAnim 上配置关节 Transform、旋转轴、连杆、限位、角速度
  - Clip：点位链表（ExposedReference ScriptAnimPoint 优先，或世界坐标）；可选「指定第六轴角」
  - 路径始终：默认姿态（Home）→ 点位… → 默认姿态（Home）；点位均为途经/作业目标
  - 「末端保持向下」时：位置 IK + 第 5 轴朝下；第 6 轴按点位 J6 目标角插值（未勾选则继承前序）
  - 关闭朝下时：对齐各点朝向；点位 J6 字段不强制覆盖腕部
  - 建议：配好关节后「测量连杆」「捕获 Rest / Home」
  - 可选指定工具 Tip；末轴无下一关节时按 Tip 测最后一节连杆

FilmWrap
  - 用专用 Shader 按分层螺旋上升裁切已有膜模型（每一层先绕 360°，再升高），不改原材质球资源
  - 组件上配置：轴向、顺时针、起始角、自下而上、圈数或每圈上升高度（米）、软边
  - Clip：FromProgress → ToProgress（0=未缠，1=缠满）；可拖拽时长
  - 机器喷嘴/膜带：Runtime 另有 WrapPath / WrapNozzleDriver / WrapRibbonMesher / WrapProgressController；完整演示见 Samples~/ScriptAnimation/FilmWrapping


已知限制与注意
--------------
1. 路网 / 节点 / 货点等场景引用必须用 Clip 上的 Exposed Reference（由 PlayableDirector 解析）。
   货点、直线端点、机械臂途经点请挂 ScriptAnimPoint；路网节点仍用 PathNode。
   不要把场景物体直接序列化进 Timeline 资产，否则运行时引用丢失、物体不动。

2. 同一 ScriptMovementTrack / ScriptDedicatedTrack 内同帧只处理权重最高的 Clip（Ease 交叠时按权重交接，不做位姿混合）。
   Clip 未声明 Blending；勿依赖 Crossfade 插值。移动与特效可并行多轨。

3. PathNetwork 寻路距离按 XZ 平面（忽略 Y）。

4. Stacker 行走轴与升降轴不要配成同一世界分量，否则会互相覆盖（OnValidate 会警告）。

5. 无 WarehouseManager 且无 CellPositionTable 时，Stacker 无法解析货位。

6. RobotArm 连杆长度应与模型层级一致，否则 IK 末端与视觉尖端会有偏差。
   目标超出工作空间时仍会输出最近解，动作可能「够不着」。

7. WorldRotationLock：另建 ScriptDedicatedTrack 绑定 WorldRotationLockAnim（可挂任意物体，不必挂在车上）；
   Target 指向要锁的节点。多车时各锁各的 Target；移动 Mixer 仅对「Target 属于本车层级」的活跃 Lock 在位移后再写一次。

8. 车体 / 堆垛机 / 机械臂请先捕获 Home；无前序且未指定起点时用 Home，未配置会 Warning 并回退原点/零角。


Demo 自检
---------
菜单：Tools → ScriptAnimation → 创建或打开 Demo 场景
生成：Assets/ScriptAnimationDemo/ScriptAnimationDemo.unity（包内 Sample 只读；菜单写入工程 Assets）
（统一场景；旧 RobotArmRigDemo / RobotArm5RigDemo 已并入，勿再建独立场景）

打开场景后播放 Timeline / Play Mode，应看到：
  - Forklift 轨：路网移动 + 取放货 + Teleport 回起点（循环）
  - ScissorLift 轨：剪叉举升取放
  - Stacker 轨：货位双轴移动（可并行）
  - RobotArm 轨：六轴多点位巡航 IK
  - RobotArm5 轨：五轴多点位巡航 IK
  - FilmWrap 轨：货物外包膜螺旋上升缠满（约 6 秒）
  - OpenBox 轨：侧壁成型 → 合底 → 合顶
  - LatentAgv 轨：Rotate / DirectMove / PathMove / BezierCorner /
    ThreePointTurn / ReverseUTurn / 平台升降 / Teleport

独立演示场景（先导入 Sample「ScriptAnimation」，再用菜单打开）：
  OpenBox/OpenBox.unity — 纸箱折叠交互
  FilmWrapping/FilmWrapping.unity — 缠膜机喷嘴 + 膜带

若物体不动：先查 Track 绑定类型、Exposed Reference 是否已解析、Console 是否有 [ScriptAnim]/[Forklift]/[LatentAgv]/[Stacker]/[RobotArm] Warning。
