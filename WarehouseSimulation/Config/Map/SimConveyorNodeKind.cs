using UnityEngine;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    public enum SimConveyorNodeKind
    {
        /// <summary>合流 / 十字路口，通过时占用互斥资源。</summary>
        [InspectorName("路口")]
        Junction = 0,

        /// <summary>入库口：到货、上料；仿真起点。</summary>
        [InspectorName("入库口")]
        InfeedPort = 1,

        /// <summary>堆垛机交互点：入库时为输送终点；出库时为输送起点。</summary>
        [InspectorName("堆垛机交互点")]
        PickupPoint = 2,

        /// <summary>出库口：出库流程输送终点，对外发运。</summary>
        [InspectorName("出库口")]
        OutfeedPort = 3,

        /// <summary>输送加工站点（缠膜机等）；具体行为由 <see cref="SimConveyorProcessMode"/> 区分。</summary>
        [InspectorName("加工站点")]
        ProcessStation = 4,

        /// <summary>垂直提升机：跨层移动节点；输送逻辑与普通节点相同，提升时间与动画可单独配置。</summary>
        [InspectorName("垂直提升机")]
        VerticalTransfer = 5,
    }
}
