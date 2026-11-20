# CineShot — Agent 实现文档

> 给 Cursor Agent 读：实现结构、约束、易踩坑。改 `Packages/com.nonsensicallab.nonsensicalkit.simulation/CineShot/**` 或 `Assets/NonsensicalSamples/NonsensicalKit.Simulation/CineDemo/**` 前后都要读、要更新本文。
> 给人看的用法在同目录 `使用说明.md`（字段含义、操作步骤都在那边，这里不重复）。桌面上的方案原稿不是实现真相，不要改它。

最后维护：2026-11-19（CineDemo 迁到 NonsensicalSamples）

---

## 1. 工程约束（长期有效）

- **C# 9 only**：不用 file-scoped namespace、record、global using、`new()` 目标类型推断以外的新语法。目标兼容 Unity 2021.3 / CM 2.8。
- Editor 里 `FindObjectsByType` / `FindFirstObjectByType` 必须包 `#if UNITY_2023_1_OR_NEWER`，否则用 `FindObjectsOfType` / `FindObjectOfType`。
- Cinemachine 分支：`CM_V3_OR_NEWER`（命名空间 `Unity.Cinemachine`）/ `CM_2_8_OR_NEWER`（`Cinemachine`）。只允许出现在 `Runtime/Output` 和 `Runtime/Compat`。
- URP 只允许出现在 `Runtime/Output.URP`（`CINE_URP_PRESENT`）。
- **禁止** `CinemachineBrain.ManualUpdate`。编辑预览靠直接摆 `Camera.main`（`PushGameCamera`）。
- 操作 Unity 只用 MCP 的 `unity_*`，不直打 HTTP bridge。`unity_execute_code` 里写 `UnityEngine.Object`（`Object` 有歧义）；播放模式会挡脚本重载。
- 未要求不 commit；临时截图/日志同轮删除；无关脏文件（其它包、URP 设置、子模块）不要 stage。
- 新建 Canvas：Scale With Screen Size，1920×1080，Match 0.618。

---

## 2. 程序集与依赖方向

```
NonsensicalKit.CineShot.Compat      能力探测（CM 版本、URP 景深是否可写）、WarnOnce
     ↑
NonsensicalKit.CineShot.Core        数据、求解器、Playables、Rig、Player、焦点求解；只定义输出接口
     ↑                        ↑
NonsensicalKit.CineShot.Output       NonsensicalKit.CineShot.Output.URP     接口实现，启动时注册到 CineOutputFactory
     ↑
NonsensicalKit.CineShot.Editor      检查器、窗口、Scene 预览、分镜铺轨（引用 Core/Output/Compat，不引用演示）
CineShot.Samples                    宿主 `Assets/NonsensicalSamples/NonsensicalKit.Simulation/CineDemo` 的演示脚本（引用 Core / Compat / Output 程序集名），模块不反向引用它；演示不进包
```

C# 命名空间仍是 `CineShot.*`（`rootNamespace` 未改）。宿主 `CineShot.Samples` 程序集名不变，避免改演示场景里的序列化类型名。

Core 不认识 Cinemachine 和 URP：`CineOutputFactory.CreateCameraOutput / CreateFocusOutput` 两个委托由 `OutputBootstrap`（编辑器 InitializeOnLoad + 运行时 SubsystemRegistration）和 `UrpBootstrap`（仅运行时）赋值。新增输出后端照这个模式注册，不要让 Core 引用后端。

---

## 3. 文件地图

