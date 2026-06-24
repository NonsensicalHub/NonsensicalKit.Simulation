using System;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    public static partial class ConveyorPlaybackPosition
    {
        private static bool TryEvaluateZpaOnEdge(
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            Vector3 fromWorld,
            Vector3 toWorld,
            in ConveyorSegmentScheduleEntry segment,
            double simTime,
            ConveyorSegmentScheduleEntry? nextSegment,
            Vector3? nextFromWorld,
            Vector3? nextToWorld,
            InfeedDepartPlaybackHints? infeedDepart,
            out Vector3 worldPosition)
        {
            worldPosition = fromWorld;
            var stops = segment.StopArriveSimTimes;
            var capacity = stops.Length;
            var hop = GetEdgeHopSeconds(map, topology, segment);

            var destIsJunction = segment.ToNodeIndex >= 0
                                 && segment.ToNodeIndex < topology.Map.Nodes.Length
                                 && topology.GetNode(segment.ToNodeIndex).Kind == SimConveyorNodeKind.Junction;

            var junctionEnterHopStart = destIsJunction
                ? JunctionSubTaskTiming.GetJunctionEnterHopStart(segment, hop)
                : double.MaxValue;

            if (destIsJunction
                && simTime >= junctionEnterHopStart - TimeEpsilon
                && TryEvaluateJunctionTransit(
                    map,
                    topology,
                    fromWorld,
                    toWorld,
                    segment,
                    simTime,
                    nextSegment,
                    nextFromWorld,
                    nextToWorld,
                    out worldPosition))
            {
                return true;
            }

            var entryStop = capacity - 1;
            var entryArrive = stops[entryStop];
            var fromIsJunction = segment.FromNodeIndex >= 0
                                 && segment.FromNodeIndex < topology.Map.Nodes.Length
                                 && topology.GetNode(segment.FromNodeIndex).Kind == SimConveyorNodeKind.Junction;
            var fromIsInfeed = IsFromInfeed(topology, segment);
            if (fromIsInfeed
                && TrySampleInfeedDepartOnEdge(
                    segment,
                    hop,
                    simTime,
                    fromWorld,
                    toWorld,
                    infeedDepart,
                    out worldPosition))
            {
                return true;
            }

            var moveInStart = Math.Max(segment.EntrySimTime, entryArrive - hop);

            // 首停留点由非入库口路段 / 路口移动覆盖，避免重复插值造成闪现。
            if (!fromIsJunction
                && !fromIsInfeed
                && simTime >= moveInStart - TimeEpsilon
                && simTime < entryArrive - TimeEpsilon)
            {
                var fromT = 0f;
                var toT = ConveyorMapMath.GetZoneNormalizedPosition(capacity, entryStop);
                var span = hop;
                var progress = span > TimeEpsilon
                    ? (float)((simTime - moveInStart) / span)
                    : 1f;
                worldPosition = Vector3.Lerp(
                    fromWorld, toWorld, Mathf.Lerp(fromT, toT, Mathf.Clamp01(progress)));
                return true;
            }

            for (var s = capacity - 1; s >= 0; s--)
            {
                var arrive = stops[s];
                var hopInStart = s < capacity - 1
                    ? Math.Max(stops[s + 1], arrive - hop)
                    : fromIsInfeed
                        ? arrive
                        : moveInStart;
                if (simTime >= hopInStart - TimeEpsilon && simTime < arrive - TimeEpsilon)
                {
                    var fromT = s < capacity - 1
                        ? ConveyorMapMath.GetZoneNormalizedPosition(capacity, s + 1)
                        : 0f;
                    var toT = ConveyorMapMath.GetZoneNormalizedPosition(capacity, s);
                    var span = arrive - hopInStart;
                    var progress = span > TimeEpsilon
                        ? (float)((simTime - hopInStart) / span)
                        : 1f;
                    worldPosition = Vector3.Lerp(
                        fromWorld, toWorld, Mathf.Lerp(fromT, toT, Mathf.Clamp01(progress)));
                    return true;
                }

                var nextArrive = s > 0 ? stops[s - 1] : segment.ExitSimTime;
                var moveOutStart = nextArrive - hop;

                if (simTime >= arrive - TimeEpsilon && simTime <= moveOutStart + TimeEpsilon)
                {
                    worldPosition = SampleStopPoint(fromWorld, toWorld, capacity, s);
                    return true;
                }

                if (!destIsJunction
                    && simTime >= moveOutStart - TimeEpsilon
                    && simTime < nextArrive - TimeEpsilon)
                {
                    var fromT = ConveyorMapMath.GetZoneNormalizedPosition(capacity, s);
                    var toT = s > 0
                        ? ConveyorMapMath.GetZoneNormalizedPosition(capacity, s - 1)
                        : 1f;
                    var span = hop;
                    var progress = span > TimeEpsilon
                        ? (float)((simTime - moveOutStart) / span)
                        : 1f;
                    worldPosition = Vector3.Lerp(
                        fromWorld, toWorld, Mathf.Lerp(fromT, toT, Mathf.Clamp01(progress)));
                    return true;
                }
            }

            worldPosition = toWorld;
            return true;
        }
    }
}
