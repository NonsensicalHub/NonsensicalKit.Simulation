using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>输送地图数据源；正式部署由对接模块提供。</summary>
    public interface IConveyorMapSource
    {
        WarehouseConveyorMap ConveyorMap { get; }
    }
}
