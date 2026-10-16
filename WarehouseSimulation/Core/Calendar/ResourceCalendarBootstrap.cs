using System;
using NonsensicalKit.Simulation.WarehouseSimulation.Core.Graph;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core.Calendar
{
    /// <summary>从阶段1 <see cref="ConveyorGraph"/> 注册默认资源骨架（区段/方向锁/站点等）。</summary>
    public static class ResourceCalendarBootstrap
    {
        public sealed class Options
        {
            public int DefaultZoneCapacity = 1;
            public int DefaultDirectionLockSameWayCapacity = 8;
            public int DefaultProcessStationCapacity = 1;
            public int ZonesPerSegment = 1;
        }

        public static void RegisterFromGraph(ResourceCalendar calendar, ConveyorGraph graph, Options options = null)
        {
            if (calendar == null || graph == null)
            {
                return;
            }

            options ??= new Options();
            var zones = Math.Max(1, options.ZonesPerSegment);

            foreach (var seg in graph.PhysicalSegments.Values)
            {
                var dirCap = seg.FlowMode == ConveyorFlowMode.SharedReversibleLane
                    ? Math.Max(1, options.DefaultDirectionLockSameWayCapacity)
                    : Math.Max(1, options.DefaultDirectionLockSameWayCapacity);

                calendar.EnsureRegistered(new ResourceDefinition
                {
                    ResourceId = ResourceIds.DirectionLock(seg.PhysicalSegmentId),
                    Kind = ResourceKind.DirectionLock,
                    Capacity = dirCap,
                });

                for (var z = 0; z < zones; z++)
                {
                    calendar.EnsureRegistered(new ResourceDefinition
                    {
                        ResourceId = ResourceIds.ConveyorZone(seg.PhysicalSegmentId, z),
                        Kind = ResourceKind.ConveyorZone,
                        Capacity = Math.Max(1, options.DefaultZoneCapacity),
                    });
                }
            }

            foreach (var arc in graph.Arcs.Values)
            {
                calendar.EnsureRegistered(new ResourceDefinition
                {
                    ResourceId = ResourceIds.ArcOccupancy(arc.ArcId),
                    Kind = ResourceKind.ConveyorZone,
                    Capacity = Math.Max(1, options.DefaultZoneCapacity),
                });
            }

            foreach (var node in graph.Nodes.Values)
            {
                switch (node.Kind)
                {
                    case ConveyorGraphNodeKind.ProcessStation:
                        calendar.EnsureRegistered(new ResourceDefinition
                        {
                            ResourceId = ResourceIds.ProcessStation(node.NodeId),
                            Kind = ResourceKind.ProcessStation,
                            Capacity = Math.Max(1, options.DefaultProcessStationCapacity),
                        });
                        if (node.PassThroughPolicy == StationPassThroughPolicy.TraversalOnly)
                        {
                            calendar.EnsureRegistered(new ResourceDefinition
                            {
                                ResourceId = ResourceIds.StationTraversal(node.NodeId),
                                Kind = ResourceKind.StationTraversal,
                                Capacity = 1,
                            });
                        }
                        else if (node.PassThroughPolicy == StationPassThroughPolicy.ExclusiveWithService)
                        {
                            calendar.EnsureRegistered(new ResourceDefinition
                            {
                                ResourceId = ResourceIds.StationExclusiveWithService(node.NodeId),
                                Kind = ResourceKind.StationExclusiveWithService,
                                Capacity = 1,
                            });
                        }

                        break;
                    case ConveyorGraphNodeKind.InfeedPort:
                    case ConveyorGraphNodeKind.OutfeedPort:
                        calendar.EnsureRegistered(new ResourceDefinition
                        {
                            ResourceId = ResourceIds.Port(node.NodeId),
                            Kind = ResourceKind.Port,
                            Capacity = 1,
                        });
                        break;
                    case ConveyorGraphNodeKind.VerticalTransfer:
                        calendar.EnsureRegistered(new ResourceDefinition
                        {
                            ResourceId = ResourceIds.TransferDevice(node.NodeId),
                            Kind = ResourceKind.TransferDevice,
                            Capacity = 1,
                        });
                        break;
                }
            }
        }
    }
}
