using System;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>货位网格索引（仓库、层、列、排、深）。0 号库与旧数据兼容。</summary>
    [Serializable]
    public struct GridIndex : IEquatable<GridIndex>
    {
        public int Level;
        public int Column;
        public int Row;
        public int Depth;

        /// <summary>所属仓库。0 为默认库，旧回放数据反序列化后也是 0。</summary>
        public int WarehouseId;

        public GridIndex(int level, int column, int row, int depth = 0, int warehouseId = 0)
        {
            Level = level;
            Column = column;
            Row = row;
            Depth = depth;
            WarehouseId = warehouseId;
        }

        public bool Equals(GridIndex other) =>
            WarehouseId == other.WarehouseId
            && Level == other.Level
            && Column == other.Column
            && Row == other.Row
            && Depth == other.Depth;

        public override bool Equals(object obj) => obj is GridIndex other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(HashCode.Combine(Level, Column, Row, Depth), WarehouseId);

        public override string ToString() =>
            WarehouseId == 0
                ? $"L{Level} C{Column} R{Row} D{Depth}"
                : $"W{WarehouseId} L{Level} C{Column} R{Row} D{Depth}";
    }
}
