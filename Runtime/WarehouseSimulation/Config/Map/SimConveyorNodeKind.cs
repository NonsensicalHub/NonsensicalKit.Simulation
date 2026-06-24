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
    }
}
