using System;
using NaughtyAttributes;
using System.Collections.Generic;
using NonsensicalKit.DigitalTwin;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback.Tasks;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime.DigitalTwin
{
    /// <summary>
    /// 堆垛机回放：通过 <see cref="JointController"/> 驱动关节，关节目标值来自 WarehouseManager 货位坐标。
    /// JointController 按数组顺序配置三轴：0=层向、1=排向、2=货叉；轨道固定列，仅层/排参与运动时长。
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

        [Header("关节")]
        [Label("关节控制器")]
        [SerializeField]
        private JointController m_jointController;

        [Label("料箱挂载点（可选）")]
        [SerializeField]
        private Transform m_cargoMount;

        private DefaultWarehouseSimulationBindingsAsset _bindings;
        private WarehouseGridConfig _grid;
        private SimSlotExclusionZone[] _exclusionZones = Array.Empty<SimSlotExclusionZone>();
        private WarehouseSimPlaybackController _playback;
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
        private float[] _jointScratch;
        private int _lastMotionSubTaskId = -1;
        private double _lastMotionSimTime = -1d;

        public int StackerId => m_stackerId;

        private IStackerKinematics Kinematics => m_kinematics;

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
            WarehouseGridConfig grid = null)
        {
            _bindings = bindings;
            _grid = grid;
            _exclusionZones = grid != null
                ? SlotGridUtility.BuildEffectiveExclusionZones(grid, bindings)
                : Array.Empty<SimSlotExclusionZone>();
            _playback = playback;
            _positionIndex = positionIndex;
            _pickupLevel = pickupLevel;
            _topology = topology;
            _cargoRegistry = cargoRegistry;
        }

        public bool TryInitialize()
        {
            if (m_jointController == null || m_jointController.Joints == null || m_jointController.Joints.Length == 0)
            {
                return false;
            }

            if (!StackerJointAxisMap.IsValid(m_jointController))
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
            _jointScratch = new float[m_jointController.Joints.Length];
            CalibrateJointInitialValues();
            _rigInitialized = true;
            _loggedEvaluateSkip = false;
            _loggedMotionResolveSkip = false;
            ResetMotionTracking();
            SnapToGrid(new GridIndex(0, _railColumn, 0));
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

            SyncPickupSideFromJob(in context.Task);
            SyncRailFromSubTask(in context.ActiveSubTask);

            var snapInstant = _playback == null || !_playback.IsPlaying;
            var progress = context.PhaseProgress;
            var subTask = context.ActiveSubTask;

            switch (context.Phase)
            {
                case StackerJobMacroPhase.Waiting:
                    if (subTask.HasStackerPose)
                    {
                        ApplySubTaskPose(in subTask, subTask.StackerToSlot, snapInstant);
                    }

                    break;

                case StackerJobMacroPhase.Approaching:
                case StackerJobMacroPhase.Picking:
                case StackerJobMacroPhase.Moving:
                case StackerJobMacroPhase.Placing:
                    ApplySubTaskMotion(in subTask, in context.Task, progress, context.SimTime, snapInstant);
                    break;
            }
        }

        public void RestoreIdle(double simTime, in StackerJobPlaybackTask lastCompletedTask, bool hasLastTask)
        {
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
                SnapPartial(lastCompletedTask.IdleStackerSlot);
                ReleaseCargoByJob(lastCompletedTask.JobId);
                return;
            }

            if (lastCompletedTask.IsOutbound)
            {
                SnapToPickupForJob(in lastCompletedTask);
                ReleaseCargoByJob(lastCompletedTask.JobId);
                return;
            }

            SnapToSlot(lastCompletedTask.TargetSlot);
            ReleaseCargoByJob(lastCompletedTask.JobId);
        }

        public void StopMotion()
        {
            if (m_jointController != null)
            {
                var current = m_jointController.GetJointsValue();
                m_jointController.ChangeState(new ActionData(current, 0f));
            }
        }

        private bool EnsureReadyForEvaluate()
        {
            if (_bindings == null || _positionIndex == null || m_jointController == null)
            {
                LogEvaluateSkipOnce("依赖未注入（需由 StackerPlaybackEventHandler 调用 Configure）。");
                return false;
            }

            if (!_rigInitialized && !TryInitialize())
            {
                LogEvaluateSkipOnce("TryInitialize 失败。");
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
    }
}
