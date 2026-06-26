using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>
    /// 货位网格到堆垛机三轴目标值的映射（与 .dat / 场景一致：PosX=列、PosY=层、PosZ=排）。
    /// </summary>
    public static class StackerAxisValueResolver
    {
        public readonly struct StackerAxisValues
        {
            public readonly float Level;
            public readonly float Row;
            public readonly float Fork;

            public StackerAxisValues(float level, float row, float fork)
            {
                Level = level;
                Row = row;
                Fork = fork;
            }
        }

        /// <summary>层向 + 排向；货叉收回到轨道列（<paramref name="railColumn"/>）。</summary>
        public static bool TryResolveCarriageValues(
            ISlotPositionIndex index,
            int railColumn,
            GridIndex slot,
            out StackerAxisValues values)
        {
            values = default;
            if (index == null)
            {
                return false;
            }

            // 同一层同一轨道列上，层向 Y 与排向 Z、货叉收回 X 均取自 (level, railColumn, row)。
            if (!TryGetExactSlotPosition(index, slot.Level, railColumn, slot.Row, out var carriageRef))
            {
                return false;
            }

            values = new StackerAxisValues(carriageRef.y, carriageRef.z, carriageRef.x);
            return true;
        }

        /// <summary>三轴全到位，货叉伸到目标列（<see cref="GridIndex.Column"/>）。</summary>
        public static bool TryResolveAbsoluteValues(
            ISlotPositionIndex index,
            int railColumn,
            GridIndex slot,
            out StackerAxisValues values)
        {
            values = default;
            if (index == null)
            {
                return false;
            }

            if (!TryResolveCarriageValues(index, railColumn, slot, out values))
            {
                return false;
            }

            if (!TryGetExactSlotPosition(index, slot.Level, slot.Column, slot.Row, out var forkRef))
            {
                return false;
            }

            values = new StackerAxisValues(values.Level, values.Row, forkRef.x);
            return true;
        }

        public static bool TryResolveOrigin(
            ISlotPositionIndex index,
            int railColumn,
            out StackerAxisValues origin)
        {
            origin = default;
            if (index == null)
            {
                return false;
            }

            if (TryGetExactSlotPosition(index, 0, railColumn, 0, out var pos))
            {
                origin = new StackerAxisValues(pos.y, pos.z, pos.x);
                return true;
            }

            // 原点行可能未录入 .dat：在同列第 0 层任一排上取层/排，货叉 X 仍取轨道列。
            if (!TryGetAnyRowOnLevelColumn(index, 0, railColumn, out var anyRow, out var anyPos))
            {
                return false;
            }

            if (!TryGetExactSlotPosition(index, 0, railColumn, anyRow, out var railPos))
            {
                railPos = anyPos;
            }

            origin = new StackerAxisValues(anyPos.y, anyPos.z, railPos.x);
            return true;
        }

        private static bool TryGetExactSlotPosition(
            ISlotPositionIndex index,
            int level,
            int column,
            int row,
            out Vector3 local)
        {
            local = default;
            if (index == null)
            {
                return false;
            }

            if (index.TryGetLocalPosition(new GridIndex(level, column, row), out local))
            {
                return true;
            }

            if (index is not SlotPositionIndex concrete)
            {
                return false;
            }

            if (concrete.TryGetAxisPosition(level, column, row, out local))
            {
                return true;
            }

            // 取货点/轨道列可能未录入 row=0：回退到同层同列任意排（与 TryResolveOrigin 一致）。
            return concrete.TryGetFirstRowOnLevelColumn(level, column, out _, out local);
        }

        private static bool TryGetAnyRowOnLevelColumn(
            ISlotPositionIndex index,
            int level,
            int column,
            out int row,
            out Vector3 local)
        {
            row = 0;
            local = default;
            if (index is not SlotPositionIndex concrete)
            {
                return false;
            }

            return concrete.TryGetFirstRowOnLevelColumn(level, column, out row, out local);
        }
    }
}
