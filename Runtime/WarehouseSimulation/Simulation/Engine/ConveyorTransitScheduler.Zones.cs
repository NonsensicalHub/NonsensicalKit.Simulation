using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    public sealed partial class ConveyorTransitScheduler
    {
        /// <summary>预约路径上的单个 zone，并累计等待/服务时间到 job。</summary>
        public ConveyorPathZoneReservation TryReservePathZone(
            WarehouseJob job,
            IReadOnlyList<ConveyorPathZone> chain,
            int zoneIndex,
            double desiredStart)
        {
            var result = new ConveyorPathZoneReservation { Success = false };
            if (chain == null || zoneIndex < 0 || zoneIndex >= chain.Count)
            {
                return result;
            }

            var zone = chain[zoneIndex];
            ConveyorPathZone? next = zoneIndex + 1 < chain.Count ? chain[zoneIndex + 1] : null;

            if (zone.Kind == ConveyorPathZoneKind.Junction)
            {
                return TryReserveJunctionPathZone(job, chain, zoneIndex, zone, next, desiredStart, result);
            }

            if (zoneIndex > 0
                && chain[zoneIndex - 1].Kind == ConveyorPathZoneKind.Junction
                && zone.Kind == ConveyorPathZoneKind.EdgeSlot)
            {
                ReleaseProvisionalNextEntryAfterJunction(chain, zoneIndex - 1, zoneIndex);
            }

            if (zone.HopSeconds <= 1e-9f)
            {
                var immediate = desiredStart;
                zone.ArriveSimTime = immediate;
                zone.LeaveSimTime = immediate;
                zone.DesiredArriveSimTime = desiredStart;
                result.Success = true;
                result.Zone = zone;
                result.NextStepStartTime = immediate;
                return result;
            }

            var hop = zone.HopSeconds;
            var hopStart = _reservations.GetEarliestFreeTimeForDuration(zone.ResourceId, desiredStart, hop);
            var arrive = hopStart + hop;
            // 下游空闲时 leave == arrive，货箱到达停留点后立即开始下一段 hop，不强制额外停留。
            var leave = ComputeZoneLeaveTime(job, zone, next, arrive, hop, chain, zoneIndex);

            for (var iter = 0; iter < 128; iter++)
            {
                var reserveDuration = Math.Max(hop, leave - hopStart);
                var conflictFreeStart = _reservations.GetEarliestFreeTimeForDuration(
                    zone.ResourceId,
                    hopStart,
                    reserveDuration);
                if (conflictFreeStart <= hopStart + 1e-9)
                {
                    break;
                }

                hopStart = conflictFreeStart;
                arrive = hopStart + hop;
                leave = ComputeZoneLeaveTime(job, zone, next, arrive, hop, chain, zoneIndex);
            }

            _reservations.TryReserve(zone.ResourceId, hopStart, leave, out _);

            zone.DesiredArriveSimTime = desiredStart;
            zone.ArriveSimTime = arrive;
            zone.LeaveSimTime = leave;

            var mutations = new TransitMutationAccumulator();
            var entryWait = hopStart - desiredStart;
            if (entryWait > 1e-9 && zone.Kind == ConveyorPathZoneKind.EdgeSlot)
            {
                mutations.ApplySegmentMetrics(new SegmentMetrics(
                    serviceDelta: 0,
                    entryWaitDelta: entryWait,
                    entryWaitResourceId: zone.ResourceId,
                    downstreamWaitDelta: 0,
                    downstreamWaitResourceId: null));
            }

            if (zone.Kind == ConveyorPathZoneKind.Pickup && job != null)
            {
                // 出库：货箱已由堆垛机放至取货点，不再按入库语义预约整段堆垛作业。
                if (job.Direction != SimFlowDirection.Outbound || job.PickupCompleteSimTime <= 0)
                {
                    ApplyPickupStackerReserve(
                        job,
                        zone.ToNodeIndex,
                        arrive,
                        zone.ResourceId,
                        zone.LeaveSimTime,
                        mutations);
                }
            }
            ConveyorTransitApplier.ApplyToJob(job, mutations);

            result.Success = true;
            result.Zone = zone;
            result.NextStepStartTime = leave;

            if (zoneIndex == 0
                && zone.Kind == ConveyorPathZoneKind.EdgeSlot
                && _topology.TryGetEdge(zone.FromNodeIndex, zone.ToNodeIndex, out var firstEdge))
            {
                result.InfeedPhysicalReleaseSimTime = arrive
                    + ConveyorMapMath.GetCargoTailClearanceSeconds(_topology.Map, firstEdge);
            }

            return result;
        }

        /// <summary>路口 zone：按整段占用窗（驶入 + 等待）迭代预约，与 bulk 路径 <see cref="ReserveJunctionStopHold"/> 一致。</summary>
        private ConveyorPathZoneReservation TryReserveJunctionPathZone(
            WarehouseJob job,
            IReadOnlyList<ConveyorPathZone> chain,
            int zoneIndex,
            ConveyorPathZone zone,
            ConveyorPathZone? next,
            double desiredStart,
            ConveyorPathZoneReservation result)
        {
            var hop = zone.HopSeconds;
            var hopStart = _reservations.GetEarliestFreeTimeForDuration(zone.ResourceId, desiredStart, hop);
            var arrive = hopStart + hop;
            double leave;

            if (next.HasValue && next.Value.Kind == ConveyorPathZoneKind.EdgeSlot)
            {
                for (var iter = 0; iter < 16; iter++)
                {
                    leave = ComputeJunctionExitHopStart(
                        zone.ToNodeIndex,
                        zone.JunctionNextNodeIndex,
                        arrive,
                        hop);
                    var holdStart = arrive - hop;
                    var holdDuration = Math.Max(leave - holdStart, hop);
                    var conflictFree = _reservations.GetEarliestFreeTimeForDuration(
                        zone.ResourceId,
                        holdStart,
                        holdDuration);
                    if (conflictFree <= holdStart + 1e-9)
                    {
                        hopStart = holdStart;
                        break;
                    }

                    hopStart = conflictFree;
                    arrive = hopStart + hop;
                }

                leave = ComputeJunctionExitHopStart(
                    zone.ToNodeIndex,
                    zone.JunctionNextNodeIndex,
                    arrive,
                    hop);
                var provisionalNextArrive = ProvisionalReserveNextEntryAfterJunction(
                    zone,
                    next.Value,
                    arrive,
                    leave);
                if (job?.ConveyorPathZones != null
                    && zoneIndex + 1 < job.ConveyorPathZones.Count
                    && provisionalNextArrive > 0)
                {
                    var nextEntry = job.ConveyorPathZones[zoneIndex + 1];
                    nextEntry.ArriveSimTime = provisionalNextArrive;
                    job.ConveyorPathZones[zoneIndex + 1] = nextEntry;
                }
            }
            else
            {
                leave = arrive + hop;
                for (var iter = 0; iter < 128; iter++)
                {
                    var reserveDuration = Math.Max(hop, leave - hopStart);
                    var conflictFreeStart = _reservations.GetEarliestFreeTimeForDuration(
                        zone.ResourceId,
                        hopStart,
                        reserveDuration);
                    if (conflictFreeStart <= hopStart + 1e-9)
                    {
                        break;
                    }

                    hopStart = conflictFreeStart;
                    arrive = hopStart + hop;
                    leave = arrive + hop;
                }
            }

            _reservations.TryReserve(zone.ResourceId, hopStart, leave, out _);

            zone.DesiredArriveSimTime = desiredStart;
            zone.ArriveSimTime = arrive;
            zone.LeaveSimTime = leave;

            ConveyorTransitApplier.ApplyToJob(job, new TransitMutationAccumulator());

            result.Success = true;
            result.Zone = zone;
            result.NextStepStartTime = leave;
            return result;
        }

        /// <summary>路口预约时占位下一段入口停留点，避免他车在路口等待尚未结束时抢入。</summary>
        private double ProvisionalReserveNextEntryAfterJunction(
            in ConveyorPathZone junctionZone,
            in ConveyorPathZone nextEntryZone,
            double junctionArrive,
            double junctionLeave)
        {
            var exitHopStart = junctionLeave;
            if (exitHopStart < junctionArrive - 1e-9)
            {
                exitHopStart = junctionArrive;
            }

            var nextHop = nextEntryZone.HopSeconds;
            if (nextHop <= 1e-9f)
            {
                return 0;
            }

            var nextArrive = _reservations.GetEarliestFreeTimeForDuration(
                nextEntryZone.ResourceId,
                junctionArrive,
                nextHop);
            if (nextArrive <= exitHopStart + 1e-9)
            {
                return 0;
            }

            _reservations.TryReserve(nextEntryZone.ResourceId, exitHopStart, nextArrive, out _);
            return nextArrive;
        }

        /// <summary>下一段入口 zone 正式预约前，释放路口阶段对入口停留点的占位。</summary>
        private void ReleaseProvisionalNextEntryAfterJunction(
            IReadOnlyList<ConveyorPathZone> chain,
            int junctionZoneIndex,
            int nextZoneIndex)
        {
            if (junctionZoneIndex < 0
                || nextZoneIndex < 0
                || junctionZoneIndex >= chain.Count
                || nextZoneIndex >= chain.Count)
            {
                return;
            }

            var junction = chain[junctionZoneIndex];
            var next = chain[nextZoneIndex];
            if (junction.Kind != ConveyorPathZoneKind.Junction
                || next.Kind != ConveyorPathZoneKind.EdgeSlot)
            {
                return;
            }

            var exitHopStart = junction.LeaveSimTime;
            if (exitHopStart < junction.ArriveSimTime - 1e-9)
            {
                exitHopStart = junction.ArriveSimTime;
            }

            var provisionalEnd = next.ArriveSimTime;
            if (provisionalEnd <= exitHopStart + 1e-9)
            {
                provisionalEnd = exitHopStart + next.HopSeconds;
            }

            _reservations.Release(next.ResourceId, exitHopStart, provisionalEnd);
        }

        private double ComputeZoneLeaveTime(
            WarehouseJob job,
            ConveyorPathZone zone,
            ConveyorPathZone? next,
            double arrive,
            float hop,
            IReadOnlyList<ConveyorPathZone> chain,
            int zoneIndex)
        {
            if (!next.HasValue)
            {
                return arrive + hop;
            }

            var nextZone = next.Value;
            switch (zone.Kind)
            {
                case ConveyorPathZoneKind.EdgeSlot when nextZone.Kind == ConveyorPathZoneKind.EdgeSlot
                                                      && nextZone.PathEdgeIndex == zone.PathEdgeIndex:
                {
                    var nextArrive = _reservations.GetEarliestFreeTimeForDuration(
                        nextZone.ResourceId,
                        arrive,
                        nextZone.HopSeconds);
                    return nextArrive;
                }

                case ConveyorPathZoneKind.EdgeSlot when nextZone.Kind == ConveyorPathZoneKind.Junction:
                {
                    var junctionEnter = ResolveJunctionDownstreamEnter(
                        nextZone.ResourceId,
                        arrive,
                        hop,
                        nextZone.ToNodeIndex,
                        nextZone.JunctionNextNodeIndex);
                    return junctionEnter - hop;
                }

                case ConveyorPathZoneKind.EdgeSlot when nextZone.Kind == ConveyorPathZoneKind.Pickup:
                {
                    if (job == null)
                    {
                        return arrive + hop;
                    }

                    var pickupEnter = ResolvePickupDownstreamEnter(
                        nextZone.ResourceId,
                        arrive,
                        hop,
                        enter => PickupDwellAfterEnter(job, nextZone.ToNodeIndex, enter));
                    return pickupEnter - hop;
                }

                case ConveyorPathZoneKind.EdgeSlot when nextZone.Kind == ConveyorPathZoneKind.Outfeed:
                {
                    if (job == null)
                    {
                        return arrive + hop;
                    }

                    var outfeedEnter = ResolveOutfeedDownstreamEnter(
                        nextZone.ResourceId,
                        arrive,
                        hop,
                        enter => OutfeedDwellAfterEnter(nextZone.ToNodeIndex, enter));
                    return outfeedEnter - hop;
                }

                case ConveyorPathZoneKind.Junction when nextZone.Kind == ConveyorPathZoneKind.EdgeSlot:
                {
                    return ComputeJunctionExitHopStart(
                        zone.ToNodeIndex,
                        zone.JunctionNextNodeIndex,
                        arrive,
                        hop);
                }

                default:
                    return arrive + hop;
            }
        }

        /// <summary>将一条路径边上的 zone 时刻写入路段 schedule（供回放/自检）。</summary>
        public static void AppendEdgeSegmentSchedule(
            WarehouseJob job,
            int pathEdgeIndex,
            IReadOnlyList<ConveyorPathZone> chain,
            double desiredEntrySimTime)
        {
            if (job == null || chain == null || chain.Count == 0)
            {
                return;
            }

            var slots = new List<(int SlotIndex, double Arrive, double Leave)>();
            ConveyorPathZone? junctionZone = null;
            ConveyorPathZone? pickupZone = null;
            ConveyorPathZone? outfeedZone = null;

            for (var i = 0; i < chain.Count; i++)
            {
                var p = chain[i];
                if (p.PathEdgeIndex != pathEdgeIndex)
                {
                    continue;
                }

                if (p.Kind == ConveyorPathZoneKind.EdgeSlot)
                {
                    slots.Add((p.SlotIndex, p.ArriveSimTime, p.LeaveSimTime));
                }
                else if (p.Kind == ConveyorPathZoneKind.Junction)
                {
                    junctionZone = p;
                }
                else if (p.Kind == ConveyorPathZoneKind.Pickup)
                {
                    pickupZone = p;
                }
                else if (p.Kind == ConveyorPathZoneKind.Outfeed)
                {
                    outfeedZone = p;
                }
            }

            if (slots.Count == 0)
            {
                return;
            }

            var edgeAnchor = chain[FindFirstZoneOnEdge(chain, pathEdgeIndex)];
            var maxSlot = 0;
            for (var i = 0; i < slots.Count; i++)
            {
                if (slots[i].SlotIndex > maxSlot)
                {
                    maxSlot = slots[i].SlotIndex;
                }
            }

            var capacity = maxSlot + 1;
            var stopArrives = new double[capacity];
            for (var i = 0; i < slots.Count; i++)
            {
                var slotIndex = slots[i].SlotIndex;
                if (slotIndex >= 0 && slotIndex < capacity)
                {
                    stopArrives[slotIndex] = slots[i].Arrive;
                }
            }

            var entrySlot = capacity - 1;
            var entryTime = entrySlot < capacity ? stopArrives[entrySlot] : slots[0].Arrive;
            var s0Leave = 0d;
            for (var i = 0; i < slots.Count; i++)
            {
                if (slots[i].SlotIndex == 0)
                {
                    s0Leave = slots[i].Leave;
                    break;
                }
            }

            var exitTime = junctionZone.HasValue
                ? Math.Max(s0Leave, junctionZone.Value.ArriveSimTime - edgeAnchor.HopSeconds)
                : pickupZone.HasValue
                    ? Math.Max(s0Leave, pickupZone.Value.ArriveSimTime - edgeAnchor.HopSeconds)
                    : outfeedZone.HasValue
                        ? Math.Max(s0Leave, outfeedZone.Value.ArriveSimTime - edgeAnchor.HopSeconds)
                        : s0Leave;
            if (exitTime < entryTime)
            {
                exitTime = entryTime;
            }

            var occupancyEnd = pickupZone?.LeaveSimTime
                               ?? outfeedZone?.LeaveSimTime
                               ?? junctionZone?.LeaveSimTime
                               ?? exitTime;

            job.ConveyorSegmentSchedule ??= new List<ConveyorSegmentScheduleEntry>();
            job.ConveyorSegmentSchedule.Add(new ConveyorSegmentScheduleEntry
            {
                FromNodeIndex = edgeAnchor.FromNodeIndex,
                ToNodeIndex = edgeAnchor.ToNodeIndex,
                SlotIndex = entrySlot,
                DesiredEntrySimTime = desiredEntrySimTime,
                EntrySimTime = entryTime,
                ExitSimTime = exitTime,
                OccupancyEndSimTime = occupancyEnd,
                StopArriveSimTimes = stopArrives,
            });
        }

        private double PickupDwellAfterEnter(WarehouseJob job, int pickupNodeIndex, double enter)
        {
            ref var pickup = ref _topology.GetNode(pickupNodeIndex);
            var workDuration = ComputeStackerWorkSeconds(job, pickup);
            var stackerResources = BuildStackerResourceIds(pickup.StackerId, job.TargetSlot.Column);
            var stackerStart = stackerResources.Length > 0
                ? _reservations.QueryEarliestStartAll(enter, workDuration, stackerResources)
                : enter;
            return stackerStart + workDuration - enter;
        }

        private float GetOutfeedServiceSeconds(int outfeedNodeIndex)
        {
            ref var node = ref _topology.GetNode(outfeedNodeIndex);
            return node.OutfeedServiceSeconds > 0f
                ? node.OutfeedServiceSeconds
                : _bindings.OutfeedServiceSeconds;
        }

        private int ResolveOutfeedPortIndex(int outfeedNodeIndex)
        {
            if (_topology?.OutfeedNodeIndices == null)
            {
                return -1;
            }

            for (var i = 0; i < _topology.OutfeedNodeIndices.Count; i++)
            {
                if (_topology.OutfeedNodeIndices[i] == outfeedNodeIndex)
                {
                    return i;
                }
            }

            return -1;
        }

        private string BuildOutfeedServiceResourceId(int outfeedNodeIndex)
        {
            ref var node = ref _topology.GetNode(outfeedNodeIndex);
            var port = ResolveOutfeedPortIndex(outfeedNodeIndex);
            return SimEntityNaming.OutfeedServiceResourceId(node, port);
        }

        private double OutfeedDwellAfterEnter(int outfeedNodeIndex, double enter)
        {
            var duration = GetOutfeedServiceSeconds(outfeedNodeIndex);
            var serviceResourceId = BuildOutfeedServiceResourceId(outfeedNodeIndex);
            var serviceStart = _reservations.QueryEarliestStartAll(
                enter,
                duration,
                new[] { serviceResourceId });
            return serviceStart + duration - enter;
        }

        private void ApplyPickupStackerReserve(
            WarehouseJob job,
            int pickupNodeIndex,
            double pickupEnter,
            string pickupZoneResourceId,
            double pickupZoneLeave,
            TransitMutationAccumulator mutations)
        {
            ref var pickup = ref _topology.GetNode(pickupNodeIndex);
            var workDuration = ComputeStackerWorkSeconds(job, pickup);
            var stackerResources = BuildStackerResourceIds(pickup.StackerId, job.TargetSlot.Column);
            var stackerStart = stackerResources.Length > 0
                ? _reservations.QueryEarliestStartAll(pickupEnter, workDuration, stackerResources)
                : pickupEnter;
            var holdEnd = stackerStart + workDuration;

            if (stackerResources.Length > 0)
            {
                foreach (var resourceId in stackerResources)
                {
                    _reservations.TryReserve(resourceId, stackerStart, holdEnd, out _);
                }
            }

            // 取货点 zone 占用需覆盖整段堆垛作业（驶入 hop 后箱仍停在取货区直到被取走）。
            // 在输送调度阶段即写入该占用，使后续箱的路径规划（ResolvePickupDownstreamEnter）能看到并被顺延，
            // 避免两箱在同一取货点上时间重叠。
            if (!string.IsNullOrEmpty(pickupZoneResourceId) && holdEnd > pickupZoneLeave + 1e-9)
            {
                _reservations.TryReserve(pickupZoneResourceId, pickupZoneLeave, holdEnd, out _);
            }

            mutations.SetStackerReserve(stackerStart, holdEnd);
            CommitInboundStackerBooking(job, pickup, holdEnd);
        }

        private static int FindFirstZoneOnEdge(IReadOnlyList<ConveyorPathZone> chain, int pathEdgeIndex)
        {
            for (var i = 0; i < chain.Count; i++)
            {
                if (chain[i].PathEdgeIndex == pathEdgeIndex)
                {
                    return i;
                }
            }

            return 0;
        }
    }
}