| 路径 | 内容 |
|---|---|
| `Runtime/Core/Data/ShotAsset.cs` | `ShotAsset` 模板、`ShotOverride`（片段覆写 + `ApplyTo` / `Resolve*`） |
| `Runtime/Core/Data/RigParams.cs` | `RigParams` 通用槽位与各机位默认值、`TargetBinding`（仅两个偏移）、`HandheldLayer`、`MatchPoint` |
| `Runtime/Core/Data/RigKind.cs` | `RigKind`、`FocusMode`、`ShotBlendMode`、`ShotOverrideMask` |
| `Runtime/Core/Data/FocusSettings.cs` | `DofMode`、`FocusSettings`（含 `HyperfocalDistance`、`Sanitize`） |
| `Runtime/Core/Data/EasingAsset.cs` / `CineSpline.cs` / `CineSequenceAsset.cs` | 缓动 LUT、弧长样条、分镜行 |
| `Runtime/Core/Solvers/*` | `CameraPose`、`TargetSample`、`ShotContext`；九个静态求解类；`SolverDispatch` 表；`SolverMath` |
| `Runtime/Core/Playables/` | `CineShotTrack`、`CineShotClip`（含 `BuildContext`）、`CineShotBehaviour`（只持 Clip）、`CineMixerBehaviour`、`ShotBlend` |
| `Runtime/Core/Scene/CineShotRig.cs` | 目标采样（速度平滑）、`SubmitPose`（最小距离保护 + 焦点）、`BlendToGameCamera` |
| `Runtime/Core/Player/` | `CinePlayer` 状态机、`CineShotPlayer` 组件 |
| `Runtime/Core/Focus/` | `FocusSolver`（阻尼、限速、拉焦）、`IFocusOutput` |
| `Runtime/Core/Perf/CinePerfProbe.cs` | 混轨每帧耗时，256 帧滑动平均，演示 HUD 显示 |
| `Runtime/Core/OutputContracts.cs` / `CineOutputFactory.cs` | `ICameraOutput`（只有 `Apply` / `SetControlling`）与工厂 |
| `Runtime/Output/` | `AdapterCameraOutput` + `ILensWriter`（CM2/CM3 各一份） |
| `Runtime/Output.URP/` | `UrpFocusOutput`：隐藏全局 Volume（优先级 1000）写 DepthOfField |
| `Editor/Inspectors/CineShotRigEditor.cs` | `CineTips`（全部问号提示文案）、`CineHelp`、Rig/Player/Clip 检查器 |
| `Editor/Inspectors/CinePropertyDrawers.cs` | RigParams（按机位换中文名）、FocusSettings、HandheldLayer、MatchPoint、**ShotOverride 分区面板**、StoryboardRow |
| `Editor/Scene/CineShotSceneEditor.cs` | 编辑预览（与播放同一套求解/混合）、各机位手柄、`WriteRig`、`SaveClipToTemplate`、`RigLabel` |
| `Editor/Windows/` | 镜头配置窗、镜头库、分镜表、能力报告 + `CineMenus` |
| `Editor/Internal/ShotLibrary.cs` | 模板缓存、搜索、`FindById`、`ShotField`（分镜行的资产选择框）、复制变体 |
| `Editor/Timeline/ShotTrackLayer.cs` | 分镜表 → 轨道的增量铺轨 |
| `Templates/Shots` | 九种机位主模板（`图库_*`），随包提供只读图库 |
| `Assets/Resources/CineShot/Templates/Shots` | 镜头库新建 / 复制变体的可写目录（包路径不可写时回落到这里） |
| `Assets/Resources/CineShot/Templates/Sequences` | 分镜表新建默认目录 |
| `Tests/EditMode/SolverTests.cs` | 求解、混合、焦点、手持层单测；`Tests/PlayMode` 播放器状态机冒烟 |

演示在工程 `Assets/NonsensicalSamples/NonsensicalKit.Simulation/CineDemo`（不进包）：`Scenes/Demo_RigGallery.unity` 是全类型演示，`Scripts/CineDemoHUD.cs`、`Scripts/DemoHeroMotion.cs`。`CineShot.Samples` 按程序集名引用 `NonsensicalKit.CineShot.Core` 等，场景/时间轴按脚本 GUID 引用 `CineShotRig` / `CineShotPlayer` / `CineShotClip` / `CineShotTrack` / `ShotAsset`。其余 `Demo_5Shots / Demo_Perf_Baseline / Demo_MatchFrame / Demo_Compat_Smoke` 是早期场景，前两个的按钮绑着 HUD 的 `UiPlay/UiInterrupt/UiResume/UiStop`，删这些方法前先处理场景。

