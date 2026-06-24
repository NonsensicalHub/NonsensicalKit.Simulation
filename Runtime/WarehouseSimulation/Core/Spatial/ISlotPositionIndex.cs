using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>
    /// 货位局部坐标索引（与 WarehouseManager .dat 一致：PosX=列、PosY=层、PosZ=排）。
    /// </summary>
    public interface ISlotPositionIndex
    {
        bool IsReady { get; }

        bool TryGetLocalPosition(GridIndex slot, out Vector3 local);

        /// <summary>
        /// 按层/列/排轴向坐标合成位置；交互点等排除区货位无精确条目时仍可解析。
        /// </summary>
        bool TryGetAxisPosition(int level, int column, int row, out Vector3 local);
    }
}
