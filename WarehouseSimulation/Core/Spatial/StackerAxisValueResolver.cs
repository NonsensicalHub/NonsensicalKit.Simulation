using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>
    /// 货位网格到堆垛机三轴目标值的映射。
    /// 轴向按 <see cref="SlotLocalAxisMapping"/> 从 .dat 自动探测（双库：排→X、层→Y、列→Z；
    /// SimulationTest：排→Z、层→Y、列→X）。
    /// 货叉「收回」取巷道中心（巷道左列与右邻列中点）；交互点伸叉 = 中心 + 配置 ForkDepthMeters 原样；
    /// 货位伸叉按目标列 Absolute。避免把收回位当成左列货位坐标。
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

        /// <summary>层向 + 排向；货叉收回到巷道中心（非巷道左列货位）。</summary>
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

            if (!TryGetExactSlotPosition(index, slot.Level, railColumn, slot.Row, out var carriageRef))
            {
                return false;
            }

            var axes = SlotLocalAxisMappingCache.Resolve(index);
            var fork = axes.ForkOf(carriageRef);
            if (TryResolveAisleCenterFork(index, axes, slot.Level, railColumn, slot.Row, out var centerFork))
            {
                fork = centerFork;
            }

            values = new StackerAxisValues(
                axes.LevelOf(carriageRef),
                axes.RowOf(carriageRef),
                fork);
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

            var axes = SlotLocalAxisMappingCache.Resolve(index);
            values = new StackerAxisValues(values.Level, values.Row, axes.ForkOf(forkRef));
            return true;
        }

        /// <summary>
        /// 交互点货叉：巷道中心 + 配置的 ForkDepthMeters 原样（关节正方向，不做符号翻正）。
        /// </summary>
        public static bool TryResolvePickupForkValues(
            ISlotPositionIndex index,
            int railColumn,
            int level,
            int row,
            int aisleLeftColumn,
            float forkDepthMeters,
            out StackerAxisValues values)
        {
            values = default;
            if (!TryResolveCarriageValues(index, railColumn, new GridIndex(level, railColumn, row), out var carriage))
            {
                return false;
            }

            var center = carriage.Fork;
            var axes = SlotLocalAxisMappingCache.Resolve(index);
            if (TryResolveAisleCenterFork(index, axes, level, aisleLeftColumn, row, out var centerFork))
            {
                center = centerFork;
            }

            // 配置偏移直接加到巷道中心；正负由配置/关节正方向表达侧向，脚本不翻符号。
            values = new StackerAxisValues(
                carriage.Level,
                carriage.Row,
                center + forkDepthMeters);
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

            // 原点货叉取巷道中心（与 Carriage 收回位一致），避免 ZeroState 停在左列货位上。
            // 优先 (L0,rail,R0)；无精确/合成条目时，排向必须外推到 row=0，
            // 禁止把「最小存在排」的 Row 分量当成原点——那会少走一整排距
            // （表现为停在第 N 排附近却在交互第 N+1 排）。
            if (TryResolveCarriageValues(index, railColumn, new GridIndex(0, railColumn, 0), out origin))
            {
                return true;
            }

            var axes = SlotLocalAxisMappingCache.Resolve(index);
            if (!TryResolveRowZeroCoordinate(index, axes, out var row0))
            {
                // 无法外推时再试 row=1 合成；仍失败则放弃。
                return TryResolveCarriageValues(index, railColumn, new GridIndex(0, railColumn, 1), out origin);
            }

            // Level/Fork 参考点：轨道列上任意存在排；没有则用 row=1 轴向合成。
            var anyRow = 0;
            Vector3 anyPos;
            if (TryGetAnyRowOnLevelColumn(index, 0, railColumn, out anyRow, out anyPos))
            {
                // ok
            }
            else if (!TryGetExactSlotPosition(index, 0, railColumn, 1, out anyPos))
            {
                return false;
            }
            else
            {
                anyRow = 1;
            }

            if (!TryGetExactSlotPosition(index, 0, railColumn, anyRow, out var railPos))
            {
                railPos = anyPos;
            }

            var fork = axes.ForkOf(railPos);
            if (TryResolveAisleCenterFork(index, axes, 0, railColumn, anyRow, out var centerFork))
            {
                fork = centerFork;
            }

            // Level/Fork 取参考点，Row 强制为外推的 row=0。
            origin = new StackerAxisValues(
                axes.LevelOf(railPos),
                row0,
                fork);
            return true;
        }

        /// <summary>row=0 的排向轴坐标：有精确分量用精确，否则按排距外推。</summary>
        private static bool TryResolveRowZeroCoordinate(
            ISlotPositionIndex index,
            in SlotLocalAxisMapping axes,
            out float row0)
        {
            row0 = 0f;
            if (index is SlotPositionIndex concrete
                && concrete.TryGetRowAxisCoordinate(0, out row0))
            {
                return true;
            }

            // 目录/包装索引：用 (任意列, row0/1/2) 轴向合成反推。
            for (var column = 0; column < 64; column++)
            {
                if (index.TryGetAxisPosition(0, column, 0, out var at0))
                {
                    row0 = axes.RowOf(at0);
                    return true;
                }
            }

            if (index.TryGetAxisPosition(0, 0, 1, out var at1)
                && index.TryGetAxisPosition(0, 0, 2, out var at2))
            {
                var pitch = axes.RowOf(at2) - axes.RowOf(at1);
                row0 = axes.RowOf(at1) - pitch;
                return Mathf.Abs(pitch) > 1e-5f;
            }

            return false;
        }

        /// <summary>
        /// 巷道中心货叉分量：优先左列与右邻列中点。
        /// 缺右邻列时，用附近相邻列对估计半列距，从左列向列号增大侧偏移，避免静默退回左列货位（那会把收回当成伸到左浅位）。
        /// </summary>
        private static bool TryResolveAisleCenterFork(
            ISlotPositionIndex index,
            in SlotLocalAxisMapping axes,
            int level,
            int aisleLeftColumn,
            int row,
            out float centerFork)
        {
            centerFork = 0f;
            if (TryGetForkComponent(index, axes, level, aisleLeftColumn, row, out var leftFork)
                && TryGetForkComponent(index, axes, level, aisleLeftColumn + 1, row, out var rightFork))
            {
                centerFork = (leftFork + rightFork) * 0.5f;
                return true;
            }

            if (!TryGetForkComponent(index, axes, level, aisleLeftColumn, row, out leftFork))
            {
                return false;
            }

            if (!TryEstimateHalfColumnForkPitchTowardHigher(
                    index, axes, level, aisleLeftColumn, row, out var halfPitchTowardHigher))
            {
                return false;
            }

            centerFork = leftFork + halfPitchTowardHigher;
            return true;
        }

        /// <summary>估计「半个列距」的货叉分量，方向为列号增大。</summary>
        private static bool TryEstimateHalfColumnForkPitchTowardHigher(
            ISlotPositionIndex index,
            in SlotLocalAxisMapping axes,
            int level,
            int aisleLeftColumn,
            int row,
            out float halfPitchTowardHigher)
        {
            halfPitchTowardHigher = 0f;
            // 优先 (aisleLeft-1, aisleLeft)：两者都在左排货架时，差值即列距。
            if (TryGetForkComponent(index, axes, level, aisleLeftColumn - 1, row, out var leftMinus)
                && TryGetForkComponent(index, axes, level, aisleLeftColumn, row, out var left))
            {
                var delta = left - leftMinus;
                if (Mathf.Abs(delta) > 1e-4f)
                {
                    halfPitchTowardHigher = delta * 0.5f;
                    return true;
                }
            }

            // 再扫同层任意相邻列对。
            for (var c = aisleLeftColumn - 8; c <= aisleLeftColumn + 8; c++)
            {
                if (c == aisleLeftColumn)
                {
                    continue;
                }

                if (!TryGetForkComponent(index, axes, level, c, row, out var a)
                    || !TryGetForkComponent(index, axes, level, c + 1, row, out var b))
                {
                    continue;
                }

                var delta = b - a;
                if (Mathf.Abs(delta) > 1e-4f)
                {
                    halfPitchTowardHigher = delta * 0.5f;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetForkComponent(
            ISlotPositionIndex index,
            in SlotLocalAxisMapping axes,
            int level,
            int column,
            int row,
            out float fork)
        {
            fork = 0f;
            if (!TryGetExactSlotPosition(index, level, column, row, out var position))
            {
                return false;
            }

            fork = axes.ForkOf(position);
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

            // 轨道列/指定排可能无完整货位：按轴向分量合成，必须保留请求排号的 Row 分量。
            // 禁止回退到「同层同列任意排」整点——那会把行走 X 悄悄换成别的排，
            // 在 RowSign=-1 时镜像到无货位处。
            if (index.TryGetAxisPosition(level, column, row, out local))
            {
                return true;
            }

            return false;
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
