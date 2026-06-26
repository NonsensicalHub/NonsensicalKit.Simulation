using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>堆垛机 fleet 拓扑（台数、列域）；正式部署由仓库布局模块提供。</summary>
    public interface IStackerFleetDescriptor
    {
        int StackerCount { get; }
        SimStackerColumnReach DefaultStackerColumnReach { get; }
        SimStackerDefinition[] StackerDefinitions { get; }
        IStackerKinematics DefaultStackerKinematics { get; }
    }
}