---

## 4. 每帧管线（必读）

```
CineMixerBehaviour.ProcessFrame（playerData = CineShotRig）
  1. 遍历 weight>0 的输入（weight 只判活跃，不做加权混合），最多 16 条
       ShotOverride.ApplyTo → 机位/构图/光学/焦点参数
       Rig.SampleLookAt / SampleFollow → TargetSample（带平滑速度）
       clip.BuildContext → SolverDispatch.Solve → 各片段独立位姿
  2. 选当前镜：ShotBlend.CompareActive（后开拍优先；开拍差 <1e-4s 比 priority）
     选上一镜：其余里次优的；没有则用 latch（当前片段切换时记下的上一帧最终位姿，按 Clip 比较）
  3. ShotBlend.Apply（进入阶段混合）→ ApplyMatch → 手持层 HandheldSolver.ApplyLayer
  4. 焦点：Blend 且有上一镜时 LerpSettings；两边都固定距离才插值距离，否则中点前用上一镜的焦点来源
  5. Rig.SubmitPose：最小距离保护（被推开才重新对准）→ ICameraOutput.Apply → FocusSolver → IFocusOutput.Apply
```

编辑预览（`CineShotSceneEditor`）复用同一套 `ApplyTo`、`BuildContext`、`SolverDispatch`、`ShotBlend`、`ApplyLayer`、`SubmitPose`。**改播放语义时必须同步预览**，否则 Scene 预览和播放不一致。

目标采样：`CineShotRig` 按 (目标实例, 偏移) 各占一个槽（注视、跟随各 8 槽）。同一帧重复采样复用速度；dt≈0 保留旧速度；速度上限 12 m/s，指数平滑 τ=0.12s。这是为了修"多条片段同帧采样导致速度跳变 → 前馈抖动"，不要改回共用一个槽。

---

## 5. 机位实现要点

求解器都是 `static class XxxSolver { static void Solve(in ShotContext, in RigParams, ref CameraPose) }`，由 `SolverDispatch.s_Table` 按 `RigKind` 下标索引（静态构造里断言长度）。**新增机位**：加枚举值（追加在末尾，序列化是整数）→ 写求解类 → 加入表 → `RigParams.DefaultFor` → `RigParamsDrawer.LabelsFor/Hint` → `CineShotSceneEditor.RigLabel` 和手柄 → `CineShotClipEditor.RigMark` → 模板 → 本文与使用说明。

- 瞄准统一走 `SolverMath.AimWithComposition`：先看目标 → 按构图偏移挪机位 → 再看一次。注视缺省回退跟随。
- 轨道 / 样条只认注视（无注视才用跟随），前馈只在目标速度 >0.5 m/s 时生效（`ShouldPredict`），避免同绑一物时两个偏移抢瞄。
- 手持机位不能复用 `StaticSolver`：Static 的 P0/P1 是偏航俯仰，手持里是振幅频率。
- 手持层只用 `HandheldSolver.ApplyLayer`（抖动 + 重新对准 + Dutch 叠加），不要调会重置基座的 `Solve`。
- 摇臂有跟随时 A/B 是相对偏移（历史上已经迁移过一次数据，不要再跑"减跟随位置"的迁移）。
- 样条行程不乘任何残留参数；`CineSpline.Evaluate` 是弧长参数化，每次按控制点哈希判断是否重建表。

---

## 6. 转场 `ShotBlend`

