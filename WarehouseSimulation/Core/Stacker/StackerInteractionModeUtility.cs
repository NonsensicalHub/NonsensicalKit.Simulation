using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>堆垛机交互点出入库能力判定。</summary>
    public static class StackerInteractionModeUtility
    {
        public static bool AllowsInbound(in SimConveyorMapNode node) =>
            node.Kind != SimConveyorNodeKind.PickupPoint
            || node.StackerInteractionMode == SimStackerInteractionMode.Both
            || node.StackerInteractionMode == SimStackerInteractionMode.InboundOnly;

        public static bool AllowsOutbound(in SimConveyorMapNode node) =>
            node.Kind != SimConveyorNodeKind.PickupPoint
            || node.StackerInteractionMode == SimStackerInteractionMode.Both
            || node.StackerInteractionMode == SimStackerInteractionMode.OutboundOnly;
    }
}
