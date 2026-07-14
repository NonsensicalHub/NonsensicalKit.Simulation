using NonsensicalKit.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>
    /// 仿真 <see cref="GridIndex"/> 与数字孪生 <see cref="Int4"/> 的索引映射。
    /// WarehouseManager 约定：x=层，y=列，z=排，w=深。
    /// </summary>
    internal static class WarehouseGridIndexMapping
    {
        public static Int4 ToInt4(in GridIndex slot) =>
            new Int4(slot.Level, slot.Column, slot.Row, slot.Depth);

        public static GridIndex ToGridIndex(in Int4 location) =>
            new GridIndex(location.I1, location.I2, location.I3, location.I4);
    }
}
