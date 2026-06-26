using UnityEngine;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>堆垛机交互点允许的货物流向。</summary>
    public enum SimStackerInteractionMode
    {
        /// <summary>入库输送终点与出库输送起点均可使用。</summary>
        [InspectorName("出入库皆可")]
        Both = 0,

        /// <summary>仅作为入库输送终点（堆垛机取货上架）。</summary>
        [InspectorName("只能入库")]
        InboundOnly = 1,

        /// <summary>仅作为出库输送起点（堆垛机放货发运）。</summary>
        [InspectorName("只能出库")]
        OutboundOnly = 2,
    }
}
