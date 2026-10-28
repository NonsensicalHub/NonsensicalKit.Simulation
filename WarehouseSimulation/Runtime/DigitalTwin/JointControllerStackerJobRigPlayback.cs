using System;
using NaughtyAttributes;
using NonsensicalKit.DigitalTwin;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>
    /// 堆垛机回放：通过 <see cref="StackerTransformDriver"/> 直接驱动三轴 Transform。
    /// 轴目标由货位世界坐标对齐得到，不再经 JointController / InitialValue / AxisScale。
    /// 调度与 Pick/Place 货叉时序不变。
    /// </summary>
    public sealed partial class JointControllerStackerJobRigPlayback : MonoBehaviour
    {
        [Label("堆垛机编号")]
        [SerializeField]
        private int m_stackerId;

        [Label("巷道左列（≤0 则用绑定/地图推导）")]
        [SerializeField]
        private int m_aisleLeftColumn;

        [Label("列向伸叉覆盖")]
        [SerializeField]
        private SimStackerColumnReach m_columnReach;

        [Header("运动学")]
        [Label("运动学")]
        [SerializeField]
        private StackerKinematicsConfig m_kinematics = new();

        [Header("驱动")]
        [Label("三轴 Transform 驱动")]
        [Tooltip("挂 StackerTransformDriver；可同物体。优先显式三轴引用，空则从下方旧 JointController 自动取节点。")]
        [SerializeField]
        private StackerTransformDriver m_transformDriver;

        [Label("旧 JointController（可选，仅迁移填节点）")]
        [Tooltip("回放不再调用 ChangeState。若驱动未配齐三轴，会把 Joints[0/1/2] 节点交给驱动。可逐步删掉。")]
        [SerializeField]
        private JointController m_jointController;

        [Label("料箱挂载点（可选）")]
        [SerializeField]
        private Transform m_cargoMount;

        private DefaultWarehouseSimulationBindingsAsset _bindings;
        private WarehouseGridConfig _grid;
        private SimSlotExclusionZone[] _exclusionZones = Array.Empty<SimSlotExclusionZone>();
        private WarehouseManagerBinPositionIndex _positionIndex;
        private int _pickupLevel;
        private ConveyorMapTopology _topology;
        private SimPlaybackCargoVisualRegistry _cargoRegistry;

        private SimStackerDefinition _definition;
        private int _activeAisleLeftColumn;
        private int _railColumn;
        private int _storageColumn;
        private int _level;
        private int _row;
        private bool _rigInitialized;
        private bool _loggedEvaluateSkip;
        private bool _loggedMotionResolveSkip;
        /// <summary>已废弃：收叉在 Pick/Place 段内完成，不再靠 Move 冒充。字段仅清零兼容。</summary>
        private float _heldForkRaw;
        private bool _hasHeldFork;
        private double _logSimTime = double.NaN;
        private int _placeDiagJobId = int.MinValue;
        private bool _placeDiagStarted;
        private bool _placeDiagCommitted;
        private bool _loggedMountError;
        private bool _loggedKinematicsMismatch;
        private IStackerKinematics _playbackKinematics;
        private bool _chainReady;
        private float _homeRow;
        private float _homeLevel;
        /// <summary>
        /// 当前轨道巷道中心的货叉仓库坐标。
        /// 货叉关节只写相对此中心的伸出量；0=模型静止位（收回），不能改成相对静止位绝对坐标，否则整段列向会灌进货叉轴导致乱飞。
        /// </summary>
        private float _centerFork;
        private int _signRow = 1;
        private int _signLevel = 1;
        private int _signFork = 1;
        private float _scaleRow = 1f;
        private float _scaleLevel = 1f;
        private float _scaleFork = 1f;

        public int StackerId => m_stackerId;

        private IStackerKinematics Kinematics => _playbackKinematics ?? m_kinematics;

        private SimStackerDefinition ActiveDefinition
        {
            get
            {
                var def = _definition;
                def.AisleLeftColumn = _activeAisleLeftColumn;
                return def;
            }
        }

        public void Configure(
            DefaultWarehouseSimulationBindingsAsset bindings,
            WarehouseSimPlaybackController playback,
            WarehouseManagerBinPositionIndex positionIndex,
            int pickupLevel,
            ConveyorMapTopology topology,
            SimPlaybackCargoVisualRegistry cargoRegistry,
            WarehouseGridConfig grid = null,
            int warehouseId = 0)
        {
            _bindings = bindings;
            _grid = grid;
            _exclusionZones = grid != null
                ? SlotGridUtility.BuildEffectiveExclusionZones(grid, bindings, warehouseId, restrictToWarehouse: true)
                : Array.Empty<SimSlotExclusionZone>();
            // 姿态由子任务 progress 直接落到关节上，不再读取播放倍速。
            _ = playback;
            _positionIndex = positionIndex;
            _pickupLevel = pickupLevel;
            _topology = topology;
            _cargoRegistry = cargoRegistry;
            _playbackKinematics = StackerKinematicsResolver.Resolve(bindings, topology, m_stackerId);
            WarnIfKinematicsDiverge();
        }

        public bool TryInitialize()
        {
            if (!EnsureDriver())
            {
                return false;
            }

            if (_positionIndex == null || !_positionIndex.IsReady && !_positionIndex.TryLoad())
            {
                return false;
            }

            if (!StackerColumnReachUtility.TryGetDefinition(
                    _bindings, _topology, m_stackerId, out _definition))
            {
                _definition = new SimStackerDefinition
                {
                    StackerId = m_stackerId,
                    AisleLeftColumn = m_aisleLeftColumn,
                    ColumnReach = m_columnReach,
                };
            }

            if (m_aisleLeftColumn > 0)
            {
                _definition.AisleLeftColumn = m_aisleLeftColumn;
            }

            if (m_columnReach == SimStackerColumnReach.OneColumn
                || m_columnReach == SimStackerColumnReach.TwoColumns
                || m_columnReach == SimStackerColumnReach.FourColumns)
            {
                _definition.ColumnReach = m_columnReach;
            }

            _activeAisleLeftColumn = _definition.AisleLeftColumn;
            _railColumn = _activeAisleLeftColumn;
            _storageColumn = _railColumn;
            _level = 0;
            _row = 0;
            _rigInitialized = true;
            _loggedEvaluateSkip = false;
            _loggedMotionResolveSkip = false;
            _loggedMountError = false;
            _chainReady = false;
            WarnIfAlignReferenceDetached();
            ResetMotionTracking();
            SnapRetractedCarriage(0, 0);
            return true;
        }

        public void Evaluate(in StackerJobPlaybackContext context)
        {
            if (!EnsureReadyForEvaluate())
            {
                return;
            }

            if (!context.HasActiveSubTask)
            {
                return;
            }

            _logSimTime = context.SimTime;
            SyncPickupSideFromJob(in context.Task);
            SyncRailFromSubTask(in context.ActiveSubTask);

            var progress = context.PhaseProgress;
            var subTask = context.ActiveSubTask;

            switch (context.Phase)
            {
                case StackerJobMacroPhase.Waiting:
                    if (subTask.HasStackerPose)
                    {
                        ApplySubTaskPose(in subTask, subTask.StackerToSlot);
                    }

                    ReconcileCargoBeforePick(in context.Task);
                    break;

                case StackerJobMacroPhase.Approaching:
                case StackerJobMacroPhase.Picking:
                case StackerJobMacroPhase.Moving:
                case StackerJobMacroPhase.Placing:
                    ApplySubTaskMotion(in subTask, in context.Task, progress);
                    break;
            }
        }

        public void RestoreIdle(double simTime, in StackerJobPlaybackTask lastCompletedTask, bool hasLastTask)
        {
            _logSimTime = simTime;
            StopMotion();
            ResetMotionTracking();
            if (!hasLastTask)
            {
                SnapToHomePose();
                return;
            }

            SyncPickupSideFromJob(in lastCompletedTask);
            if (lastCompletedTask.HasIdleStackerSlot)
            {
                var idle = lastCompletedTask.IdleStackerSlot;
                if (_railColumn != idle.Column)
                {
                    _activeAisleLeftColumn = idle.Column;
                    _railColumn = idle.Column;
                    CalibrateJointInitialValues();
                }

                SnapRetractedCarriage(idle.Level, idle.Row);
            }
            else if (lastCompletedTask.IsOutbound)
            {
                SnapRetractedPickup(in lastCompletedTask);
            }
            else
            {
                SnapRetractedCarriage(
                    lastCompletedTask.TargetSlot.Level,
                    lastCompletedTask.TargetSlot.Row);
            }

            if (!lastCompletedTask.IsOutbound && IsStorageSlot(lastCompletedTask.TargetSlot))
            {
                // 已经停在货叉放下的位置就不要再按货位号重摆，避免列号落到邻巷。
                if (_cargoRegistry == null || !_cargoRegistry.IsJobParked(lastCompletedTask.JobId))
                {
                    PlaceCargoAtSlotByJob(lastCompletedTask.JobId, lastCompletedTask.TargetSlot);
                    SimSubTask noPose = default;
                    LogPlaceCargoCommitted(
                        "空闲补放",
                        in lastCompletedTask,
                        lastCompletedTask.TargetSlot,
                        in noPose,
                        hasPose: false);
                }
            }
            else if (lastCompletedTask.ReleaseCargoOnIdle)
            {
                ReleaseCargoByJob(lastCompletedTask.JobId);
            }
        }

        public void StopMotion()
        {
            if (m_transformDriver != null)
            {
                m_transformDriver.StopMotion();
            }
        }

        private bool EnsureDriver()
        {
            if (m_transformDriver == null)
            {
                m_transformDriver = GetComponent<StackerTransformDriver>();
            }

            // 场景未预先挂驱动时运行时补一个，减少手工配置；节点仍优先显式引用 / 旧 JC fallback。
            if (m_transformDriver == null)
            {
                m_transformDriver = gameObject.AddComponent<StackerTransformDriver>();
            }

            if (m_jointController != null)
            {
                m_transformDriver.SetLegacyJointController(m_jointController);
            }
            else
            {
                m_transformDriver.EnsureNodesFromLegacy();
            }

            return m_transformDriver.IsReady;
        }

        private bool EnsureReadyForEvaluate()
        {
            if (_bindings == null || _positionIndex == null)
            {
                LogEvaluateSkipOnce("依赖未注入（需由 StackerPlaybackEventHandler 调用 Configure）。");
                return false;
            }

            if (!_rigInitialized && !TryInitialize())
            {
                LogEvaluateSkipOnce("TryInitialize 失败（检查 StackerTransformDriver 三轴是否就绪）。");
                return false;
            }

            if (!EnsureDriver())
            {
                LogEvaluateSkipOnce("StackerTransformDriver 未就绪。");
                return false;
            }

            return true;
        }

        private void LogEvaluateSkipOnce(string reason)
        {
            if (_loggedEvaluateSkip)
            {
                return;
            }

            _loggedEvaluateSkip = true;
            SimPlaybackLog.Warn($"JointStackerRig 堆垛机 {m_stackerId} {reason}", this);
        }

        private void WarnIfAlignReferenceDetached()
        {
            if (m_transformDriver == null || m_transformDriver.ForkAxis == null)
            {
                return;
            }

            var row = m_transformDriver.RowAxis;
            var level = m_transformDriver.LevelAxis;
            var fork = m_transformDriver.ForkAxis;
            // 预想：走行 → 升降 → 货叉。货叉不是层向的子节点时，升降不会带动货叉，看起来就像自己飞。
            if (level != null && fork != null && fork != level && !fork.IsChildOf(level))
            {
                SimPlaybackLog.Warn(
                    $"JointStackerRig 堆垛机 {m_stackerId} 货叉轴不在层向轴下（当前父={fork.parent?.name ?? "null"}），" +
                    "升降时货叉不会跟着走。",
                    this);
            }
            else if (row != null && level != null && level != row && !level.IsChildOf(row))
            {
                SimPlaybackLog.Warn(
                    $"JointStackerRig 堆垛机 {m_stackerId} 层向轴不在走行轴下（当前父={level.parent?.name ?? "null"}），" +
                    "走行时升降/货叉不会跟着走。",
                    this);
            }

            var align = m_transformDriver.AlignReference;
            if (align == fork || align.IsChildOf(fork))
            {
                return;
            }

            SimPlaybackLog.Warn(
                $"JointStackerRig 堆垛机 {m_stackerId} 的对齐参考不在货叉轴下，伸叉时本体可能和货叉脱节。",
                this);
        }

        private void WarnIfKinematicsDiverge()
        {
            if (_loggedKinematicsMismatch || _playbackKinematics == null || m_kinematics == null)
            {
                return;
            }

            if (NearlyEqual(_playbackKinematics.VerticalSpeedMetersPerSecond, m_kinematics.VerticalSpeedMetersPerSecond)
                && NearlyEqual(_playbackKinematics.VerticalSpeedLoadedMetersPerSecond, m_kinematics.VerticalSpeedLoadedMetersPerSecond)
                && NearlyEqual(_playbackKinematics.DepthSpeedMetersPerSecond, m_kinematics.DepthSpeedMetersPerSecond)
                && NearlyEqual(_playbackKinematics.DepthSpeedLoadedMetersPerSecond, m_kinematics.DepthSpeedLoadedMetersPerSecond)
                && NearlyEqual(_playbackKinematics.VerticalAccelerationMetersPerSecondSquared, m_kinematics.VerticalAccelerationMetersPerSecondSquared)
                && NearlyEqual(_playbackKinematics.DepthAccelerationMetersPerSecondSquared, m_kinematics.DepthAccelerationMetersPerSecondSquared)
                && NearlyEqual(_playbackKinematics.PickSeconds, m_kinematics.PickSeconds)
                && NearlyEqual(_playbackKinematics.PlaceSeconds, m_kinematics.PlaceSeconds))
            {
                return;
            }

            _loggedKinematicsMismatch = true;
            SimPlaybackLog.Warn(
                $"JointStackerRig 堆垛机 {m_stackerId} 上的运动学与硬件绑定不一致。回放插值改用硬件绑定，避免和仿真时长各算一套。",
                this);
        }

        private static bool NearlyEqual(float a, float b) => Math.Abs(a - b) <= 0.001f;
    }
}
