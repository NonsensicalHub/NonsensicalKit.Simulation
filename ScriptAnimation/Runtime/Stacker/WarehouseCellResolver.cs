using NonsensicalKit.Core;
using NonsensicalKit.DigitalTwin.Warehouse;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 通过 <see cref="WarehouseManager"/> 将货位坐标（层/列/排/深）解析为世界位置。
    /// </summary>
    public static class WarehouseCellResolver
    {
        public static bool TryGetWorldPosition(
            WarehouseManager warehouse,
            Int4 cell,
            out Vector3 worldPos)
        {
            worldPos = default;
            if (warehouse == null)
                return false;

            RuntimeBinData bin = warehouse.GetRuntimeBinData(cell);
            if (bin == null)
                return false;

            worldPos = bin.Pos;
            return true;
        }
    }
}
