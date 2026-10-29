using UnityEngine;
using NaughtyAttributes;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>仿真策略配置：放置、到达、入库口选择与输送寻路等（与硬件参数分离）。</summary>
    [CreateAssetMenu(fileName = "WarehouseSimStrategyProfile", menuName = "Warehouse Simulation/Strategy Profile")]
    public class WarehouseSimStrategyProfile : ScriptableObject
    {
        [Header("堆垛机货位")]
        [Label("放置策略")]
        public StackerSlotPlacementStrategy StackerPlacementStrategy = StackerSlotPlacementStrategy.NearestToPickup;

        [Header("入 / 出库口")]
        [Label("端口选择策略")]
        [Tooltip("多入库口或多出库口时，下一箱货物分配到哪个端口（入库与出库共用同一策略）。")]
        public InfeedPortSelectionStrategy InfeedPortSelectionStrategy = InfeedPortSelectionStrategy.RoundRobin;

        [Header("输送 / 堆垛机交互点")]
        [Label("输送路径策略")]
        public ConveyorRoutingStrategy ConveyorRoutingStrategy = ConveyorRoutingStrategy.ShortestDistance;
    }
}
