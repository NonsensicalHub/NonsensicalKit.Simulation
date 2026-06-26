using System;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>入库口驶离插值窗口（与 <see cref="SimSubTaskKind.InfeedMove"/> 子任务对齐）。</summary>
    public readonly struct InfeedDepartPlaybackHints
    {
        private const double TimeEpsilon = 1e-6;

        public readonly double MoveStartSimTime;
        public readonly double MoveEndSimTime;
        public readonly Vector3? AnchorWorld;

        public InfeedDepartPlaybackHints(double moveStartSimTime, double moveEndSimTime, Vector3? anchorWorld)
        {
            MoveStartSimTime = moveStartSimTime;
            MoveEndSimTime = moveEndSimTime;
            AnchorWorld = anchorWorld;
        }

        public bool HasWindow => MoveEndSimTime > MoveStartSimTime + TimeEpsilon;
    }

    /// <summary>按仿真路段预约时刻与 ZPA 停留点，计算料箱在路段上的世界坐标。</summary>
    public static partial class ConveyorPlaybackPosition
    {
        private const double TimeEpsilon = 1e-6;

        public static bool TryEvaluate(
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            Vector3 fromWorld,
            Vector3 toWorld,
            in ConveyorSegmentScheduleEntry segment,
            double simTime,
            out Vector3 worldPosition)
            => TryEvaluate(
                map,
                topology,
                fromWorld,
                toWorld,
                segment,
                simTime,
                prevSegment: null,
                prevFromWorld: null,
                prevToWorld: null,
                nextSegment: null,
                nextFromWorld: null,
                nextToWorld: null,
                out worldPosition,
                infeedDepart: null);

        public static bool TryEvaluate(
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            Vector3 fromWorld,
            Vector3 toWorld,
            in ConveyorSegmentScheduleEntry segment,
            double simTime,
            ConveyorSegmentScheduleEntry? nextSegment,
            Vector3? nextFromWorld,
            Vector3? nextToWorld,
            out Vector3 worldPosition)
            => TryEvaluate(
                map,
                topology,
                fromWorld,
                toWorld,
                segment,
                simTime,
                prevSegment: null,
                prevFromWorld: null,
                prevToWorld: null,
                nextSegment: nextSegment,
                nextFromWorld: nextFromWorld,
                nextToWorld: nextToWorld,
                out worldPosition,
                infeedDepart: null);

        public static bool TryEvaluate(
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            Vector3 fromWorld,
            Vector3 toWorld,
            in ConveyorSegmentScheduleEntry segment,
            double simTime,
            ConveyorSegmentScheduleEntry? prevSegment,
            Vector3? prevFromWorld,
            Vector3? prevToWorld,
            ConveyorSegmentScheduleEntry? nextSegment,
            Vector3? nextFromWorld,
            Vector3? nextToWorld,
            out Vector3 worldPosition,
            InfeedDepartPlaybackHints? infeedDepart = null)
        {
            worldPosition = fromWorld;
            if (map == null || topology == null)
            {
                return false;
            }

            var edgeDist = Vector3.Distance(fromWorld, toWorld);
            if (edgeDist <= 1e-4f)
            {
                worldPosition = toWorld;
                return true;
            }

            var fromIsJunction = segment.FromNodeIndex >= 0
                                 && segment.FromNodeIndex < topology.Map.Nodes.Length
                                 && topology.GetNode(segment.FromNodeIndex).Kind == SimConveyorNodeKind.Junction;

            if (fromIsJunction
                && prevSegment.HasValue
                && prevFromWorld.HasValue
                && prevToWorld.HasValue
                && prevSegment.Value.ToNodeIndex == segment.FromNodeIndex
                && TryGetEntryStopArrive(segment, out var entryArrive)
                && simTime < entryArrive - TimeEpsilon
                && TryEvaluateJunctionTransit(
                    map,
                    topology,
                    prevFromWorld.Value,
                    prevToWorld.Value,
                    prevSegment.Value,
                    simTime,
                    segment,
                    fromWorld,
                    toWorld,
                    out worldPosition))
            {
                return true;
            }

            var hopSeconds = GetEdgeHopSeconds(map, topology, segment);
            if (TryEvaluateInfeedDepart(
                    topology,
                    segment,
                    hopSeconds,
                    simTime,
                    fromWorld,
                    toWorld,
                    infeedDepart,
                    out var infeedDepartPos))
            {
                worldPosition = infeedDepartPos;
                return true;
            }

            if (!IsFromInfeed(topology, segment)
                && simTime < segment.EntrySimTime - TimeEpsilon)
            {
                worldPosition = fromWorld;
                return true;
            }

            if (segment.StopArriveSimTimes != null && segment.StopArriveSimTimes.Length > 0)
            {
                return TryEvaluateZpaOnEdge(
                    map,
                    topology,
                    fromWorld,
                    toWorld,
                    segment,
                    simTime,
                    nextSegment,
                    nextFromWorld,
                    nextToWorld,
                    infeedDepart,
                    out worldPosition);
            }

            if (simTime < segment.ExitSimTime - TimeEpsilon)
            {
                var span = segment.ExitSimTime - segment.EntrySimTime;
                var travel = span > TimeEpsilon
                    ? (float)((simTime - segment.EntrySimTime) / span)
                    : 1f;
                worldPosition = Vector3.Lerp(fromWorld, toWorld, Mathf.Clamp01(travel));
                return true;
            }

            worldPosition = toWorld;
            return true;
        }

        private static Vector3 SampleStopPoint(Vector3 fromWorld, Vector3 toWorld, int capacity, int slotIndex)
        {
            var t = ConveyorMapMath.GetZoneNormalizedPosition(capacity, slotIndex);
            return Vector3.Lerp(fromWorld, toWorld, t);
        }

        private static float GetEdgeHopSeconds(
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            in ConveyorSegmentScheduleEntry segment)
        {
            if (topology.TryGetEdge(segment.FromNodeIndex, segment.ToNodeIndex, out var edge))
            {
                return ConveyorMapMath.GetZoneHopSeconds(map, edge);
            }

            return 0f;
        }

        private static bool IsFromInfeed(
            ConveyorMapTopology topology,
            in ConveyorSegmentScheduleEntry segment)
        {
            return segment.FromNodeIndex >= 0
                   && segment.FromNodeIndex < topology.Map.Nodes.Length
                   && topology.GetNode(segment.FromNodeIndex).Kind == SimConveyorNodeKind.InfeedPort;
        }
    }
}
