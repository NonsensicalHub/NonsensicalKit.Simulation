using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>
    /// 货位局部坐标轴向：不同 .dat 可能把「排」写在 X 或 Z。
    /// 堆垛机关节约定：Level=层高，Row=巷道行走，Fork=伸叉（列向）。
    /// </summary>
    public readonly struct SlotLocalAxisMapping
    {
        /// <summary>0=X 1=Y 2=Z</summary>
        public readonly int LevelAxis;

        /// <summary>0=X 1=Y 2=Z</summary>
        public readonly int RowAxis;

        /// <summary>0=X 1=Y 2=Z（列向 / 伸叉）</summary>
        public readonly int ForkAxis;

        public SlotLocalAxisMapping(int levelAxis, int rowAxis, int forkAxis)
        {
            LevelAxis = levelAxis;
            RowAxis = rowAxis;
            ForkAxis = forkAxis;
        }

        /// <summary>
        /// 与 DigitalTwin WarehouseLayoutAxisConfig 默认一致：排→X、层→Y、列→Z。
        /// 双库 StackerWarehouse / StackerWarehouse1 即此约定。
        /// </summary>
        public static SlotLocalAxisMapping DefaultProduction { get; } = new(1, 0, 2);

        /// <summary>SimulationTest 等旧样本：排→Z、层→Y、列→X。</summary>
        public static SlotLocalAxisMapping LegacyTest { get; } = new(1, 2, 0);

        public float LevelOf(in Vector3 local) => local[LevelAxis];

        public float RowOf(in Vector3 local) => local[RowAxis];

        public float ForkOf(in Vector3 local) => local[ForkAxis];
    }

    /// <summary>按货位索引缓存轴向探测结果。</summary>
    public static class SlotLocalAxisMappingCache
    {
        private static readonly ConditionalWeakTable<ISlotPositionIndex, Holder> Cache = new();

        private sealed class Holder
        {
            public bool Ready;
            public SlotLocalAxisMapping Mapping;
        }

        public static SlotLocalAxisMapping Resolve(ISlotPositionIndex index)
        {
            if (index == null)
            {
                return SlotLocalAxisMapping.DefaultProduction;
            }

            if (index is SlotPositionIndex concrete && concrete.HasAxisMapping)
            {
                return concrete.AxisMapping;
            }

            var holder = Cache.GetOrCreateValue(index);
            if (!holder.Ready)
            {
                holder.Mapping = SlotLocalAxisDetector.Detect(index);
                holder.Ready = true;
            }

            return holder.Mapping;
        }
    }

    /// <summary>用「只变排 / 只变列 / 只变层」的货位对判定 Pos 分量。</summary>
    public static class SlotLocalAxisDetector
    {
        private const float MinDelta = 1e-4f;

        public static SlotLocalAxisMapping Detect(ISlotPositionIndex index)
        {
            if (index == null || !index.IsReady)
            {
                return SlotLocalAxisMapping.DefaultProduction;
            }

            if (!TryFindAxis(index, AxisKind.Row, out var rowAxis)
                || !TryFindAxis(index, AxisKind.Column, out var forkAxis))
            {
                return SlotLocalAxisMapping.DefaultProduction;
            }

            var levelAxis = TryFindAxis(index, AxisKind.Level, out var detectedLevel)
                ? detectedLevel
                : RemainingAxis(rowAxis, forkAxis, preferred: 1);

            if (rowAxis == forkAxis || rowAxis == levelAxis || forkAxis == levelAxis)
            {
                return SlotLocalAxisMapping.DefaultProduction;
            }

            return new SlotLocalAxisMapping(levelAxis, rowAxis, forkAxis);
        }

        public static SlotLocalAxisMapping DetectFromPositions(
            IReadOnlyList<GridIndex> slots,
            IReadOnlyList<Vector3> positions)
        {
            if (slots == null || positions == null || slots.Count != positions.Count || slots.Count < 2)
            {
                return SlotLocalAxisMapping.DefaultProduction;
            }

            if (!TryFindAxisFromPairs(slots, positions, AxisKind.Row, out var rowAxis)
                || !TryFindAxisFromPairs(slots, positions, AxisKind.Column, out var forkAxis))
            {
                return SlotLocalAxisMapping.DefaultProduction;
            }

            var levelAxis = TryFindAxisFromPairs(slots, positions, AxisKind.Level, out var detectedLevel)
                ? detectedLevel
                : RemainingAxis(rowAxis, forkAxis, preferred: 1);

            if (rowAxis == forkAxis || rowAxis == levelAxis || forkAxis == levelAxis)
            {
                return SlotLocalAxisMapping.DefaultProduction;
            }

            return new SlotLocalAxisMapping(levelAxis, rowAxis, forkAxis);
        }

        private enum AxisKind
        {
            Row,
            Column,
            Level,
        }

        private static int RemainingAxis(int a, int b, int preferred)
        {
            for (var i = 0; i < 3; i++)
            {
                if (i != a && i != b)
                {
                    return i;
                }
            }

            return preferred;
        }

        private static int DominantAxis(in Vector3 delta)
        {
            var ax = Mathf.Abs(delta.x);
            var ay = Mathf.Abs(delta.y);
            var az = Mathf.Abs(delta.z);
            if (ax >= ay && ax >= az)
            {
                return 0;
            }

            return ay >= az ? 1 : 2;
        }

        private static bool TryFindAxis(ISlotPositionIndex index, AxisKind kind, out int axis)
        {
            axis = 0;
            const int scan = 16;
            Vector3? anchor = null;
            var anchorLevel = 0;
            var anchorColumn = 0;
            var anchorRow = 0;

            for (var i = 0; i < scan; i++)
            for (var j = 0; j < scan; j++)
            {
                int level;
                int column;
                int row;
                switch (kind)
                {
                    case AxisKind.Row:
                        level = 0;
                        column = i;
                        row = j;
                        break;
                    case AxisKind.Column:
                        level = 0;
                        column = j;
                        row = i;
                        break;
                    default:
                        level = j;
                        column = i;
                        row = 0;
                        break;
                }

                if (!TryGet(index, level, column, row, out var pos))
                {
                    continue;
                }

                if (anchor == null)
                {
                    anchor = pos;
                    anchorLevel = level;
                    anchorColumn = column;
                    anchorRow = row;
                    continue;
                }

                var matched = kind switch
                {
                    AxisKind.Row => column == anchorColumn && level == anchorLevel && row != anchorRow,
                    AxisKind.Column => row == anchorRow && level == anchorLevel && column != anchorColumn,
                    _ => row == anchorRow && column == anchorColumn && level != anchorLevel,
                };
                if (!matched)
                {
                    continue;
                }

                var delta = pos - anchor.Value;
                if (Mathf.Abs(delta.x) < MinDelta
                    && Mathf.Abs(delta.y) < MinDelta
                    && Mathf.Abs(delta.z) < MinDelta)
                {
                    continue;
                }

                axis = DominantAxis(delta);
                return true;
            }

            return false;
        }

        private static bool TryFindAxisFromPairs(
            IReadOnlyList<GridIndex> slots,
            IReadOnlyList<Vector3> positions,
            AxisKind kind,
            out int axis)
        {
            axis = 0;
            for (var i = 0; i < slots.Count; i++)
            {
                var a = slots[i];
                var pa = positions[i];
                for (var j = i + 1; j < slots.Count; j++)
                {
                    var b = slots[j];
                    var matched = kind switch
                    {
                        AxisKind.Row => a.Column == b.Column && a.Level == b.Level && a.Row != b.Row,
                        AxisKind.Column => a.Row == b.Row && a.Level == b.Level && a.Column != b.Column,
                        _ => a.Row == b.Row && a.Column == b.Column && a.Level != b.Level,
                    };
                    if (!matched)
                    {
                        continue;
                    }

                    var delta = positions[j] - pa;
                    if (Mathf.Abs(delta.x) < MinDelta
                        && Mathf.Abs(delta.y) < MinDelta
                        && Mathf.Abs(delta.z) < MinDelta)
                    {
                        continue;
                    }

                    axis = DominantAxis(delta);
                    return true;
                }
            }

            return false;
        }

        private static bool TryGet(ISlotPositionIndex index, int level, int column, int row, out Vector3 pos)
        {
            if (index.TryGetLocalPosition(new GridIndex(level, column, row), out pos))
            {
                return true;
            }

            return index.TryGetAxisPosition(level, column, row, out pos);
        }
    }
}
