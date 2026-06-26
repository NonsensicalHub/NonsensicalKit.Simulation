using System;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>
    /// 基于货位坐标索引的距离计算（与 WarehouseManager .dat 坐标系一致）。
    /// </summary>
    public static class SlotPositionMath
    {
        public static float ComputeLevelDistance(
            ISlotPositionIndex index,
            int railColumn,
            int fromLevel,
            int toLevel)
        {
            if (!TryGetLocalPosition(index, new GridIndex(fromLevel, railColumn, 0), out var from)
                || !TryGetLocalPosition(index, new GridIndex(toLevel, railColumn, 0), out var to))
            {
                throw BuildMissingPositionException(fromLevel, railColumn, 0, toLevel, railColumn, 0);
            }

            return Mathf.Abs(to.y - from.y);
        }

        public static float ComputeRowDistance(
            ISlotPositionIndex index,
            int railColumn,
            int level,
            int fromRow,
            int toRow)
        {
            if (!TryGetLocalPosition(index, new GridIndex(level, railColumn, fromRow), out var from)
                || !TryGetLocalPosition(index, new GridIndex(level, railColumn, toRow), out var to))
            {
                throw BuildMissingPositionException(level, railColumn, fromRow, level, railColumn, toRow);
            }

            return Mathf.Abs(to.z - from.z);
        }

        public static float ComputeManhattanDistance(
            ISlotPositionIndex index,
            in GridIndex from,
            in GridIndex to)
        {
            if (!TryGetLocalPosition(index, from, out var fromPos)
                || !TryGetLocalPosition(index, to, out var toPos))
            {
                throw new InvalidOperationException(
                    $"无法从货位坐标索引解析距离（{from} → {to}），请检查仓库数据是否已加载且包含对应货位。");
            }

            return Mathf.Abs(toPos.x - fromPos.x)
                   + Mathf.Abs(toPos.y - fromPos.y)
                   + Mathf.Abs(toPos.z - fromPos.z);
        }

        private static bool TryGetLocalPosition(
            ISlotPositionIndex index,
            in GridIndex slot,
            out Vector3 local)
        {
            local = default;
            if (index == null || !index.IsReady)
            {
                return false;
            }

            if (index.TryGetLocalPosition(slot, out local))
            {
                return true;
            }

            return index.TryGetAxisPosition(slot.Level, slot.Column, slot.Row, out local);
        }

        private static InvalidOperationException BuildMissingPositionException(
            int fromLevel,
            int fromColumn,
            int fromRow,
            int toLevel,
            int toColumn,
            int toRow) =>
            new(
                "无法从货位坐标索引解析堆垛机移动距离，请检查仓库数据是否已加载。" +
                $" 缺失坐标：({fromLevel},{fromColumn},{fromRow}) 或 ({toLevel},{toColumn},{toRow})。");
    }
}
