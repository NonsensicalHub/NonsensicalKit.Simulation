using System;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>从路段时刻表推导路口驶入 / 等待 / 驶出子任务时间窗（与仿真预约、回放一致）。</summary>
    public static class JunctionSubTaskTiming
    {
        private const double TimeEpsilon = 1e-6;

        public static bool TryResolveWindows(
            in ConveyorSegmentScheduleEntry junctionSeg,
            ConveyorSegmentScheduleEntry? nextSeg,
            ConveyorMapTopology topology,
            WarehouseConveyorMap map,
            float hop,
            out double enterStart,
            out double enterEnd,
            out double waitStart,
            out double waitEnd,
            out double exitMoveStart,
            out double exitEnd)
        {
            enterStart = enterEnd = waitStart = waitEnd = exitMoveStart = exitEnd = 0;
            var stops = junctionSeg.StopArriveSimTimes;
            if (stops == null || stops.Length == 0 || hop <= 1e-9f)
            {
                return false;
            }

            if (topology == null
                || junctionSeg.ToNodeIndex < 0
                || junctionSeg.ToNodeIndex >= topology.Map.Nodes.Length
                || topology.GetNode(junctionSeg.ToNodeIndex).Kind != SimConveyorNodeKind.Junction)
            {
                return false;
            }

            exitEnd = ResolveTransitEnd(junctionSeg, nextSeg);

            var approachHop = hop;
            if (topology.TryGetEdge(junctionSeg.FromNodeIndex, junctionSeg.ToNodeIndex, out var junctionEdge))
            {
                approachHop = ConveyorMapMath.GetNodeApproachHopSeconds(map, junctionEdge);
                if (approachHop <= 1e-9f)
                {
                    approachHop = hop;
                }
            }

            var nextHop = approachHop;
            if (nextSeg.HasValue
                && topology.TryGetEdge(
                    junctionSeg.ToNodeIndex,
                    nextSeg.Value.ToNodeIndex,
                    out var nextEdge))
            {
                nextHop = ConveyorMapMath.GetEdgeTerminalHopSeconds(map, nextEdge);
                if (nextHop <= 1e-9f)
                {
                    nextHop = approachHop;
                }
            }

            // 驶入路口：从路段 ExitSimTime（末停留点）再经端部留白距离至路口中心。
            enterStart = junctionSeg.ExitSimTime;
            enterEnd = enterStart + approachHop;

            // 驶出移动最早可开始时刻：到达路口中心即可驶出；仅当下一段入口停留点被占时推迟。
            exitMoveStart = enterEnd;
            if (nextSeg.HasValue)
            {
                var nextStops = nextSeg.Value.StopArriveSimTimes;
                if (nextStops != null && nextStops.Length > 0)
                {
                    var nextEntryArrive = nextStops[nextStops.Length - 1];
                    exitMoveStart = Math.Max(exitMoveStart, nextEntryArrive - nextHop);
                }
            }

            waitStart = enterEnd;
            waitEnd = exitMoveStart;
            exitEnd = Math.Max(exitEnd, exitMoveStart + nextHop);
            return exitEnd > enterStart + TimeEpsilon;
        }

        /// <summary>路口停留点互斥占用窗（驶入 hop + 路口等待；驶出 hop 占用下一段入口停留点）。</summary>
        public static bool TryGetJunctionHoldWindow(
            in ConveyorSegmentScheduleEntry junctionSeg,
            ConveyorSegmentScheduleEntry? nextSeg,
            ConveyorMapTopology topology,
            WarehouseConveyorMap map,
            float hop,
            out double holdStart,
            out double holdEnd)
        {
            holdStart = holdEnd = 0;
            if (!TryResolveWindows(
                    junctionSeg,
                    nextSeg,
                    topology,
                    map,
                    hop,
                    out var enterStart,
                    out _,
                    out _,
                    out _,
                    out var exitMoveStart,
                    out _))
            {
                return false;
            }

            holdStart = enterStart;
            holdEnd = exitMoveStart;
            return holdEnd > holdStart + TimeEpsilon;
        }

        /// <summary>规划阶段估算路口占用尾端（驶入结束 + 路口等待；驶出 hop 不计入路口占用）。</summary>
        public static double EstimateHoldEndAfterEnter(
            double downstreamEnter,
            float hop,
            int junctionNodeIndex,
            int nextNodeIndex,
            ConveyorMapTopology topology,
            WarehouseConveyorMap map,
            double nextEntryArriveSimTime = -1,
            float nextHop = -1)
        {
            var exitMoveStart = downstreamEnter;
            if (nextNodeIndex >= 0
                && topology != null
                && topology.TryGetEdge(junctionNodeIndex, nextNodeIndex, out var nextEdge))
            {
                if (nextHop <= 1e-9f)
                {
                    nextHop = ConveyorMapMath.GetEdgeTerminalHopSeconds(map, nextEdge);
                    if (nextHop <= 1e-9f)
                    {
                        nextHop = hop;
                    }
                }

                if (nextEntryArriveSimTime > downstreamEnter + 1e-9)
                {
                    exitMoveStart = Math.Max(exitMoveStart, nextEntryArriveSimTime - nextHop);
                }
            }

            return exitMoveStart;
        }
        public static double GetJunctionEnterHopStart(in ConveyorSegmentScheduleEntry junctionSeg, float hop) =>
            junctionSeg.ExitSimTime;

        /// <summary>
        /// 进入路口后、下一段入口停留点仍被占用导致在路口中心等待（驶出起点晚于到达中心时刻）。
        /// </summary>
        public static bool HasJunctionWait(
            double s0Arrive,
            double enterEnd,
            double exitMoveStart,
            float hop)
        {
            return exitMoveStart > enterEnd + TimeEpsilon;
        }

        /// <summary>驶出子任务起点：无额外等待时紧接驶入结束，有等待时从可驶出时刻开始。</summary>
        public static double GetExitSubTaskStart(
            double s0Arrive,
            double enterEnd,
            double exitMoveStart,
            float hop) =>
            HasJunctionWait(s0Arrive, enterEnd, exitMoveStart, hop) ? exitMoveStart : enterEnd;

        public static double ResolveTransitEnd(
            in ConveyorSegmentScheduleEntry segment,
            ConveyorSegmentScheduleEntry? nextSegment)
        {
            var transitEnd = segment.OccupancyEndSimTime;
            if (!nextSegment.HasValue)
            {
                return transitEnd;
            }

            var next = nextSegment.Value;
            var nextStops = next.StopArriveSimTimes;
            if (nextStops != null && nextStops.Length > 0)
            {
                return Math.Max(transitEnd, nextStops[nextStops.Length - 1]);
            }

            return Math.Max(transitEnd, next.EntrySimTime);
        }
    }
}
