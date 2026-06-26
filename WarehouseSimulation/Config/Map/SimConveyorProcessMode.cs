using UnityEngine;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>输送加工站点的行为模式（按种类区分，不写死具体设备名称）。</summary>
    public enum SimConveyorProcessMode
    {
        /// <summary>进入后停留一段时间完成加工（如缠膜机）。</summary>
        [InspectorName("停留加工")]
        Dwell = 0,
    }
}
