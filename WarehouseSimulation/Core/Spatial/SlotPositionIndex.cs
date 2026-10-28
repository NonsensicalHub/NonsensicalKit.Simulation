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
        private SlotLocalAxisMapping _axisMapping = SlotLocalAxisMapping.DefaultProduction;
        private bool _hasAxisMapping;
        private WarehouseSlotDisableRule[] _slotDisableRules = System.Array.Empty<WarehouseSlotDisableRule>();

        public bool IsReady => _localBySlot.Count > 0;

        public bool HasAxisMapping => _hasAxisMapping;

        public SlotLocalAxisMapping AxisMapping => _axisMapping;

        /// <summary>.dat DSBL 结构禁用规则（与 WarehouseManager 一致）。</summary>
        public WarehouseSlotDisableRule[] SlotDisableRules => _slotDisableRules;

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
            if (data == null)
            {
                return index;
            }

            if (data.SlotDisableRules != null && data.SlotDisableRules.Length > 0)
            {
                index._slotDisableRules = data.SlotDisableRules;
            }

            if (data.Bins == null || data.Bins.Length == 0)
            {
                return index;
            }

            for (var i = 0; i < data.Bins.Length; i++)
            {
                var bin = data.Bins[i];
                index._localBySlot[new GridIndex(bin.Level, bin.Column, bin.Row, bin.Depth)] =
                    new Vector3(bin.PosX, bin.PosY, bin.PosZ);
            }

            // 货位入表后再探测：只扫小网格，避免对全库 O(n²)。
            index._axisMapping = SlotLocalAxisDetector.Detect(index);
            index._hasAxisMapping = true;
            return index;
        }

        public bool HasExactLocalPosition(GridIndex slot)
        {
            if (slot.WarehouseId != 0)
            {
                slot.WarehouseId = 0;
            }

            return _localBySlot.ContainsKey(slot);
        }

        public bool TryGetLocalPosition(GridIndex slot, out Vector3 local)
        {
            if (slot.WarehouseId != 0)
            {
                slot.WarehouseId = 0;
            }

            return _localBySlot.TryGetValue(slot, out local);
        }

        public bool TryGetAxisPosition(int level, int column, int row, out Vector3 local)
        {
            if (TryGetLocalPosition(new GridIndex(level, column, row, 0), out local))
            {
                return true;
            }

            var axes = _hasAxisMapping ? _axisMapping : SlotLocalAxisMapping.DefaultProduction;
            // 排向允许外推（缺 row=0 bin 时仍能合成原点）；层/列仍要求至少有一条精确分量。
            if (!TryGetAxisCoordinate(column, (slot, value) => slot.Column == column, value => axes.ForkOf(value), out var fork)
                || !TryGetAxisCoordinate(level, (slot, value) => slot.Level == level, value => axes.LevelOf(value), out var levelValue)
                || !TryGetRowAxisCoordinate(row, out var rowValue))
            {
                local = default;
                return false;
            }

            local = Vector3.zero;
            local[axes.LevelAxis] = levelValue;
            local[axes.RowAxis] = rowValue;
            local[axes.ForkAxis] = fork;
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

        /// <summary>
        /// 排向轴坐标：优先任意 Row==row 的精确分量；缺该排时按已有排距外推到目标排。
        /// 双库底层常缺 row=0 bin，原点必须外推到 0，不能用「最小存在排」整点冒充。
        /// </summary>
        public bool TryGetRowAxisCoordinate(int row, out float rowCoordinate)
        {
            var axes = _hasAxisMapping ? _axisMapping : SlotLocalAxisMapping.DefaultProduction;
            if (TryGetAxisCoordinate(
                    row,
                    (slot, _) => slot.Row == row,
                    value => axes.RowOf(value),
                    out rowCoordinate))
            {
                return true;
            }

            return TryExtrapolateRowAxisCoordinate(row, axes, out rowCoordinate);
        }

        /// <summary>
        /// 用已排序相邻排的精确货位中位数估计排距（全排通用，不针对某几排）。
        /// 每排取首次见到的排向分量；至少需要两档不同排号。
        /// </summary>
        public bool TryEstimateRowPitch(out float pitch)
        {
            pitch = 0f;
            var axes = _hasAxisMapping ? _axisMapping : SlotLocalAxisMapping.DefaultProduction;
            // row -> first axis coordinate
            var byRow = new System.Collections.Generic.SortedDictionary<int, float>();
            foreach (var pair in _localBySlot)
            {
                var row = pair.Key.Row;
                if (byRow.ContainsKey(row))
                {
                    continue;
                }

                byRow[row] = axes.RowOf(pair.Value);
            }

            if (byRow.Count < 2)
            {
                return false;
            }

            var pitches = new System.Collections.Generic.List<float>();
            int? prevRow = null;
            float prevCoord = 0f;
            foreach (var kv in byRow)
            {
                if (prevRow.HasValue)
                {
                    var deltaRow = kv.Key - prevRow.Value;
                    if (deltaRow != 0)
                    {
                        var p = (kv.Value - prevCoord) / deltaRow;
                        if (Mathf.Abs(p) > 1e-5f)
                        {
                            pitches.Add(p);
                        }
                    }
                }

                prevRow = kv.Key;
                prevCoord = kv.Value;
            }

            if (pitches.Count == 0)
            {
                return false;
            }

            pitches.Sort();
            pitch = pitches[pitches.Count / 2];
            return Mathf.Abs(pitch) > 1e-5f;
        }

        private bool TryExtrapolateRowAxisCoordinate(
            int row,
            in SlotLocalAxisMapping axes,
            out float rowCoordinate)
        {
            rowCoordinate = 0f;
            // 取最靠近目标排的精确排作为锚点，避免字典首次条目漂到远排。
            int sampleRow = -1;
            float sampleCoord = 0f;
            var bestDist = int.MaxValue;
            foreach (var pair in _localBySlot)
            {
                var dist = pair.Key.Row > row ? pair.Key.Row - row : row - pair.Key.Row;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    sampleRow = pair.Key.Row;
                    sampleCoord = axes.RowOf(pair.Value);
                    if (dist == 0)
                    {
                        break;
                    }
                }
            }

            if (sampleRow < 0)
            {
                return false;
            }

            if (sampleRow == row)
            {
                rowCoordinate = sampleCoord;
                return true;
            }

            if (!TryEstimateRowPitch(out var pitch))
            {
                return false;
            }

            rowCoordinate = sampleCoord + (row - sampleRow) * pitch;
            return true;
        }
    }
}
