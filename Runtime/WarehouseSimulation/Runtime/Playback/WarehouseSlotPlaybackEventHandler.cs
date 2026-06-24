using System.Collections.Generic;
using NaughtyAttributes;
using NonsensicalKit.DigitalTwin.Warehouse;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback.Tasks;
using NonsensicalKit.Simulation.WarehouseSimulation.Runtime.DigitalTwin;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>
    /// 货位回放：从子任务时间轴构建宏观快照（占用/高亮），并绑定数字孪生 WarehouseManager。
    /// </summary>
    public sealed class WarehouseSlotPlaybackEventHandler : SimPlaybackEventHandlerBehaviour
    {
        [SerializeField, Label("数字孪生仓库")]
        private WarehouseManager m_warehouseManager;

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
        private SimSlotExclusionZone[] _cachedExclusionZones = System.Array.Empty<SimSlotExclusionZone>();
        private SimSlotExclusionZone[] _exclusionZones = System.Array.Empty<SimSlotExclusionZone>();
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

            if (Visual != null)
            {
                foreach (var slot in _previousOccupied)
                {
                    Visual.SetOccupied(slot, false);
                }

                Visual.HideHighlight();
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
            if (Visual == null || subTasks == null || subTasks.Count == 0)
            {
                LogSkipOnce();
                return;
            }

            if (!_snapshotIndex.IsCurrentSource(subTasks))
            {
                _snapshotIndex.Build(subTasks, m_highlightOnStackerMove, m_eventSource?.InitialOccupiedSlots);
                _outboundJobIds.Clear();
                _outboundJobIds.UnionWith(SimSubTaskQuery.BuildOutboundJobIds(subTasks));
                _cachedExclusionZones = System.Array.Empty<SimSlotExclusionZone>();
            }

            var snapshot = _snapshotIndex.BuildAt(simTime);
            ApplySnapshotDiff(in snapshot);
        }

        private void ClearWarehouseOccupancyFromGrid()
        {
            ResolveEventSource();
            var grid = PlaybackEventSource?.GridConfig;
            if (grid == null || m_warehouseManager == null || !m_warehouseManager.Inited)
            {
                return;
            }

            RebuildExclusionZones(grid);
            var slots = new List<GridIndex>();
            for (var level = 0; level < grid.LevelCount; level++)
            {
                for (var column = 0; column < grid.ColumnCount; column++)
                {
                    for (var row = 0; row < grid.RowCount; row++)
                    {
                        if (!SlotGridUtility.IsStorageSlot(grid, level, column, row, _exclusionZones))
                        {
                            continue;
                        }

                        slots.Add(new GridIndex(level, column, row));
                    }
                }
            }

            if (_adapter == null && m_warehouseManager != null)
            {
                _adapter = new WarehouseManagerSlotVisualAdapter(m_warehouseManager, m_warnWhenNotReady);
            }

            _adapter?.ApplyOccupancyBatch(slots, occupied: false);
        }

        private void HideHighlight()
        {
            if (m_warehouseManager != null && m_warehouseManager.Inited)
            {
                m_warehouseManager.HideHighlightBin();
            }
        }

        private void RebuildExclusionZones(WarehouseGridConfig grid)
        {
            _exclusionZones = SlotGridUtility.BuildEffectiveExclusionZones(
                grid,
                PlaybackEventSource?.HardwareBindings);
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
            if (Visual == null || initial == null || initial.Count == 0)
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

                Visual.SetOccupied(slot, true);
                _previousOccupied.Add(slot);
            }
        }

        private void ApplySnapshotDiff(in WarehouseSlotPlaybackSnapshot snapshot)
        {
            var occupied = snapshot.OccupiedSlots ?? new HashSet<GridIndex>();

            foreach (var slot in _previousOccupied)
            {
                if (!occupied.Contains(slot))
                {
                    Visual.SetOccupied(slot, false);
                }
            }

            foreach (var slot in occupied)
            {
                if (!IsPlaybackStorageSlot(slot))
                {
                    continue;
                }

                Visual.SetOccupied(slot, true);
            }

            _previousOccupied.Clear();
            foreach (var slot in occupied)
            {
                if (IsPlaybackStorageSlot(slot))
                {
                    _previousOccupied.Add(slot);
                }
            }

            if (!EqualsGridIndex(_previousHighlight, snapshot.HighlightSlot))
            {
                _previousHighlight = snapshot.HighlightSlot;
                if (snapshot.HighlightSlot.HasValue)
                {
                    Visual.ApplyHighlight(snapshot.HighlightSlot.Value);
                }
                else
                {
                    Visual.HideHighlight();
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
            var grid = m_eventSource?.GridConfig;
            if (grid == null)
            {
                return true;
            }

            if (_cachedExclusionZones.Length == 0 && m_eventSource != null)
            {
                _cachedExclusionZones = SlotGridUtility.BuildEffectiveExclusionZones(
                    grid,
                    m_eventSource.HardwareBindings);
            }

            return SlotGridUtility.IsStorageSlot(
                grid,
                slot.Level,
                slot.Column,
                slot.Row,
                _cachedExclusionZones);
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
