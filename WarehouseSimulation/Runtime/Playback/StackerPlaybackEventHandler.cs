using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Serialization;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>
    /// 堆垛机回放：将子任务时间轴聚合为宏观任务，驱动 JointController 堆垛机 Rig。
    /// </summary>
    public sealed class StackerPlaybackEventHandler : SimPlaybackEventHandlerBehaviour
    {
        [SerializeField, Label("硬件绑定")]
        private DefaultWarehouseSimulationBindingsAsset m_bindings;

        [SerializeField, Label("回放控制器（倍速）")]
        private WarehouseSimPlaybackController m_playback;

        [SerializeField, Label("货位坐标索引")]
        [Tooltip("仓库列表留空时，作为唯一的 0 号库。列表有内容时以列表为准。")]
        private WarehouseManagerBinPositionIndex m_positionIndex;

        [FormerlySerializedAs("m_additionalWarehouses")]
        [SerializeField, Label("仓库列表")]
        [Tooltip("每一项一座仓库，包含 0 号库。留空则使用上方的单个货位坐标索引。")]
        private WarehousePlaybackBinding[] m_warehouses;

        [Header("堆垛机机组")]
        [SerializeField, Label("堆垛机绑定")]
        private JointControllerStackerJobRigPlayback[] m_stackers;

        [SerializeField, Label("取货默认层（地图未标层时）")]
        private int m_pickupLevel;

        [Header("料箱（与输送线共用实例池）")]
        [SerializeField, Label("料箱实例池")]
        private SimPlaybackCargoVisualRegistry m_cargoRegistry;

        private readonly SimSubTaskPlaybackIndex _subTaskIndex = new();
        private readonly Dictionary<int, JointControllerStackerJobRigPlayback> _executors = new();
        private readonly List<JointControllerStackerJobRigPlayback> _executorList = new();
        private readonly Dictionary<int, IdleStamp> _idleStamps = new();
        private ConveyorMapTopology _topology;
        private IReadOnlyList<SimSubTask> _configuredTimeline;
        private double _lastEvaluateSimTime = double.NaN;
        private bool _loggedEvaluateSkip;
        private bool _rigsInitialized;
        private readonly HashSet<int> _missingPositionWarehouses = new();

        private struct IdleStamp
        {
            public bool HasLast;
            public int JobId;
            public double EndTime;

            public bool Matches(in IdleStamp other) =>
                HasLast == other.HasLast
                && JobId == other.JobId
                && System.Math.Abs(EndTime - other.EndTime) < 1e-4;
        }

        private void Awake()
        {
            if (m_cargoRegistry == null)
            {
                m_cargoRegistry = GetComponent<SimPlaybackCargoVisualRegistry>();
            }

            RebuildTopology();
            RebuildExecutorIndex();
        }

        public override void ResetPlaybackState()
        {
            _idleStamps.Clear();
            _configuredTimeline = null;
            _lastEvaluateSimTime = double.NaN;
            StopAllRigMotion();
            ConfigureAllRigs();
        }

        public override void OnPlaybackEvaluate(double simTime, IReadOnlyList<SimSubTask> subTasks)
        {
            EnsureExecutorsConfigured(subTasks);
            if (!double.IsNaN(_lastEvaluateSimTime) && simTime + 1e-4 < _lastEvaluateSimTime)
            {
                _idleStamps.Clear();
            }

            _lastEvaluateSimTime = simTime;

            if (subTasks == null || subTasks.Count == 0)
            {
                return;
            }

            if (!ValidateEvaluateReady(out var skipReason))
            {
                LogEvaluateSkipOnce(skipReason);
                return;
            }

            _loggedEvaluateSkip = false;

            if (!_subTaskIndex.IsCurrentSource(subTasks))
            {
                _subTaskIndex.Build(subTasks);
            }

            if (_executorList.Count == 0)
            {
                LogEvaluateSkipOnce("未绑定任何堆垛机执行器，回放不会更新堆垛机姿态。");
                return;
            }

            for (var i = 0; i < _executorList.Count; i++)
            {
                var executor = _executorList[i];
                if (executor == null)
                {
                    continue;
                }

                var stackerId = executor.StackerId;
                if (StackerJobPlaybackResolver.TryBuildActiveContext(
                        subTasks,
                        _subTaskIndex,
                        stackerId,
                        simTime,
                        out var context))
                {
                    _idleStamps.Remove(stackerId);
                    executor.Evaluate(in context);
                    continue;
                }

                if (_subTaskIndex.TryGetActiveForStacker(stackerId, simTime, out _))
                {
                    LogEvaluateSkipOnce(
                        $"堆垛机 {stackerId} 在 t={simTime:F2}s 有活动子任务但无法构建回放上下文，本帧跳过 RestoreIdle。");
                    continue;
                }

                var hasLast = StackerJobPlaybackResolver.TryGetLastCompletedJob(
                    subTasks,
                    _subTaskIndex,
                    stackerId,
                    simTime,
                    out var last);
                var stamp = new IdleStamp
                {
                    HasLast = hasLast,
                    JobId = hasLast ? last.JobId : 0,
                    EndTime = hasLast ? last.TaskEndSimTime : 0d,
                };
                if (_idleStamps.TryGetValue(stackerId, out var applied) && applied.Matches(stamp))
                {
                    continue;
                }

                if (hasLast)
                {
                    executor.RestoreIdle(simTime, in last, hasLastTask: true);
                }
                else
                {
                    executor.RestoreIdle(simTime, default, hasLastTask: false);
                }

                _idleStamps[stackerId] = stamp;
            }
        }

        private void StopAllRigMotion()
        {
            for (var i = 0; i < _executorList.Count; i++)
            {
                _executorList[i]?.StopMotion();
            }
        }

        private bool ValidateEvaluateReady(out string skipReason)
        {
            if (m_bindings == null)
            {
                skipReason = "未配置「硬件绑定」，堆垛机回放不会更新。";
                return false;
            }

            if (HasWarehouseList)
            {
                skipReason = null;
                return true;
            }

            if (m_positionIndex == null)
            {
                skipReason = "未配置「货位坐标索引」，堆垛机回放不会更新。";
                return false;
            }

            if (!m_positionIndex.IsReady && !m_positionIndex.TryLoad())
            {
                skipReason = "货位坐标索引尚未加载，堆垛机回放不会更新。";
                return false;
            }

            skipReason = null;
            return true;
        }

        private void EnsureExecutorsConfigured(IReadOnlyList<SimSubTask> subTasks)
        {
            if (_topology == null)
            {
                RebuildTopology();
            }

            if (_executors.Count == 0 || !_rigsInitialized)
            {
                RebuildExecutorIndex();
            }

            if (ReferenceEquals(_configuredTimeline, subTasks))
            {
                return;
            }

            ConfigureAllRigs();
            _configuredTimeline = subTasks;
            _idleStamps.Clear();
        }

        private void ConfigureAllRigs()
        {
            if (m_stackers == null)
            {
                return;
            }

            for (var i = 0; i < m_stackers.Length; i++)
            {
                var rig = m_stackers[i];
                if (rig == null)
                {
                    continue;
                }

                ConfigureRig(rig);
            }
        }

        private void RebuildExecutorIndex()
        {
            _executors.Clear();
            _executorList.Clear();
            _rigsInitialized = false;
            if (m_stackers == null)
            {
                return;
            }

            for (var i = 0; i < m_stackers.Length; i++)
            {
                var rig = m_stackers[i];
                if (rig == null)
                {
                    continue;
                }

                ConfigureRig(rig);

                if (!rig.TryInitialize())
                {
                    continue;
                }

                _executors[rig.StackerId] = rig;
                _executorList.Add(rig);
            }

            _rigsInitialized = _executorList.Count > 0;
            if (_executorList.Count == 0)
            {
                SimPlaybackLog.Warn(
                    "未成功绑定任何堆垛机 Rig（请检查 m_stackers、关节控制器引用及 m_stackerId）。",
                    this);
            }
        }

        private void ConfigureRig(JointControllerStackerJobRigPlayback rig)
        {
            var warehouseId = 0;
            if (m_bindings != null
                && StackerColumnReachUtility.TryGetDefinition(m_bindings, _topology, rig.StackerId, out var definition))
            {
                warehouseId = definition.WarehouseId;
            }

            rig.Configure(
                m_bindings,
                m_playback,
                ResolvePositionIndex(warehouseId),
                m_pickupLevel,
                _topology,
                m_cargoRegistry,
                ResolveGrid(warehouseId),
                warehouseId);
        }

        private bool HasWarehouseList => m_warehouses != null && m_warehouses.Length > 0;

        private WarehouseManagerBinPositionIndex ResolvePositionIndex(int warehouseId)
        {
            if (HasWarehouseList)
            {
                for (var i = 0; i < m_warehouses.Length; i++)
                {
                    var binding = m_warehouses[i];
                    if (binding == null || binding.WarehouseId != warehouseId || binding.PositionIndex == null)
                    {
                        continue;
                    }

                    binding.PositionIndex.UseWarehouse(warehouseId);
                    return binding.PositionIndex;
                }
            }
            else if (warehouseId == 0)
            {
                m_positionIndex?.UseWarehouse(0);
                return m_positionIndex;
            }

            if (_missingPositionWarehouses.Add(warehouseId))
            {
                SimPlaybackLog.Warn($"堆垛机所属仓库 {warehouseId} 未配置货位坐标索引。", this);
            }

            return null;
        }

        private WarehouseGridConfig ResolveGrid(int warehouseId)
        {
            if (WarehouseCatalog.TryGet(m_bindings, warehouseId, out var definition) && definition.Grid != null)
            {
                return definition.Grid;
            }

            return warehouseId == 0 ? m_playback?.EventSource?.GridConfig : null;
        }

        private bool RebuildTopology()
        {
            _topology = null;
            return SimPlaybackTopologyUtility.TryBuild(m_bindings?.ConveyorMap, m_bindings, out _topology, this);
        }

        private void LogEvaluateSkipOnce(string reason)
        {
            if (_loggedEvaluateSkip)
            {
                return;
            }

            _loggedEvaluateSkip = true;
            SimPlaybackLog.Warn($"StackerHandler {reason}", this);
        }
    }
}
