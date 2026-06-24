using System;
using System.Collections.Generic;
using System.IO;
using NonsensicalKit.DigitalTwin.Warehouse;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>
    /// 从 WarehouseManager 同款 .dat 加载货位局部坐标。
    /// </summary>
    public sealed class SlotPositionIndex : ISlotPositionIndex
    {
        private readonly Dictionary<GridIndex, Vector3> _localBySlot = new();

        public bool IsReady => _localBySlot.Count > 0;

        public static SlotPositionIndex LoadFromFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("仓库数据路径不能为空。", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"仓库数据不存在：{filePath}", filePath);
            }

            var data = BinDataIO.LoadSync(filePath);
            return FromWarehouseData(data);
        }

        public static SlotPositionIndex TryLoadFromStreamingAssets(string warehouseName)
        {
            if (string.IsNullOrWhiteSpace(warehouseName))
            {
                return null;
            }

            var path = Path.Combine(Application.streamingAssetsPath, "Warehouse", $"{warehouseName}.dat");
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[WarehouseSimulation] 仓库数据不存在：{path}");
                return null;
            }

            return LoadFromFile(path);
        }

        public static SlotPositionIndex FromWarehouseData(WarehouseData data)
        {
            var index = new SlotPositionIndex();
            if (data?.Bins == null || data.Bins.Length == 0)
            {
                return index;
            }

            for (var i = 0; i < data.Bins.Length; i++)
            {
                var bin = data.Bins[i];
                index._localBySlot[new GridIndex(bin.Level, bin.Column, bin.Row, bin.Depth)] =
                    new Vector3(bin.PosX, bin.PosY, bin.PosZ);
            }

            return index;
        }

        public bool TryGetLocalPosition(GridIndex slot, out Vector3 local) =>
            _localBySlot.TryGetValue(slot, out local);

        public bool TryGetAxisPosition(int level, int column, int row, out Vector3 local)
        {
            if (TryGetLocalPosition(new GridIndex(level, column, row, 0), out local))
            {
                return true;
            }

            if (!TryGetAxisCoordinate(column, (slot, value) => slot.Column == column, value => value.x, out var x)
                || !TryGetAxisCoordinate(level, (slot, value) => slot.Level == level, value => value.y, out var y)
                || !TryGetAxisCoordinate(row, (slot, value) => slot.Row == row, value => value.z, out var z))
            {
                local = default;
                return false;
            }

            local = new Vector3(x, y, z);
            return true;
        }

        public bool TryGetLevelY(int level, int column, int preferredRow, out float y)
        {
            if (TryGetAxisComponent(level, column, preferredRow, component => component.y, out y))
            {
                return true;
            }

            foreach (var pair in _localBySlot)
            {
                if (pair.Key.Level == level && pair.Key.Column == column)
                {
                    y = pair.Value.y;
                    return true;
                }
            }

            y = default;
            return false;
        }

        public bool TryGetRowZ(int level, int column, int row, out float z)
        {
            if (TryGetAxisComponent(level, column, row, component => component.z, out z))
            {
                return true;
            }

            z = default;
            return false;
        }

        /// <summary>在指定层/列上取任意一排的坐标（用于 .dat 未录入 row=0 时的原点校准）。</summary>
        public bool TryGetFirstRowOnLevelColumn(
            int level,
            int column,
            out int row,
            out Vector3 local)
        {
            row = 0;
            local = default;
            var found = false;
            foreach (var pair in _localBySlot)
            {
                var slot = pair.Key;
                if (slot.Level != level || slot.Column != column)
                {
                    continue;
                }

                if (!found || slot.Row < row)
                {
                    row = slot.Row;
                    local = pair.Value;
                    found = true;
                }
            }

            return found;
        }

        private bool TryGetAxisComponent(
            int level,
            int column,
            int row,
            System.Func<Vector3, float> select,
            out float value)
        {
            if (TryGetLocalPosition(new GridIndex(level, column, row, 0), out var local))
            {
                value = select(local);
                return true;
            }

            foreach (var pair in _localBySlot)
            {
                var slot = pair.Key;
                if (slot.Level == level && slot.Column == column && slot.Row == row)
                {
                    value = select(pair.Value);
                    return true;
                }
            }

            value = default;
            return false;
        }

        private bool TryGetAxisCoordinate(
            int _,
            Func<GridIndex, Vector3, bool> matches,
            Func<Vector3, float> select,
            out float coordinate)
        {
            foreach (var pair in _localBySlot)
            {
                if (matches(pair.Key, pair.Value))
                {
                    coordinate = select(pair.Value);
                    return true;
                }
            }

            coordinate = default;
            return false;
        }
    }
}
