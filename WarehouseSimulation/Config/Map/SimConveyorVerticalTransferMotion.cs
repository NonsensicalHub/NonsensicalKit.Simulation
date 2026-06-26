using UnityEngine;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>垂直提升机跨层移动的默认回放插值模式。</summary>
    public enum SimConveyorVerticalTransferMotion
    {
        [InspectorName("垂直插值（保持水平位置）")]
        LinearVertical = 0,

        [InspectorName("三维直线插值")]
        LinearFull = 1,
    }
}