- 模式在**进入片段**上；混合发生在进入阶段。
- 窗口 W：重叠 >0.01s 用重叠时长；否则 `blendDuration`（≤0.01 回退 0.5）。
- 曲线：进入片段 `easeIn` → 上一镜 `easeOut` → 覆写/模板 Easing → smoothstep。
- **MatchFrame**：`matchHoldDuration`=H。`t<H` 贴住 `ResolveMatchPoint`；之后从锚点 `Lerp` 到本镜，窗口 `W-H`。`H>=W` 硬切本镜。匹配点可覆写（`ShotOverrideMask.MatchPoint`）。
- 检查器：Blend / MatchFrame 都显示 `blendDuration`；MatchFrame 另显示贴住时长与 `H>=W` 提示。
- 分镜行：`blendMode + blendDuration`，铺轨时写入片段（贴住时长在片段上配）。
- 编辑：选中且 `valid` 时 Scene 异色 frustum；「预览/修改」当次有效，推 `Camera.main`，切选中恢复。修改走 `WriteOverride`。

---

## 7. 焦点 / 景深

- 配置嵌在 `ShotAsset.focusSettings` / `ShotOverride.focusSettings`，没有独立的焦点资产，Rig 上也没有。
- 对焦距离来源由 `FocusMode` 决定，在 `CineShotRig.SubmitPose` 里生成 `FocusRequest`：固定距离 = hint；跟随目标 = 机位到瞄准点距离；超焦距 = `FocusSettings.HyperfocalDistance`（f²/(N·c)+f，焦距按毫米换米）。`SourceId` 变化触发拉焦。
- `DofMode.Off` → Volume DoF `active=false`。「焦点模式=无」≠ 关虚化。
- 散景写 focusDistance/aperture/focalLength/bladeCount；高斯写 gaussianStart=距离、End=距离+带宽（带宽参考光圈）。
- `UrpFocusOutput` 只在运行时注册（编辑模式不建隐藏 Volume，避免平时编辑画面被虚化）。

---

## 8. 播放器 `CinePlayer`

状态：Idle → Playing →（Interrupt）BlendingOut → PausedResumable →（Resume）Playing；Stop 回 Idle。

- `Play(shot, t)` 任何状态都可调，**总是从 t 开始并丢弃续播点**。续播只走 `Resume`。
- 输出默认取 `rig.Output`（Rig 重建适配器后仍有效）；带 `ICameraOutput` 的构造函数给测试用。
- `CineShotPlayer` 只创建一次 `CinePlayer`，外部订阅的事件不会因 `Play` 丢失。
- 交权混合由 `rig.BlendToGameCamera(k)` 逐帧插值到游戏虚拟相机位姿。

---

## 9. 编辑器约定

- 片段检查器：镜头资产下拉 → 片段覆写 → 注视/跟随 → 转场（含匹配贴住）→ 匹配预览/修改 → 样条/摇镜 → 分镜铺轨（折叠）。焦点只在覆写里改。
- 覆写面板：外框与每个分区都用 `GUI.Box(..., EditorStyles.helpBox)`，标题是普通 `EditorGUI.Foldout`（不要 foldoutHeader 底纹、不要编号），右侧 miniLabel 状态，按钮「覆写 / 模板」，开覆写时 Seed 拷模板值。三块：运镜 / 画面 / 其它（含匹配帧锚点）。改覆写默认不写模板。
- 说明文案集中在 `CineTips`，展示用字段旁问号（`CineHelp`）+ tooltip，状态用 miniLabel。不要堆 `MessageType.Info` 长 HelpBox。
- SessionState：`CineShot.FollowPlayhead`、`CineShot.WriteClip`（默认 true = 写片段覆写）、`CineShot.ManualTime`、`CineShot.Override.Fold.*`。
- 分镜行存 `shotGuid`（= `ShotAsset.Id`），界面用 `ShotLibrary.ShotField` 资产选择框，不要再暴露裸 GUID 文本框。
- 铺轨匹配键 `镜头Id#行号`，增量更新，不整轨清空。

---

## 10. 已删除，不要加回来

