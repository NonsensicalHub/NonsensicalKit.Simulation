using System;
using NonsensicalKit.Simulation.WarehouseSimulation.Core.Graph;
using NonsensicalKit.Simulation.WarehouseSimulation.Simulation.Routing;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core.Calendar
{
    /// <summary>
    /// 从阶段1图 / 路线步骤派生稳定资源 ID。
    /// 直通默认不生成 ProcessStation 预约 ID（见 <see cref="RouteStepResourceDemand"/>）。
    /// </summary>
    public static class ResourceIds
    {
        public static string ConveyorZone(string physicalSegmentId, int zoneIndex) =>
            $"zone:{physicalSegmentId}:{zoneIndex}";

        public static string DirectionLock(string physicalSegmentId) =>
            $"dirlock:{physicalSegmentId}";

        public static string JunctionMovement(string nodeId, string entryPortId, string exitPortId) =>
            $"juncmov:{nodeId}:{entryPortId}>{exitPortId}";

        public static string MovementConflictGroup(string groupId) =>
            $"conflict:{groupId}";

        public static string ProcessStation(string nodeId) =>
            $"process:{nodeId}";

        public static string StationTraversal(string nodeId) =>
            $"traverse:{nodeId}";

        public static string StationExclusiveWithService(string nodeId) =>
            $"stex:{nodeId}";

        public static string BufferSlot(string bufferId, int slotIndex) =>
            $"buffer:{bufferId}:{slotIndex}";

        public static string Port(string nodeId) =>
            $"port:{nodeId}";

        public static string TransferDevice(string groupOrNodeId) =>
            $"xfer:{groupOrNodeId}";

        public static string ArcOccupancy(string arcId) =>
            $"arc:{arcId}";

        /// <summary>由弧 From→To 推断 Forward/Reverse（相对物理段字典序较小端为 Forward 起点）。</summary>
        public static TravelDirection DirectionForArc(ConveyorArc arc)
        {
            if (arc == null || string.IsNullOrEmpty(arc.FromNodeId) || string.IsNullOrEmpty(arc.ToNodeId))
            {
                return TravelDirection.None;
            }

            var cmp = string.CompareOrdinal(arc.FromNodeId, arc.ToNodeId);
            // 较小 NodeId → 较大 NodeId 视为 Forward；反向为 Reverse。
            return cmp <= 0 ? TravelDirection.Forward : TravelDirection.Reverse;
        }
    }

    /// <summary>从 RouteStep 生成资源需求（不预约；供 Kernel/阶段3 使用）。</summary>
    public static class RouteStepResourceDemand
    {
        /// <summary>
        /// 为单个步骤列出应预约的资源规格（相对锚点的 offset=0，duration=名义成本）。
        /// Process 占 ProcessStation；PassThrough 默认不占加工服务，仅按策略占 Traversal/Exclusive。
        /// </summary>
        public static void AppendDemandsForStep(
            ConveyorGraph graph,
            RouteStep step,
            System.Collections.Generic.List<ResourceWindowRequest> into)
        {
            if (step == null || into == null)
            {
                return;
            }

            var duration = Math.Max(0.0, step.NominalCostSeconds);
            switch (step.Kind)
            {
                case RouteStepKind.Move:
                case RouteStepKind.PassThrough:
                    if (!string.IsNullOrEmpty(step.PhysicalSegmentId))
                    {
                        into.Add(ResourceWindowRequest.Create(
                            ResourceIds.DirectionLock(step.PhysicalSegmentId),
                            duration,
                            offsetFromAnchor: 0,
                            units: 1,
                            direction: ResolveMoveDirection(graph, step)));
                        into.Add(ResourceWindowRequest.Create(
                            ResourceIds.ConveyorZone(step.PhysicalSegmentId, 0),
                            duration,
                            0,
                            1));
                    }

                    if (!string.IsNullOrEmpty(step.ArcId))
                    {
                        into.Add(ResourceWindowRequest.Create(
                            ResourceIds.ArcOccupancy(step.ArcId),
                            duration,
                            0,
                            1));
                    }

                    if (!string.IsNullOrEmpty(step.FromNodeId)
                        && !string.IsNullOrEmpty(step.EntryPortId)
                        && !string.IsNullOrEmpty(step.ExitPortId))
                    {
                        into.Add(ResourceWindowRequest.Create(
                            ResourceIds.JunctionMovement(step.FromNodeId, step.EntryPortId, step.ExitPortId),
                            duration,
                            0,
                            1));
                    }

                    AppendPassThroughStationDemands(graph, step, duration, into);
                    break;

                case RouteStepKind.Process:
                    into.Add(ResourceWindowRequest.Create(
                        ResourceIds.ProcessStation(step.FromNodeId ?? step.ResourceId),
                        duration,
                        0,
                        1));
                    break;

                case RouteStepKind.Turnaround:
                    if (!string.IsNullOrEmpty(step.PhysicalSegmentId))
                    {
                        // 换向窗口：方向锁仍占用，方向用目标出弧方向。
                        into.Add(ResourceWindowRequest.Create(
                            ResourceIds.DirectionLock(step.PhysicalSegmentId),
                            duration,
                            0,
                            1,
                            ResolveTurnaroundDirection(graph, step)));
                    }

                    break;

                case RouteStepKind.Transfer:
                    into.Add(ResourceWindowRequest.Create(
                        ResourceIds.TransferDevice(step.ResourceId ?? step.FromNodeId),
                        duration,
                        0,
                        1));
                    break;
            }
        }

        private static void AppendPassThroughStationDemands(
            ConveyorGraph graph,
            RouteStep step,
            double duration,
            System.Collections.Generic.List<ResourceWindowRequest> into)
        {
            if (step.Kind != RouteStepKind.PassThrough
                || graph == null
                || !graph.TryGetNode(step.ToNodeId, out var node)
                || node.Kind != ConveyorGraphNodeKind.ProcessStation)
            {
                return;
            }

            // 直通默认不占 ProcessStation。
            switch (node.PassThroughPolicy)
            {
                case StationPassThroughPolicy.TraversalOnly:
                    into.Add(ResourceWindowRequest.Create(
                        ResourceIds.StationTraversal(node.NodeId),
                        duration,
                        0,
                        1));
                    break;
                case StationPassThroughPolicy.ExclusiveWithService:
                    into.Add(ResourceWindowRequest.Create(
                        ResourceIds.StationExclusiveWithService(node.NodeId),
                        duration,
                        0,
                        1));
                    break;
            }
        }

        private static TravelDirection ResolveMoveDirection(ConveyorGraph graph, RouteStep step)
        {
            if (graph != null
                && !string.IsNullOrEmpty(step.ArcId)
                && graph.TryGetArc(step.ArcId, out var arc))
            {
                return ResourceIds.DirectionForArc(arc);
            }

            return TravelDirection.None;
        }

        private static TravelDirection ResolveTurnaroundDirection(ConveyorGraph graph, RouteStep step)
        {
            if (graph != null
                && !string.IsNullOrEmpty(step.ArcId)
                && graph.TryGetArc(step.ArcId, out var arc))
            {
                return ResourceIds.DirectionForArc(arc);
            }

            return TravelDirection.None;
        }
    }
}
