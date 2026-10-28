using System;
using System.Collections.Generic;
using NaughtyAttributes;
using NonsensicalKit.DigitalTwin.Warehouse;
using UnityEngine;
using UnityEngine.Serialization;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>
    /// 货位回放：从子任务时间轴构建宏观快照（占用/高亮），并绑定数字孪生 WarehouseManager。
    /// </summary>
    public sealed class WarehouseSlotPlaybackEventHandler : SimPlaybackEventHandlerBehaviour
    {
        [SerializeField, Label("数字孪生仓库")]
        [Tooltip("仓库列表留空时，作为唯一的 0 号库。列表有内容时以列表为准。")]
        private WarehouseManager m_warehouseManager;

        [FormerlySerializedAs("m_additionalWarehouses")]
        [SerializeField, Label("仓库列表")]
        [Tooltip("每一项一座仓库，包含 0 号库。留空则使用上方的单个数字孪生仓库。")]
        private WarehousePlaybackBinding[] m_warehouses;

        [SerializeField, Label("未就绪时输出警告")]
        private bool m_warnWhenNotReady = true;

        [SerializeField, Label("移动阶段高亮目标货位")]
        private bool m_highlightOnStackerMove = true;

        [SerializeField, Label("回放事件源（初始占用）")]
        private SimPlaybackEventSource m_eventSource;

        private readonly HashSet<GridIndex> _previousOccupied = new();
        private readonly List<GridIndex> _occupancyClearBuffer = new();
        private SimPlaybackCargoVisualRegistry _cargoRegistry;
        private bool _cargoRegistryResolved;
        private double _logSimTime = double.NaN;
        private int _slotDiagFrame = -1;
        private int _slotDiagCount;
        private readonly HashSet<GridIndex> _loggedParkSkip = new();
        private readonly HashSet<GridIndex> _loggedGpuFail = new();
        private readonly HashSet<int> _outboundJobIds = new();
        private readonly WarehouseSlotPlaybackSnapshotIndex _snapshotIndex = new();
        private GridIndex? _previousHighlight;
        private bool _loggedSkip;
        private readonly Dictionary<int, SimSlotExclusionZone[]> _exclusionByWarehouse = new();
        private readonly Dictionary<int, WarehouseManagerSlotVisualAdapter> _extraAdapters = new();
        private WarehouseManagerSlotVisualAdapter _adapter;

        private void Awake()
        {
            ResolveEventSource();
        }

        private SimPlaybackEventSource PlaybackEventSource
        {
            get
            {
                ResolveEventSource();
                return m_eventSource;
            }
        }

        private WarehouseManagerSlotVisualAdapter Visual =>
            _adapter ??= m_warehouseManager != null
                ? new WarehouseManagerSlotVisualAdapter(m_warehouseManager, m_warnWhenNotReady)
                : null;

        public override void ResetPlaybackState()
        {
            ClearWarehouseOccupancyFromGrid();
            HideHighlight();

            foreach (var slot in _previousOccupied)
            {
                AdapterFor(slot.WarehouseId)?.SetOccupied(slot, false);
            }

            _previousOccupied.Clear();
            _outboundJobIds.Clear();
            _snapshotIndex.ResetPlaybackCursor();
            _previousHighlight = null;
            _loggedSkip = false;
            _loggedParkSkip.Clear();
            _loggedGpuFail.Clear();
            ApplyInitialOccupancySlots();
        }

        public override void OnPlaybackEvaluate(double simTime, IReadOnlyList<SimSubTask> subTasks)
        {
            ResolveEventSource();
            if (!HasPlaybackTarget() || subTasks == null || subTasks.Count == 0)
            {
                LogSkipOnce();
                return;
            }

            if (!_snapshotIndex.IsCurrentSource(subTasks))
            {
                _snapshotIndex.Build(subTasks, m_highlightOnStackerMove, m_eventSource?.InitialOccupiedSlots);
                _outboundJobIds.Clear();
                _outboundJobIds.UnionWith(SimSubTaskQuery.BuildOutboundJobIds(subTasks));
                _exclusionByWarehouse.Clear();
            }

            var snapshot = _snapshotIndex.BuildAt(simTime);
            _logSimTime = simTime;
            ApplySnapshotDiff(in snapshot);
        }

        private void ClearWarehouseOccupancyFromGrid()
        {
            ResolveEventSource();
            _exclusionByWarehouse.Clear();
            // 不依赖网格枚举：WarehouseManager 默认 ShowAllCargo，0% 初始必须 ClearAllCargo。
            // 旧逻辑在 TryGetGrid 失败时直接跳过，会出现「二号库空、一号库仍满」。
            ForEachConfiguredWarehouse(warehouseId => AdapterFor(warehouseId)?.ClearAllOccupied());
        }

        private void HideHighlight()
        {
            ForEachConfiguredWarehouse(warehouseId => AdapterFor(warehouseId)?.HideHighlight());
        }

        /// <summary>
        /// 堆垛机已把场景料箱停在该占用货位上时跳过 GPU，避免叠画。
        /// 料箱停在货叉尖（场景放货点），不一定等于货位索引世界坐标；不要再拉回索引坐标，
        /// 否则又会和「货叉不追绝对列」打架，看起来像货物不同步。
        /// </summary>
        private bool IsSlotHeldBySceneCargo(GridIndex slot)
        {
            var registry = ResolveCargoRegistry();
            return registry != null && registry.IsSlotParked(slot);
        }

        /// <summary>
        /// 料箱登记和货位脚本是同级物体，不在同一条父链上。先向上找，再在父节点的子级里找。
        /// </summary>
        private SimPlaybackCargoVisualRegistry ResolveCargoRegistry()
        {
            if (_cargoRegistryResolved)
            {
                return _cargoRegistry;
            }

            _cargoRegistryResolved = true;
            _cargoRegistry = GetComponentInParent<SimPlaybackCargoVisualRegistry>();
            if (_cargoRegistry != null)
            {
                return _cargoRegistry;
            }

            var parent = transform.parent;
            if (parent != null)
            {
                _cargoRegistry = parent.GetComponentInChildren<SimPlaybackCargoVisualRegistry>(true);
            }

            return _cargoRegistry;
        }

        private void ResolveEventSource()
        {
            if (m_eventSource != null)
            {
                return;
            }

            m_eventSource = GetComponent<SimPlaybackEventSource>();
            if (m_eventSource != null)
            {
                return;
            }

            var playback = GetComponentInParent<WarehouseSimPlaybackController>();
            if (playback != null)
            {
                m_eventSource = playback.EventSource;
            }
        }

        private void ApplyInitialOccupancySlots()
        {
            ResolveEventSource();
            var initial = m_eventSource?.InitialOccupiedSlots;
            if (!HasPlaybackTarget() || initial == null || initial.Count == 0)
            {
                return;
            }

            for (var i = 0; i < initial.Count; i++)
            {
                var slot = initial[i];
                if (!IsPlaybackStorageSlot(slot))
                {
                    continue;
                }

                if (TrySetOccupied(slot, true))
                {
                    _previousOccupied.Add(slot);
                }
            }
        }

        private void ApplySnapshotDiff(in WarehouseSlotPlaybackSnapshot snapshot)
        {
            var occupied = snapshot.OccupiedSlots;
            // 堆垛机已经把场景料箱停在货位上的，这里收掉快照里不再占用的；还占着的不要再写 GPU。
            ResolveCargoRegistry()?.ReleaseParkedExcept(occupied, _logSimTime);
            _occupancyClearBuffer.Clear();
            foreach (var slot in _previousOccupied)
            {
                if (occupied == null || !occupied.Contains(slot))
                {
                    _occupancyClearBuffer.Add(slot);
                }
            }

            for (var i = 0; i < _occupancyClearBuffer.Count; i++)
            {
                var slot = _occupancyClearBuffer[i];
                if (TrySetOccupied(slot, false))
                {
                    _previousOccupied.Remove(slot);
                    _loggedParkSkip.Remove(slot);
                    LogSlotDiag($"隐藏 {DescribeSlot(slot)}");
                }
            }

            if (occupied != null)
            {
                foreach (var slot in occupied)
                {
                    if (IsSlotHeldBySceneCargo(slot))
                    {
                        if (_loggedParkSkip.Add(slot))
                        {
                            LogSlotDiag($"跳过GPU {DescribeSlot(slot)} 原因=场景料箱已停在此货位");
                        }

                        if (_previousOccupied.Contains(slot) && TrySetOccupied(slot, false))
                        {
                            _previousOccupied.Remove(slot);
                            LogSlotDiag($"改由场景料箱显示，关掉GPU {DescribeSlot(slot)}");
                        }

                        continue;
                    }

                    if (!IsPlaybackStorageSlot(slot) || _previousOccupied.Contains(slot))
                    {
                        continue;
                    }

                    if (TrySetOccupied(slot, true))
                    {
                        _previousOccupied.Add(slot);
                        LogSlotDiag($"GPU显示 {DescribeSlot(slot)}");
                    }
                    else if (_loggedGpuFail.Add(slot))
                    {
                        LogSlotDiag($"GPU显示失败 {DescribeSlot(slot)}");
                    }
                }
            }

            ApplyHighlightDiff(in snapshot);
        }

        private const int SlotDiagCapPerFrame = 24;

        private void LogSlotDiag(string message)
        {
            var frame = Time.frameCount;
            if (frame != _slotDiagFrame)
            {
                _slotDiagFrame = frame;
                _slotDiagCount = 0;
            }

            if (_slotDiagCount == SlotDiagCapPerFrame)
            {
                _slotDiagCount++;
                SimPlaybackLog.CargoSync($"t={_logSimTime:F3} 本帧货位日志超过 {SlotDiagCapPerFrame} 条，其余省略");
                return;
            }

            if (_slotDiagCount > SlotDiagCapPerFrame)
            {
                return;
            }

            _slotDiagCount++;
            SimPlaybackLog.CargoSync($"t={_logSimTime:F3} {message}", this);
        }

        private string DescribeSlot(GridIndex slot)
        {
            var manager = FindManager(slot.WarehouseId);
            var managerName = manager != null ? manager.name : "无管理器";
            var world = "无坐标";
            if (m_warehouses != null)
            {
                for (var i = 0; i < m_warehouses.Length; i++)
                {
                    var binding = m_warehouses[i];
                    if (binding == null || binding.WarehouseId != slot.WarehouseId || binding.PositionIndex == null)
                    {
                        continue;
                    }

                    world = binding.PositionIndex.TryGetWorldPosition(slot, out var position)
                        ? position.ToString("F2")
                        : "查不到坐标";
                    break;
                }
            }

            return $"slot={slot} manager={managerName} world={world}";
        }

        /// <summary>管理器未就绪或未绑定时返回 false，调用方不得把这次变更记成已应用。</summary>
        private bool TrySetOccupied(GridIndex slot, bool occupied)
        {
            var adapter = AdapterFor(slot.WarehouseId);
            return adapter != null && adapter.SetOccupied(slot, occupied);
        }

        private void ApplyHighlightDiff(in WarehouseSlotPlaybackSnapshot snapshot)
        {
            if (EqualsGridIndex(_previousHighlight, snapshot.HighlightSlot))
            {
                return;
            }

            if (_previousHighlight.HasValue)
            {
                var previous = _previousHighlight.Value;
                var adapter = AdapterFor(previous.WarehouseId);
                if (adapter == null || !adapter.HideHighlight())
                {
                    return;
                }
            }

            if (snapshot.HighlightSlot.HasValue)
            {
                var slot = snapshot.HighlightSlot.Value;
                var adapter = AdapterFor(slot.WarehouseId);
                if (adapter == null || !adapter.ApplyHighlight(slot))
                {
                    return;
                }
            }

            _previousHighlight = snapshot.HighlightSlot;
        }

        private static bool EqualsGridIndex(GridIndex? a, GridIndex? b)
        {
            if (!a.HasValue && !b.HasValue)
            {
                return true;
            }

            if (!a.HasValue || !b.HasValue)
            {
                return false;
            }

            return a.Value.Equals(b.Value);
        }

        private bool IsPlaybackStorageSlot(GridIndex slot)
        {
            ResolveEventSource();
            if (!TryGetGrid(slot.WarehouseId, out var grid))
            {
                return slot.WarehouseId == 0;
            }

            return SlotGridUtility.IsStorageSlot(
                grid,
                slot.Level,
                slot.Column,
                slot.Row,
                ExclusionZones(slot.WarehouseId, grid));
        }

        private bool TryGetGrid(int warehouseId, out WarehouseGridConfig grid)
        {
            if (WarehouseCatalog.TryGet(PlaybackEventSource?.HardwareBindings, warehouseId, out var definition)
                && definition.Grid != null)
            {
                grid = definition.Grid;
                return true;
            }

            if (warehouseId == 0)
            {
                grid = PlaybackEventSource?.GridConfig;
                return grid != null;
            }

            grid = null;
            return false;
        }

        private SimSlotExclusionZone[] ExclusionZones(int warehouseId, WarehouseGridConfig grid)
        {
            if (_exclusionByWarehouse.TryGetValue(warehouseId, out var cached))
            {
                return cached;
            }

            var zones = SlotGridUtility.BuildEffectiveExclusionZones(
                grid,
                PlaybackEventSource?.HardwareBindings,
                warehouseId,
                restrictToWarehouse: true);
            _exclusionByWarehouse[warehouseId] = zones;
            return zones;
        }

        private bool HasWarehouseList => m_warehouses != null && m_warehouses.Length > 0;

        private bool HasPlaybackTarget()
        {
            if (!HasWarehouseList)
            {
                return m_warehouseManager != null;
            }

            for (var i = 0; i < m_warehouses.Length; i++)
            {
                if (m_warehouses[i]?.Manager != null)
                {
                    return true;
                }
            }

            return false;
        }

        private void ForEachConfiguredWarehouse(Action<int> action)
        {
            if (!HasWarehouseList)
            {
                action(0);
                return;
            }

            var seen = new HashSet<int>();
            for (var i = 0; i < m_warehouses.Length; i++)
            {
                var binding = m_warehouses[i];
                if (binding == null)
                {
                    continue;
                }

                action(binding.WarehouseId);
                seen.Add(binding.WarehouseId);
            }

            // 列表未收录时，旧字段 m_warehouseManager 仍可能挂着一号库；0% 清空不能漏掉它。
            if (m_warehouseManager != null && !seen.Contains(0))
            {
                action(0);
            }
        }

        private WarehouseManagerSlotVisualAdapter AdapterFor(int warehouseId)
        {
            if (!HasWarehouseList)
            {
                return warehouseId == 0 ? Visual : null;
            }

            if (_extraAdapters.TryGetValue(warehouseId, out var adapter))
            {
                return adapter;
            }

            var manager = FindManager(warehouseId);
            if (manager == null)
            {
                return null;
            }

            adapter = new WarehouseManagerSlotVisualAdapter(manager, m_warnWhenNotReady);
            _extraAdapters[warehouseId] = adapter;
            return adapter;
        }

        private WarehouseManager FindManager(int warehouseId)
        {
            if (m_warehouses != null)
            {
                for (var i = 0; i < m_warehouses.Length; i++)
                {
                    var binding = m_warehouses[i];
                    if (binding != null && binding.WarehouseId == warehouseId)
                    {
                        return binding.Manager;
                    }
                }
            }

            // 兼容：仓库号 0 仍可能只挂在旧字段 m_warehouseManager 上。
            if (warehouseId == 0)
            {
                return m_warehouseManager;
            }

            return null;
        }

        private void LogSkipOnce()
        {
            if (_loggedSkip)
            {
                return;
            }

            _loggedSkip = true;
            SimPlaybackLog.Warn("SlotHandler 跳过：未绑定 WarehouseManager。", this);
        }
    }
}