| 删掉的东西 | 原因 |
|---|---|
| `IFocusProvider` / `FocusProviders.cs` | 播放不走它，焦点在 `SubmitPose` 里直接算 |
| `PoseAccumulator` | 混轨不做加权姿态 |
| `CineFrameProbe` | 空的 Begin/End，测不到东西；耗时由混轨写 `CinePerfProbe` |
| `ShotThumbnailBaker` 与「重建缩略图」菜单/按钮 | 只渲染一个占位方块，且没有任何界面显示缩略图 |
| 能力报告的「生成并保存」与 `capability-report-*.md` | 往模块目录写生成文件；保留「复制诊断信息」 |
| `CineCapabilities` 的 Impulse / CM 轨道 / Brain / 镜头写入位 | 模块不依赖这些能力 |
| `Tools/Cine/校验镜头引用`、`重建缩略图` 菜单 | 前者只查空 Id（Id 自动生成）；片段级校验保留在轨道检查器 |
| `ICameraOutput.Bind/Priority/SupportsDutch/SupportsPhysicalLens/OutputTransform/IsControlling`、Rig 的 `m_FallbackCamera` | 没有调用方 |
| `IFocusOutputFactory`、`IFocusOutput.IsValid`、`FocusState.Velocity/Pulling` | 没有调用方 |
| `TargetBinding.mode/damping`、`TargetBindingMode` | 求解不读，界面上标着"预留" |
| `FocusSettings.focusLayerMask` | 不参与任何计算 |
| `ShotOverride.tags` | 标签只给镜头库搜索，片段级覆写无意义 |
| `ISolver` 接口与 `SolveGeneric`、`ShotContext.DeltaTime`、`ShotAsset.ApplyResolvedOptics`、`CinePlayer.AlignBeforeResume` | 没有调用方 |
| 镜头库「列表/网格」切换、分镜表「批量时长 2.5 秒」 | 网格没实现；批量按钮是写死的数值 |
| `CineTargetTag`、`CineBrainPreview`、CM2 worktree | 早期删除，不要重建 |
| 全时间轴所有片段 gizmos | 和播放头预览重复且糊，评估后不做 |

---

## 11. 演示场景注意事项（`Demo_RigGallery`）

- 时间轴 `Seq_RigGallery` 约 350 秒：图库 → 景深焦点 → 手持层/构图/缓动/优先级/匹配帧/跟随 → 「走动·*」九条（每条 9 秒，步距 9.4 秒）。
- `DemoHeroMotion`：只在显示名以「走动」开头的片段里沿 +Z 位移（每条从原点重新开始），并启用 Animator 播 `Hero_Locomotion` 的跑步；`applyRootMotion=false`。**不改旋转、不改镜头、不改注视点**。片段区间在 Awake 时缓存，运行中改时间轴需要重进播放。
- 注视点绑在 Hero 根节点，模板目标偏移 (0, 1.55, 0)。不要改成骨骼点，不要自行重算注视点或重绑。
- `CineDemoHUD`：点播按钮在 Start 时按片段生成并缓存区间、优先级；当前镜判定与混轨同规则（`ShotBlend.CompareActive`）；说明文案按片段显示名写在 `Describe`，改片段名要同步。
- 用户对演示改动的要求是"只做要求的那件事"，不要顺手改机位、注视、模型。

---

## 12. 变更记录（新在上）

### 2026-11-19 CineDemo 迁到 NonsensicalSamples

- 演示目录改为 `Assets/NonsensicalSamples/NonsensicalKit.Simulation/CineDemo`；`CineShot.Samples` 程序集名与脚本 GUID 不变
- 同步规则 glob、使用说明、包 README、Build Settings 场景路径

### 2026-11-19 程序集改名与用户模板路径

- 模块程序集改为 `NonsensicalKit.CineShot.*`（文件名同步）；C# 命名空间仍是 `CineShot.*`
- `CineShot.Samples` 名不变，只改 asmdef 引用；演示场景/HUD 未动
- 镜头库新建 / 变体 / 分镜新建写到 `Assets/Resources/CineShot/Templates/`

