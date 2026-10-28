using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>
    /// 货位局部坐标索引（与 WarehouseManager .dat 一致）。
    /// 列/排对应哪个 Pos 分量因仓库而异，由 <see cref="SlotLocalAxisMapping"/> 探测。
    /// </summary>
    public interface ISlotPositionIndex
    {
        bool IsReady { get; }

        bool TryGetLocalPosition(GridIndex slot, out Vector3 local);

        /// <summary>
        /// .dat 是否有该货位的精确条目（不含轴向合成）。缺 bin 的货位不可再分配入库。
        /// </summary>
        bool HasExactLocalPosition(GridIndex slot);

        /// <summary>
        /// 按层/列/排轴向坐标合成位置；交互点等排除区货位无精确条目时仍可解析。
        /// </summary>
        bool TryGetAxisPosition(int level, int column, int row, out Vector3 local);
    }
}
