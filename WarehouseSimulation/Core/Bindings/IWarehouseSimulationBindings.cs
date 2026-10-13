using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>
    /// 仿真运行时所需的仓库/设备绑定数据（地图、堆垛机、资源策略）。
    /// 核心模块只依赖此契约；默认实现由 <see cref="DefaultWarehouseSimulationBindingsAsset"/> 提供。
    /// </summary>
    public interface IWarehouseSimulationBindings
        : IConveyorMapSource, IStackerFleetDescriptor, ISimResourcePolicy
    {
        /// <summary>货位局部坐标索引（与 WarehouseManager .dat 一致）。多库时按货位仓库号选择。</summary>
        ISlotPositionIndex SlotPositions { get; }

        /// <summary>在主线程预加载货位坐标（后台仿真启动前调用）。</summary>
        void EnsureSlotPositionsLoaded();

        /// <summary>
        /// 全部货架。为空时仍使用绑定资产上的仓库数据名，以及场景 Runner 上的网格，作为唯一的 0 号库。
        /// </summary>
        SimWarehouseDefinition[] Warehouses { get; }
    }
}
