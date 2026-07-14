using System;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;
using NonsensicalKit.Simulation.WarehouseSimulation.Simulation;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    public static partial class ConveyorPlaybackPosition
    {
        /// <summary>
        /// 入库口驶离时刻：与 <see cref="StackerWarehouseSimulator.RecordInfeedDepartMoveSubTask"/> 一致。
        /// </summary>
        private static bool TryEvaluateInfeedDepart(
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            in ConveyorSegmentScheduleEntry segment,
            double simTime,
            Vector3 fromWorld,
            Vector3 toWorld,
            InfeedDepartPlaybackHints? infeedDepart,
            out Vector3 worldPosition)
        {
            worldPosition = fromWorld;
            if (!IsFromInfeed(topology, segment) || simTime >= segment.EntrySimTime - TimeEpsilon)
            {
                return false;
            }

            var anchor = infeedDepart?.AnchorWorld ?? fromWorld;
            var entryHop = segment.StopArriveSimTimes != null && segment.StopArriveSimTimes.Length > 0
                ? GetSlotHopSeconds(map, topology, segment, segment.StopArriveSimTimes.Length - 1)
                : GetEdgeHopSeconds(map, topology, segment);

            if (segment.StopArriveSimTimes == null || segment.StopArriveSimTimes.Length == 0)
            {
                if (!TryResolveInfeedDepartWindow(segment, entryHop, infeedDepart, out var moveStart, out var moveEnd))
                {
                    return false;
                }

                if (simTime < moveStart - TimeEpsilon)
                {
                    worldPosition = anchor;
                    return true;
                }

                if (simTime >= moveEnd - TimeEpsilon)
                {
                    return false;
                }

                var span = moveEnd - moveStart;
                var progress = span > TimeEpsilon
                    ? (float)((simTime - moveStart) / span)
                    : 1f;
                worldPosition = Vector3.Lerp(anchor, toWorld, Mathf.Clamp01(progress));
                return true;
            }

            return TrySampleInfeedDepartOnEdge(
                map,
                topology,
                segment,
                simTime,
                fromWorld,
                toWorld,
                infeedDepart,
                out worldPosition);
        }

        private static bool TrySampleInfeedDepartOnEdge(
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            in ConveyorSegmentScheduleEntry segment,
            double simTime,
            Vector3 fromWorld,
            Vector3 toWorld,
            InfeedDepartPlaybackHints? infeedDepart,
            out Vector3 worldPosition)
        {
            worldPosition = fromWorld;
            var stops = segment.StopArriveSimTimes;
            if (stops == null || stops.Length == 0)
            {
                return false;
            }

            var entryHop = GetSlotHopSeconds(map, topology, segment, stops.Length - 1);
            if (!TryResolveInfeedDepartWindow(segment, entryHop, infeedDepart, out var moveStart, out var moveEnd))
            {
                return false;
            }

            if (simTime < moveStart - TimeEpsilon)
            {
                return false;
            }

            if (simTime >= moveEnd - TimeEpsilon)
            {
                return false;
            }

            var entryStop = stops.Length - 1;
            var stopWorld = SampleStopPoint(map, topology, fromWorld, toWorld, segment, entryStop);
            var anchor = infeedDepart?.AnchorWorld ?? fromWorld;
            var span = moveEnd - moveStart;
            var progress = span > TimeEpsilon
                ? (float)((simTime - moveStart) / span)
                : 1f;
            worldPosition = Vector3.Lerp(anchor, stopWorld, Mathf.Clamp01(progress));
            return true;
        }

        private static bool TryResolveInfeedDepartWindow(
            in ConveyorSegmentScheduleEntry segment,
            float hop,
            InfeedDepartPlaybackHints? infeedDepart,
            out double moveStart,
            out double moveEnd)
        {
            if (infeedDepart.HasValue && infeedDepart.Value.HasWindow)
            {
                moveStart = infeedDepart.Value.MoveStartSimTime;
                moveEnd = infeedDepart.Value.MoveEndSimTime;
                return true;
            }

            var stops = segment.StopArriveSimTimes;
            moveEnd = segment.EntrySimTime;
            if (stops != null && stops.Length > 0)
            {
                moveEnd = stops[stops.Length - 1];
            }

            moveStart = hop > 1e-9f ? moveEnd - hop : moveEnd;
            moveStart = Math.Max(moveStart, segment.EntrySimTime);
            return moveEnd > moveStart + TimeEpsilon;
        }
    }
}
