using System;
using NaughtyAttributes;
using System.Collections.Generic;
using NonsensicalKit.Core;
using NonsensicalKit.DigitalTwin.Warehouse;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime.DigitalTwin
{
    /// <summary>
    /// 货位轴向坐标表（与 Warehouse 局部坐标一致：PosX=列、PosY=层、PosZ=排）。
    /// 堆垛机库各列/层/排可独立配置，以反映巷道列与货位列的不同间距。
    /// </summary>
    [Serializable]
    public sealed class WarehouseSlotAxisProfile
    {
        [Label("排向坐标 PosZ（米，索引=排号）")]
        public float[] RowCoordinates = Array.Empty<float>();

        [Label("列向坐标 PosX（米，索引=列号）")]
        public float[] ColumnCoordinates = Array.Empty<float>();

        [Label("层向坐标 PosY（米，索引=层号）")]
        public float[] LevelCoordinates = Array.Empty<float>();

        public void EnsureSize(int rowCount, int columnCount, int levelCount)
        {
            RowCoordinates = EnsureAxisArray(RowCoordinates, rowCount, index => index * 1.2f);
            ColumnCoordinates = EnsureAxisArray(ColumnCoordinates, columnCount, index => index * 1.4f);
            LevelCoordinates = EnsureAxisArray(LevelCoordinates, levelCount, index => index * 1.8f);
        }

        public bool TryBuildWarehouseData(
            WarehouseGridConfig grid,
            int depthCount,
            out WarehouseData data,
            out string error) =>
            TryBuildWarehouseData(grid, depthCount, bindings: null, out data, out error);

        public bool TryBuildWarehouseData(
            WarehouseGridConfig grid,
            int depthCount,
            IWarehouseSimulationBindings bindings,
            out WarehouseData data,
            out string error)
        {
            data = null;
            error = null;

            if (grid == null)
            {
                error = "网格配置不能为空。";
                return false;
            }

            if (grid.RowCount <= 0 || grid.ColumnCount <= 0 || grid.LevelCount <= 0 || depthCount <= 0)
            {
                error = "网格尺寸与深度必须大于 0。";
                return false;
            }

            if (!TryValidateAxis("排", RowCoordinates, grid.RowCount, out error)
                || !TryValidateAxis("列", ColumnCoordinates, grid.ColumnCount, out error)
                || !TryValidateAxis("层", LevelCoordinates, grid.LevelCount, out error))
            {
                return false;
            }

            var zones = SlotGridUtility.BuildEffectiveExclusionZones(grid, bindings);
            var bins = new List<BinData>();

            for (var row = 0; row < grid.RowCount; row++)
            {
                for (var column = 0; column < grid.ColumnCount; column++)
                {
                    for (var level = 0; level < grid.LevelCount; level++)
                    {
                        for (var depth = 0; depth < depthCount; depth++)
                        {
                            if (!SlotGridUtility.IsStorageSlot(grid, level, column, row, zones))
                            {
                                continue;
                            }

                            bins.Add(new BinData
                            {
                                Row = row,
                                Column = column,
                                Level = level,
                                Depth = depth,
                                PosX = ColumnCoordinates[column],
                                PosY = LevelCoordinates[level],
                                PosZ = RowCoordinates[row],
                            });
                        }
                    }
                }
            }

            data = new WarehouseData(
                bins.ToArray(),
                new Int4(grid.LevelCount, grid.ColumnCount, grid.RowCount, depthCount));
            return true;
        }

        public void ApplyUniformSpacing(Vector3 spacing)
        {
            ApplyUniformAxis(ColumnCoordinates, spacing.x);
            ApplyUniformAxis(RowCoordinates, spacing.z);
            ApplyUniformAxis(LevelCoordinates, spacing.y);
        }

        private static float[] EnsureAxisArray(float[] current, int count, Func<int, float> defaultValue)
        {
            if (current != null && current.Length == count)
            {
                return current;
            }

            var result = new float[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = current != null && i < current.Length
                    ? current[i]
                    : defaultValue(i);
            }

            return result;
        }

        private static void ApplyUniformAxis(float[] axis, float spacing)
        {
            if (axis == null)
            {
                return;
            }

            for (var i = 0; i < axis.Length; i++)
            {
                axis[i] = i * spacing;
            }
        }

        private static bool TryValidateAxis(
            string axisName,
            float[] coordinates,
            int expectedCount,
            out string error)
        {
            error = null;
            if (coordinates == null || coordinates.Length != expectedCount)
            {
                error = $"{axisName}向坐标数组长度应为 {expectedCount}，当前为 {coordinates?.Length ?? 0}。";
                return false;
            }

            return true;
        }
    }
}
