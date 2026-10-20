using UnityEngine;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>堆垛机交互点允许的货物流向。</summary>
    public enum SimStackerInteractionMode
    {
        /// <summary>同一交互点既可做入库交接（输送终点）也可做出库交接（输送起点）。</summary>
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
