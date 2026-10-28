using System.Collections.Generic;
using NonsensicalKit.Core;
using NonsensicalKit.DigitalTwin.Warehouse;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>
    /// 将货位回放快照桥接到 <see cref="WarehouseManager"/> 的 GPU 实例渲染。
    /// </summary>
    internal sealed class WarehouseManagerSlotVisualAdapter
    {
        private readonly WarehouseManager _manager;
        private readonly bool _warnWhenNotReady;
        private bool _warnedNotReady;

        public WarehouseManagerSlotVisualAdapter(WarehouseManager manager, bool warnWhenNotReady)
        {
            _manager = manager;
            _warnWhenNotReady = warnWhenNotReady;
        }

        public void ApplySnapshot(in WarehouseSlotPlaybackSnapshot snapshot)
        {
            if (!EnsureReady())
            {
                return;
            }

            if (snapshot.HighlightSlot.HasValue)
            {
                ApplyHighlight(snapshot.HighlightSlot.Value);
            }
            else
            {
                HideHighlight();
            }

            // 必须先清空：0% 初始占用时 OccupiedSlots 为空，旧逻辑直接 return 会留下
            // WarehouseManager 默认「显示全部货物」的满库视觉（双库里常见一只库被清、另一只仍满）。
            ClearAllOccupied();
            if (snapshot.OccupiedSlots == null || snapshot.OccupiedSlots.Count == 0)
            {
                return;
            }

            ApplyOccupancyBatch(snapshot.OccupiedSlots, occupied: true);
        }

        /// <summary>清空该 WarehouseManager 全部货位货物显示（不依赖网格配置）。</summary>
        public void ClearAllOccupied()
        {
            if (!EnsureReady())
            {
                return;
            }

            _manager.ClearAllCargo(autoUpdate: true);
        }

        public bool ApplyHighlight(GridIndex slot)
        {
            if (!EnsureReady())
            {
                return false;
            }

            _manager.LocateHighlightBin(WarehouseGridIndexMapping.ToInt4(slot));
            return true;
        }

        public bool HideHighlight()
        {
            if (!EnsureReady())
            {
                return false;
            }

            _manager.HideHighlightBin();
            return true;
        }

        public bool SetOccupied(GridIndex slot, bool occupied)
        {
            if (!EnsureReady())
            {
                return false;
            }

            _manager.SetCargoState(WarehouseGridIndexMapping.ToInt4(slot), occupied, autoUpdate: true);
            return true;
        }

        public void ApplyOccupancyBatch(IReadOnlyCollection<GridIndex> slots, bool occupied)
        {
            if (!EnsureReady() || slots == null || slots.Count == 0)
            {
                return;
            }

            var locations = new Int4[slots.Count];
            var states = new bool[slots.Count];
            var index = 0;
            foreach (var slot in slots)
            {
                locations[index] = WarehouseGridIndexMapping.ToInt4(slot);
                states[index] = occupied;
                index++;
            }

            _manager.SetCargoState(locations, states, autoUpdate: true);
        }

        private bool EnsureReady()
        {
            if (_manager == null)
            {
                return false;
            }

            if (_manager.Inited)
            {
                return true;
            }

            if (_warnWhenNotReady && !_warnedNotReady)
            {
                _warnedNotReady = true;
                Debug.LogWarning("[WarehouseSimulation] WarehouseManager 尚未初始化，跳过货位更新。");
            }

            return false;
        }
    }
}
