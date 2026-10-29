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
        private bool _rigsInitialized;
        private readonly HashSet<int> _outboundJobIds = new();
        private readonly Dictionary<GridIndex, SimSubTask> _duePlaceBySlot = new();
        private readonly WarehouseSlotPlaybackSnapshotIndex _duePlaceOccupancy = new();
        private bool _duePlaceBuilt;

        private struct IdleStamp
        {
            public bool HasLast;
            public int JobId;
            public double EndTime;
            public bool ReleaseCargo;

            public bool Matches(in IdleStamp other) =>
                HasLast == other.HasLast
                && JobId == other.JobId
                && ReleaseCargo == other.ReleaseCargo
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
            _duePlaceOccupancy.ResetPlaybackCursor();
            _outboundJobIds.Clear();
            _duePlaceBySlot.Clear();
            _duePlaceBuilt = false;
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

            if (!ValidateEvaluateReady())
            {
                return;
            }

            if (!_duePlaceBuilt || !_subTaskIndex.IsCurrentSource(subTasks))
            {
                _subTaskIndex.Build(subTasks);
                _duePlaceOccupancy.Build(subTasks, highlightOnStackerMove: false);
                _outboundJobIds.Clear();
                _outboundJobIds.UnionWith(SimSubTaskQuery.BuildOutboundJobIds(subTasks));
                _duePlaceBuilt = true;
            }

            if (_executorList.Count == 0)
            {
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
                    ReleaseCargo = hasLast && last.ReleaseCargoOnIdle,
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

            CatchUpDueInboundPlaces(simTime);
        }

        /// <summary>
        /// 倍速或跳帧可能整段跨过伸叉提交点。货位快照已经占上的入库货，把场景料箱补停进去，避免货架 GPU 再画一只。
        /// </summary>
        private void CatchUpDueInboundPlaces(double simTime)
        {
            var occupied = _duePlaceOccupancy.BuildAt(simTime).OccupiedSlots;
            if (occupied == null || occupied.Count == 0)
            {
                return;
            }

            _duePlaceBySlot.Clear();
            for (var i = 0; i < _executorList.Count; i++)
            {
                var executor = _executorList[i];
                if (executor == null
                    || !_subTaskIndex.TryGetStackerTasks(executor.StackerId, out var tasks)
                    || tasks == null)
                {
                    continue;
                }

                for (var t = 0; t < tasks.Count; t++)
                {
                    var place = tasks[t];
                    if (place.Kind != SimSubTaskKind.StackerPlace
                        || _outboundJobIds.Contains(place.JobId)
                        || !occupied.Contains(place.Slot)
                        || simTime + 1e-4 < StackerCargoHandover.Resolve(in place))
                    {
                        continue;
                    }

                    if (!_duePlaceBySlot.TryGetValue(place.Slot, out var existing)
                        || place.EndSimTime >= existing.EndSimTime)
                    {
                        _duePlaceBySlot[place.Slot] = place;
                    }
                }
            }

            foreach (var pair in _duePlaceBySlot)
            {
                var place = pair.Value;
                if (!_executors.TryGetValue(place.StackerId, out var executor) || executor == null)
                {
                    continue;
                }

                var visual = place.HasStackerPose ? place.StackerToSlot : place.Slot;
                executor.EnsureInboundCargoParked(place.JobId, place.Slot, visual);
            }
        }

        private void StopAllRigMotion()
        {
            for (var i = 0; i < _executorList.Count; i++)
            {
                _executorList[i]?.StopMotion();
            }
        }

        private bool ValidateEvaluateReady()
        {
            if (m_bindings == null)
            {
                return false;
            }

            if (HasWarehouseList)
            {
                return true;
            }

            if (m_positionIndex == null)
            {
                return false;
            }

            return m_positionIndex.IsReady || m_positionIndex.TryLoad();
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
    }
}
