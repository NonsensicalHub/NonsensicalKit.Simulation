using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>将节点路径展开为按行进顺序的 zone 链（路段槽位 → 路口/取货 zone）。</summary>
    internal static class ConveyorPathZoneChainBuilder
    {
        public static List<ConveyorPathZone> Build(
            IReadOnlyList<int> pathNodeIndices,
            ConveyorMapTopology topology,
            WarehouseConveyorMap map)
        {
            var chain = new List<ConveyorPathZone>();
            if (pathNodeIndices == null || pathNodeIndices.Count < 2 || topology == null)
            {
                return chain;
            }

            for (var edgeIndex = 0; edgeIndex < pathNodeIndices.Count - 1; edgeIndex++)
            {
                var from = pathNodeIndices[edgeIndex];
                var to = pathNodeIndices[edgeIndex + 1];
                if (!topology.TryGetEdge(from, to, out var edge))
                {
                    chain.Clear();
                    return chain;
                }

                ref var toNode = ref topology.GetNode(to);
                var capacity = topology.Map.GetEdgeCapacity(edge);
                var slotIds = ConveyorMapMath.BuildSegmentSlotIds(map, from, to, capacity);
                var nodeApproachHop = ConveyorMapMath.GetNodeApproachHopSeconds(map, edge);

                for (var s = capacity - 1; s >= 0; s--)
                {
                    chain.Add(new ConveyorPathZone
                    {
                        Kind = ConveyorPathZoneKind.EdgeSlot,
                        ResourceId = slotIds[s],
                        PathEdgeIndex = edgeIndex,
                        FromNodeIndex = from,
                        ToNodeIndex = to,
                        SlotIndex = s,
                        HopSeconds = ConveyorMapMath.GetZoneHopSecondsFromPrevious(map, edge, s),
                    });
                }

                if (toNode.Kind == SimConveyorNodeKind.Junction)
                {
                    var nextNode = edgeIndex + 2 < pathNodeIndices.Count
                        ? pathNodeIndices[edgeIndex + 2]
                        : -1;
                    chain.Add(new ConveyorPathZone
                    {
                        Kind = ConveyorPathZoneKind.Junction,
                        ResourceId = topology.GetJunctionZoneResourceId(to),
                        PathEdgeIndex = edgeIndex,
                        FromNodeIndex = from,
                        ToNodeIndex = to,
                        SlotIndex = -1,
                        HopSeconds = nodeApproachHop,
                        JunctionNextNodeIndex = nextNode,
                    });
                }
                else if (toNode.Kind == SimConveyorNodeKind.PickupPoint)
                {
                    chain.Add(new ConveyorPathZone
                    {
                        Kind = ConveyorPathZoneKind.Pickup,
                        ResourceId = SimEntityNaming.PickupResourceId(toNode, to),
                        PathEdgeIndex = edgeIndex,
                        FromNodeIndex = from,
                        ToNodeIndex = to,
                        SlotIndex = -1,
                        HopSeconds = nodeApproachHop,
                    });
                }
                else if (toNode.Kind == SimConveyorNodeKind.OutfeedPort)
                {
                    chain.Add(new ConveyorPathZone
                    {
                        Kind = ConveyorPathZoneKind.Outfeed,
                        ResourceId = SimEntityNaming.OutfeedResourceId(toNode, to),
                        PathEdgeIndex = edgeIndex,
                        FromNodeIndex = from,
                        ToNodeIndex = to,
                        SlotIndex = -1,
                        HopSeconds = nodeApproachHop,
                    });
                }
                else if (toNode.Kind == SimConveyorNodeKind.ProcessStation
                         && ConveyorProcessStationUtility.NodeIsDwellProcessStation(in toNode))
                {
                    chain.Add(new ConveyorPathZone
                    {
                        Kind = ConveyorPathZoneKind.ProcessStation,
                        ResourceId = SimEntityNaming.ProcessStationZoneResourceId(toNode, to),
                        PathEdgeIndex = edgeIndex,
                        FromNodeIndex = from,
                        ToNodeIndex = to,
                        SlotIndex = -1,
                        HopSeconds = nodeApproachHop,
                    });
                }
                else if (toNode.Kind == SimConveyorNodeKind.VerticalTransfer)
                {
                    chain.Add(new ConveyorPathZone
                    {
                        Kind = ConveyorPathZoneKind.VerticalTransfer,
                        ResourceId = SimEntityNaming.VerticalTransferZoneResourceId(toNode, to),
                        PathEdgeIndex = edgeIndex,
                        FromNodeIndex = from,
                        ToNodeIndex = to,
                        SlotIndex = -1,
                        HopSeconds = nodeApproachHop,
                    });
                }
            }

            return chain;
        }
    }
}