### 2026-11-19 迁入 NonsensicalKit.Simulation 包

- 模块根目录改为 `Packages/com.nonsensicallab.nonsensicalkit.simulation/CineShot`；`.meta` GUID 不变
- `Assets/CineDemo` 不迁；演示资源按脚本 GUID 引用
- 包内 `Templates/Shots` 只读；用户新建不写回 Packages

### 2026-11-17 匹配帧时长与覆写锚点

- `CineShotClip.matchHoldDuration`；`ShotOverride` / mask 增加匹配帧锚点；混轨与 Scene 预览共用新 `ApplyMatch`
- MatchFrame：先贴住 H 秒，再按剩余窗口从锚点混到本镜；`H>=W` 硬切并 Inspector 提示
- 选中 + valid：异色匹配 frustum；预览推相机 / 修改跟手；切选中恢复；默认写片段覆写
- 测试：贴住→过渡、超窗硬切、覆写 Resolve

### 2026-11-13 样条检查器布局

- `CineHelp.Prop` 按 `GetPropertyHeight` 预留完整高度（展开数组不再与下一字段重叠）；帮助图标钉在首行
- 样条检查器：张力提到控制点上方

### 2026-11-13 模块收尾验收

- 删除无效功能与死代码（见第 10 节清单）；`FocusProfile.cs` 更名 `FocusSettings.cs`（GUID 不变）
- 超焦距按公式计算，弥散圈参数真正生效（原来写死 ≥50 米）
- 求解器改静态类，`SolveStatic` → `Solve`；`CineShotClip.BuildContext` 统一三处 ShotContext 构造；`HandheldSolver.ApplyLayer` 统一混轨与预览的手持层
- 混轨 latch 按片段比较（同模板连续两条不再拿到过期位姿）；`CineShotRig.ReleasePose` 空方法删除
- `CineShotPlayer.Play` 不再重建状态机（修事件订阅丢失）；`CinePlayer.Play` 明确为"从 t 开始"
- 分镜行改为资产选择框；镜头库可按九种机位新建；铺轨报告与对话框中文化
- 演示 HUD / 角色位移不再每帧遍历时间轴；HUD 只在当前镜变化时改按钮颜色
- 新增测试：超焦距公式、手持层不丢目标。EditMode 13/13、PlayMode 1/1 通过
- 重写 `使用说明.md`（接入、组件、模板字段、九机位参数表、覆写、转场、焦点、手持层、工具、FAQ）与本文

### 2026-11-13 之前（同一开发周期，摘要）

- 走动章节：人物前向位移 + Robot Kyle 跑步动画；每条走动片段从起点出发；目标速度按槽位平滑，修轨道/环绕跟拍抖动
- 演示拆到 `Assets/CineDemo`；主模板集中到 `Templates/Shots`；HUD 加镜头说明与点播
- 景深/焦点演示：变浅变深、焦点近远、两镜间切换；叠化插值光圈与固定距离
- 覆写面板改为 helpBox 分区样式；手持振幅手柄
- 标签接入镜头库搜索；同刻叠片按 priority 决胜
- 手持：正弦主波 + Perlin，抖完重新对准；样条前馈门限；瞄准统一 `AimWithComposition`
- 跟随目标时动态写对焦距离；`DofMode.Off`；焦点配置嵌入模板与覆写
- 摇镜机位 `RigKind.Pan`；转场重叠优先、进入阶段混合；镜头配置窗可滚动

---

## 13. 收工检查

1. `unity_get_compilation_errors` 为 0
2. 改了求解/混合/焦点/播放器：跑 EditMode（`NonsensicalKit.CineShot.Tests.EditMode`），动了播放器再跑 PlayMode
3. 改了播放语义：确认 Scene 预览同步（第 4 节）
4. 更新第 12 节；字段或行为变了同步第 3–9 节和 `使用说明.md`
5. 删掉本轮的临时文件
