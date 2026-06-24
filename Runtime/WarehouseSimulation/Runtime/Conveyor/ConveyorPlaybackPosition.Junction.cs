using System;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    public static partial class ConveyorPlaybackPosition
    {
        /// <summary>
        /// 路口移动：驶入 / 等待 / 驶出三阶段（与 JunctionEnter、JunctionWait、JunctionExit 子任务同窗口）。
        /// </summary>
        private static bool TryEvaluateJunctionTransit(
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
        {
            worldPosition = toWorld;
            if (segment.ToNodeIndex < 0
                || segment.ToNodeIndex >= topology.Map.Nodes.Length
                || topology.GetNode(segment.ToNodeIndex).Kind != SimConveyorNodeKind.Junction)
            {
                return false;
            }

            var hop = GetEdgeHopSeconds(map, topology, segment);
            if (!JunctionSubTaskTiming.TryResolveWindows(
                    segment,
                    nextSegment,
                    topology,
                    map,
                    hop,
                    out var enterStart,
                    out var enterEnd,
                    out _,
                    out _,
                    out var exitMoveStart,
                    out var exitEnd))
            {
                return false;
            }

            if (simTime < enterStart - TimeEpsilon || simTime >= exitEnd - TimeEpsilon)
            {
                return false;
            }

            var stops = segment.StopArriveSimTimes;
            var capacity = stops.Length;
            var stop0 = SampleStopPoint(fromWorld, toWorld, capacity, 0);
            var exitTarget = ResolveJunctionExitTarget(
                map, topology, toWorld, nextSegment, nextFromWorld, nextToWorld);

            if (simTime < enterEnd - TimeEpsilon)
            {
                var span = enterEnd - enterStart;
                var progress = span > TimeEpsilon
                    ? (float)((simTime - enterStart) / span)
                    : 1f;
                worldPosition = Vector3.Lerp(stop0, toWorld, Mathf.Clamp01(progress));
                return true;
            }

            var hasWait = JunctionSubTaskTiming.HasJunctionWait(stops[0], enterEnd, exitMoveStart, hop);
            if (hasWait && simTime < exitMoveStart - TimeEpsilon)
            {
                worldPosition = toWorld;
                return true;
            }

            var outboundStart = hasWait ? exitMoveStart : enterEnd;
            var outboundSpan = exitEnd - outboundStart;
            var outboundProgress = outboundSpan > TimeEpsilon
                ? (float)((simTime - outboundStart) / outboundSpan)
                : 1f;
            worldPosition = Vector3.Lerp(toWorld, exitTarget, Mathf.Clamp01(outboundProgress));
            return true;
        }

        private static bool TryGetEntryStopArrive(
            in ConveyorSegmentScheduleEntry segment,
            out double entryArrive)
        {
            entryArrive = segment.EntrySimTime;
            var stops = segment.StopArriveSimTimes;
            if (stops == null || stops.Length == 0)
            {
                return false;
            }

            entryArrive = stops[stops.Length - 1];
            return true;
        }

        private static Vector3 ResolveJunctionExitTarget(
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            Vector3 junctionWorld,
            ConveyorSegmentScheduleEntry? nextSegment,
            Vector3? nextFromWorld,
            Vector3? nextToWorld)
        {
            if (!nextSegment.HasValue || !nextFromWorld.HasValue || !nextToWorld.HasValue)
            {
                return junctionWorld;
            }

            var nextStops = nextSegment.Value.StopArriveSimTimes;
            if (nextStops != null && nextStops.Length > 0)
            {
                var nextCapacity = nextStops.Length;
                return SampleStopPoint(
                    nextFromWorld.Value,
                    nextToWorld.Value,
                    nextCapacity,
                    nextCapacity - 1);
            }

            var span = nextSegment.Value.ExitSimTime - nextSegment.Value.EntrySimTime;
            if (span > TimeEpsilon)
            {
                var hop = 0f;
                if (topology.TryGetEdge(
                        nextSegment.Value.FromNodeIndex,
                        nextSegment.Value.ToNodeIndex,
                        out var nextEdge))
                {
                    hop = ConveyorMapMath.GetZoneHopSeconds(map, nextEdge);
                }

                if (hop > TimeEpsilon)
                {
                    var travel = Math.Min(1d, hop / span);
                    return Vector3.Lerp(nextFromWorld.Value, nextToWorld.Value, (float)travel);
                }
            }

            return nextFromWorld.Value;
        }
    }
}
