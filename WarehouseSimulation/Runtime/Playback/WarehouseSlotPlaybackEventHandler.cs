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
            ApplySnapshotDiff(in snapshot);
        }

        private void ClearWarehouseOccupancyFromGrid()
        {
            ResolveEventSource();
            _exclusionByWarehouse.Clear();
            ForEachConfiguredWarehouse(warehouseId =>
            {
                TryGetGrid(warehouseId, out var grid);
                ClearWarehouse(warehouseId, grid, AdapterFor(warehouseId));
            });
        }

        private void ClearWarehouse(int warehouseId, WarehouseGridConfig grid, WarehouseManagerSlotVisualAdapter adapter)
        {
            if (grid == null || adapter == null)
            {
                return;
            }

            var zones = ExclusionZones(warehouseId, grid);
            var slots = new List<GridIndex>();
            for (var level = 0; level < grid.LevelCount; level++)
            {
                for (var column = 0; column < grid.ColumnCount; column++)
                {
                    for (var row = 0; row < grid.RowCount; row++)
                    {
                        if (!SlotGridUtility.IsStorageSlot(grid, level, column, row, zones))
                        {
                            continue;
                        }

                        slots.Add(new GridIndex(level, column, row, 0, warehouseId));
                    }
                }
            }

            adapter.ApplyOccupancyBatch(slots, occupied: false);
        }

        private void HideHighlight()
        {
            ForEachConfiguredWarehouse(warehouseId => AdapterFor(warehouseId)?.HideHighlight());
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

                AdapterFor(slot.WarehouseId)?.SetOccupied(slot, true);
                _previousOccupied.Add(slot);
            }
        }

        private void ApplySnapshotDiff(in WarehouseSlotPlaybackSnapshot snapshot)
        {
            var occupied = snapshot.OccupiedSlots;

            foreach (var slot in _previousOccupied)
            {
                if (occupied == null || !occupied.Contains(slot))
                {
                    AdapterFor(slot.WarehouseId)?.SetOccupied(slot, false);
                }
            }

            if (occupied != null)
            {
                foreach (var slot in occupied)
                {
                    if (!IsPlaybackStorageSlot(slot) || _previousOccupied.Contains(slot))
                    {
                        continue;
                    }

                    AdapterFor(slot.WarehouseId)?.SetOccupied(slot, true);
                }
            }

            _previousOccupied.Clear();
            if (occupied != null)
            {
                foreach (var slot in occupied)
                {
                    if (IsPlaybackStorageSlot(slot))
                    {
                        _previousOccupied.Add(slot);
                    }
                }
            }

            if (!EqualsGridIndex(_previousHighlight, snapshot.HighlightSlot))
            {
                if (_previousHighlight.HasValue)
                {
                    AdapterFor(_previousHighlight.Value.WarehouseId)?.HideHighlight();
                }

                _previousHighlight = snapshot.HighlightSlot;
                if (snapshot.HighlightSlot.HasValue)
                {
                    AdapterFor(snapshot.HighlightSlot.Value.WarehouseId)?.ApplyHighlight(snapshot.HighlightSlot.Value);
                }
            }
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

            for (var i = 0; i < m_warehouses.Length; i++)
            {
                var binding = m_warehouses[i];
                if (binding != null)
                {
                    action(binding.WarehouseId);
                }
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
            if (m_warehouses == null)
            {
                return null;
            }

            for (var i = 0; i < m_warehouses.Length; i++)
            {
                var binding = m_warehouses[i];
                if (binding != null && binding.WarehouseId == warehouseId)
                {
                    return binding.Manager;
                }
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
