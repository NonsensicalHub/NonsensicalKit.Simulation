using System;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>货位网格索引（层、列、排、深），与 NonsensicalKit Int4 语义一致。</summary>
    [Serializable]
    public struct GridIndex : IEquatable<GridIndex>
    {
        public int Level;
        public int Column;
        public int Row;
        public int Depth;

        public GridIndex(int level, int column, int row, int depth = 0)
        {
            Level = level;
            Column = column;
            Row = row;
            Depth = depth;
        }

        public bool Equals(GridIndex other) =>
            Level == other.Level && Column == other.Column && Row == other.Row && Depth == other.Depth;

        public override bool Equals(object obj) => obj is GridIndex other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Level, Column, Row, Depth);

        public override string ToString() => $"L{Level} C{Column} R{Row} D{Depth}";
    }
}
