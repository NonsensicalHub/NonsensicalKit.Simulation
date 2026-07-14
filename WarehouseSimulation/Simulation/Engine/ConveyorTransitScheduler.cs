using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>
    /// 输送时间与资源预约计算（零压力积放 ZPA）。
    /// <para>主路径按 zone 链逐步预约（<see cref="ConveyorTransitScheduler.Zones.cs"/>）。</para>
    /// </summary>
    public sealed partial class ConveyorTransitScheduler
    {
        private readonly ConveyorMapTopology _topology;
        private readonly ReservationTable _reservations;
        private readonly IWarehouseSimulationBindings _bindings;
        private readonly StackerCarriageBookkeeper _stackerCarriage;

        public ConveyorTransitScheduler(
            ConveyorMapTopology topology,
            ReservationTable reservations,
            IWarehouseSimulationBindings bindings,
            StackerCarriageBookkeeper stackerCarriage = null)
        {
            _topology = topology;
            _reservations = reservations;
            _bindings = bindings;
            _stackerCarriage = stackerCarriage;
        }

        #region 路口 / 下游约束（zone 链共用）

        private double ComputeJunctionExitHopStart(
            int junctionNodeIndex,
            int nextNodeIndex,
            double downstreamEnter,
            float hop)
        {
            var exitHopStart = downstreamEnter;
            if (nextNodeIndex < 0
                || !_topology.TryGetEdge(junctionNodeIndex, nextNodeIndex, out var nextEdge))
            {
                return exitHopStart;
            }

            var nextHop = ConveyorMapMath.GetEdgeTerminalHopSeconds(_topology.Map, nextEdge);
            if (nextHop <= 1e-9f)
            {
                nextHop = hop;
            }

            var nextCapacity = _topology.Map.GetEdgeCapacity(nextEdge);
            if (nextCapacity <= 0)
            {
                return exitHopStart;
            }

            var nextSlotIds = ConveyorMapMath.BuildSegmentSlotIds(
                _topology.Map,
                junctionNodeIndex,
                nextNodeIndex,
                nextCapacity);
            var nextEntrySlot = nextCapacity - 1;
            if (nextEntrySlot < 0 || nextEntrySlot >= nextSlotIds.Length)
            {
                return exitHopStart;
            }

            var nextEntryHopStart = _reservations.GetEarliestFreeTimeForDuration(
                nextSlotIds[nextEntrySlot],
                downstreamEnter,
                nextHop);
            return Math.Max(exitHopStart, nextEntryHopStart);
        }

        /// <summary>按整段路口占用（非仅驶入 hop）反推可进入时刻。</summary>
        private double ResolveJunctionDownstreamEnter(
            string junctionStopId,
            double s0Arrive,
            float hop,
            int junctionNodeIndex,
            int nextNodeIndex)
        {
            var downstreamEnter = s0Arrive + hop;
            for (var iter = 0; iter < 16; iter++)
            {
                var exitHopStart = ComputeJunctionExitHopStart(
                    junctionNodeIndex,
                    nextNodeIndex,
                    downstreamEnter,
                    hop);
                var holdEnd = exitHopStart;
                var holdDuration = holdEnd - s0Arrive;
                if (holdDuration < hop - 1e-9)
                {
                    holdDuration = hop;
                }

                var holdStart = _reservations.GetEarliestFreeTimeForDuration(
                    junctionStopId,
                    s0Arrive,
                    holdDuration);
                var nextEnter = holdStart + hop;
                if (Math.Abs(nextEnter - downstreamEnter) <= 1e-9)
                {
                    return downstreamEnter;
                }

                downstreamEnter = nextEnter;
            }

            return downstreamEnter;
        }

        /// <summary>按整段取货点占用（驶入 hop + 堆垛作业）反推可进入时刻。</summary>
        private double ResolvePickupDownstreamEnter(
            string pickupZoneId,
            double s0Arrive,
            float hop,
            Func<double, double> dwellResolver)
        {
            var downstreamEnter = s0Arrive + hop;
            for (var iter = 0; iter < 16; iter++)
            {
                var dwell = ResolveDownstreamDwellAfterEnter(downstreamEnter, hop, null, dwellResolver);
                var holdDuration = hop + dwell;
                if (holdDuration <= hop + 1e-9)
                {
                    holdDuration = hop * 2;
                }

                var holdStart = _reservations.GetEarliestFreeTimeForDuration(
                    pickupZoneId,
                    s0Arrive,
                    holdDuration);
                var nextEnter = holdStart + hop;
                if (Math.Abs(nextEnter - downstreamEnter) <= 1e-9)
                {
                    return downstreamEnter;
                }

                downstreamEnter = nextEnter;
            }

            return downstreamEnter;
        }

        /// <summary>按整段出库口 / 加工站 / 提升机占用（驶入 hop + 服务）反推可进入时刻。</summary>
        private double ResolveOutfeedDownstreamEnter(
            string outfeedZoneId,
            double s0Arrive,
            float hop,
            Func<double, double> dwellResolver)
        {
            var downstreamEnter = s0Arrive + hop;
            for (var iter = 0; iter < 16; iter++)
            {
                var dwell = ResolveDownstreamDwellAfterEnter(downstreamEnter, hop, null, dwellResolver);
                var holdDuration = hop + dwell;
                if (holdDuration <= hop + 1e-9)
                {
                    holdDuration = hop * 2;
                }

                var holdStart = _reservations.GetEarliestFreeTimeForDuration(
                    outfeedZoneId,
                    s0Arrive,
                    holdDuration);
                var nextEnter = holdStart + hop;
                if (Math.Abs(nextEnter - downstreamEnter) <= 1e-9)
                {
                    return downstreamEnter;
                }

                downstreamEnter = nextEnter;
            }

            return downstreamEnter;
        }

        private static double ResolveDownstreamDwellAfterEnter(
            double downstreamEnter,
            float hop,
            double? downstreamHoldSeconds,
            Func<double, double> downstreamDwellResolver)
        {
            if (downstreamDwellResolver != null)
            {
                return Math.Max(hop, downstreamDwellResolver(downstreamEnter));
            }

            return downstreamHoldSeconds ?? hop;
        }

        private float ComputeStackerWorkSeconds(WarehouseJob job, in SimConveyorMapNode pickupNode)
        {
            if (_bindings == null)
            {
                return 0f;
            }

            var plan = StackerWorkPlanner.PlanInbound(
                _bindings, _topology, _stackerCarriage, job, pickupNode);
            return Math.Max(0f, plan.TotalSeconds);
        }

        private void CommitInboundStackerBooking(
            WarehouseJob job,
            in SimConveyorMapNode pickupNode,
            double stackerEnd)
        {
            if (_stackerCarriage == null)
            {
                return;
            }

            var plan = StackerWorkPlanner.PlanInbound(
                _bindings, _topology, _stackerCarriage, job, pickupNode);
            _stackerCarriage.CommitBooking(
                pickupNode.StackerId,
                stackerEnd,
                plan.EndRow,
                plan.EndLevel);
        }

        private string[] BuildStackerResourceIds(int stackerId, int targetColumn)
        {
            if (_bindings == null)
            {
                return Array.Empty<string>();
            }

            var list = new List<string>(2);
            if (_bindings.UseStackerReservation)
            {
                list.Add(SimEntityNaming.StackerResourceId(stackerId));
            }

            if (_bindings.UseAisleColumnReservation)
            {
                list.Add($"aisle-col-{targetColumn}");
            }

            return list.ToArray();
        }

        #endregion
    }
}
