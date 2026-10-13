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

            if (zoneIndex > 0 && IsSameEdgeSlot(chain[zoneIndex - 1], zone))
            {
                ReleaseSameEdgeSuccessorHold(chain[zoneIndex - 1], zone);
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
            var hopStart = desiredStart;
            var arrive = desiredStart;
            var leave = desiredStart;
            var isEntry = IsSegmentEntrySlot(chain, zoneIndex);
            string laneTokenId = null;
            var laneDuration = 0d;
            var slotCommitted = false;
            var reserveStart = hopStart;
            var reserveEnd = hopStart;

            // 入口槽占路段令牌直到车尾离开，并占住下游路口的直线。窗口被顺延时，停留点预约作废重试。
            for (var attempt = 0; attempt < 24 && !slotCommitted; attempt++)
            {
                hopStart = _reservations.GetEarliestFreeTimeForDuration(zone.ResourceId, hopStart, hop);
                arrive = hopStart + hop;
                // 下游空闲时 leave == arrive，货箱到达停留点后立即开始下一段 hop，不强制额外停留。
                leave = ComputeZoneLeaveTime(job, zone, next, arrive, hop, chain, zoneIndex);

                for (var iter = 0; iter < 128; iter++)
                {
                    var holdEnd = SlotReservationEnd(zone, next, leave);
                    var reserveDuration = Math.Max(hop, holdEnd - hopStart);
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

                if (isEntry)
                {
                    laneDuration = SegmentBodyLaneDuration(chain, zoneIndex, hopStart, leave);
                    var laneStart = EarliestLaneStart(zone, hopStart, laneDuration, out laneTokenId);
                    if (laneStart > hopStart + 1e-9)
                    {
                        hopStart = laneStart;
                        continue;
                    }
                }

                var committedEnd = SlotReservationEnd(zone, next, leave);
                reserveStart = hopStart;
                reserveEnd = committedEnd;
                CommitReservationWindow(zone.ResourceId, ref reserveStart, ref reserveEnd);
                if (reserveStart > hopStart + 1e-9)
                {
                    var shift = reserveStart - hopStart;
                    hopStart = reserveStart;
                    arrive += shift;
                    leave += shift;
                }

                if (!isEntry || laneDuration <= 1e-9 || string.IsNullOrEmpty(laneTokenId))
                {
                    slotCommitted = true;
                    break;
                }

                laneDuration = SegmentBodyLaneDuration(chain, zoneIndex, hopStart, leave);
                var acceptedLaneStart = EarliestLaneStart(zone, hopStart, laneDuration, out laneTokenId);
                if (acceptedLaneStart > hopStart + 1e-9)
                {
                    _reservations.Release(zone.ResourceId, reserveStart, reserveEnd);
                    hopStart = acceptedLaneStart;
                    continue;
                }

                _reservations.TryReserve(laneTokenId, hopStart, hopStart + laneDuration, out var laneActual);
                if (laneActual > hopStart + 1e-9)
                {
                    _reservations.Release(laneTokenId, laneActual, laneActual + laneDuration);
                    _reservations.Release(zone.ResourceId, reserveStart, reserveEnd);
                    hopStart = laneActual;
                    continue;
                }

                zone.LaneResourceId = laneTokenId;
                zone.LaneReserveStart = hopStart;
                zone.LaneReserveEnd = hopStart + laneDuration;
                slotCommitted = true;
            }

            if (!slotCommitted)
            {
                arrive = hopStart + hop;
                leave = ComputeZoneLeaveTime(job, zone, next, arrive, hop, chain, zoneIndex);
                reserveStart = hopStart;
                reserveEnd = SlotReservationEnd(zone, next, leave);
                CommitReservationWindow(zone.ResourceId, ref reserveStart, ref reserveEnd);
                if (reserveStart > hopStart + 1e-9)
                {
                    var shift = reserveStart - hopStart;
                    hopStart = reserveStart;
                    arrive += shift;
                    leave += shift;
                }
            }

            if (next.HasValue && IsSameEdgeSlot(zone, next.Value))
            {
                HoldSameEdgeSuccessor(next.Value, leave);
            }

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

            if (TryGetEdgeGateHoldSeconds(zone, out var gateWaitSeconds))
            {
                var appliedGateWait = Math.Min(gateWaitSeconds, Math.Max(0d, leave - arrive));
                if (appliedGateWait > 1e-9)
                {
                    mutations.ApplySegmentMetrics(new SegmentMetrics(
                        serviceDelta: 0,
                        entryWaitDelta: 0,
                        entryWaitResourceId: null,
                        downstreamWaitDelta: appliedGateWait,
                        downstreamWaitResourceId: ConveyorMapMath.BuildSegmentGateResourceId(
                            _topology.Map,
                            zone.FromNodeIndex,
                            zone.ToNodeIndex)));
                }
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
            else if (zone.Kind == ConveyorPathZoneKind.ProcessStation && job != null)
            {
                ApplyProcessStationServiceReserve(job, zone.ToNodeIndex, arrive, zone.LeaveSimTime, mutations);
            }
            else if (zone.Kind == ConveyorPathZoneKind.VerticalTransfer && job != null)
            {
                ApplyVerticalTransferServiceReserve(job, zone.ToNodeIndex, arrive, zone.LeaveSimTime, mutations);
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

        /// <summary>路口 zone：从到达中心到开始驶出占用；来向驶入不锁路口。</summary>
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
            var hopStart = desiredStart;
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
                    if (leave < arrive)
                    {
                        leave = arrive;
                    }

                    var holdDuration = leave - arrive;
                    if (holdDuration <= 1e-9)
                    {
                        hopStart = arrive - hop;
                        break;
                    }

                    var conflictFree = _reservations.GetEarliestFreeTimeForDuration(
                        zone.ResourceId,
                        arrive,
                        holdDuration);
                    if (conflictFree <= arrive + 1e-9)
                    {
                        hopStart = arrive - hop;
                        break;
                    }

                    arrive = conflictFree;
                    hopStart = arrive - hop;
                }

                leave = ComputeJunctionExitHopStart(
                    zone.ToNodeIndex,
                    zone.JunctionNextNodeIndex,
                    arrive,
                    hop);
            }
            else
            {
                leave = arrive + hop;
                for (var iter = 0; iter < 128; iter++)
                {
                    var conflictFreeStart = _reservations.GetEarliestFreeTimeForDuration(
                        zone.ResourceId,
                        arrive,
                        leave - arrive);
                    if (conflictFreeStart <= arrive + 1e-9)
                    {
                        hopStart = arrive - hop;
                        break;
                    }

                    arrive = conflictFreeStart;
                    hopStart = arrive - hop;
                    leave = arrive + hop;
                }
            }

            // 来向驶入还在各自边上。路口锁从到达中心开始，到开始驶出为止。
            if (leave < arrive)
            {
                leave = arrive;
            }

            var junctionStart = arrive;
            var junctionEnd = leave;
            CommitReservationWindow(zone.ResourceId, ref junctionStart, ref junctionEnd);
            if (junctionStart > arrive + 1e-9)
            {
                var shift = junctionStart - arrive;
                hopStart += shift;
                arrive = junctionStart;
                leave = junctionEnd;
            }

            if (next.HasValue && next.Value.Kind == ConveyorPathZoneKind.EdgeSlot)
            {
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

            zone.DesiredArriveSimTime = desiredStart;
            zone.ArriveSimTime = arrive;
            zone.LeaveSimTime = leave;

            ConveyorTransitApplier.ApplyToJob(job, new TransitMutationAccumulator());

            result.Success = true;
            result.Zone = zone;
            result.NextStepStartTime = leave;
            return result;
        }

        /// <summary>
        /// 从当前槽的离开时刻推到车头到达下游节点。
        /// 后面的槽位 hop 与路口/取货点驶入 hop 都算在路段上；路口中心的等待不算。
        /// </summary>
        public static double ProjectSegmentClearTime(
            IReadOnlyList<ConveyorPathZone> chain,
            int zoneIndex,
            double leave)
        {
            if (chain == null || zoneIndex < 0 || zoneIndex >= chain.Count)
            {
                return leave;
            }

            var clear = leave;
            var edge = chain[zoneIndex].PathEdgeIndex;
            for (var i = zoneIndex + 1; i < chain.Count; i++)
            {
                var ahead = chain[i];
                if (ahead.PathEdgeIndex != edge)
                {
                    break;
                }

                if (ahead.Kind == ConveyorPathZoneKind.EdgeSlot || IsApproachNode(ahead.Kind))
                {
                    clear += ahead.HopSeconds;
                }
            }

            return clear;
        }

        /// <summary>
        /// 车尾离开本段的时刻。车头到下游节点后若还停着，停的时间也算；
        /// 再沿后续路段开出一个车身长度，尾端才让开。没有下一段时，取货抬走即结束。
        /// </summary>
        public double ProjectBodyClearTime(
            IReadOnlyList<ConveyorPathZone> chain,
            int zoneIndex,
            double frontAtNode)
        {
            if (chain == null || zoneIndex < 0 || zoneIndex >= chain.Count)
            {
                return frontAtNode;
            }

            var nodeLeave = CommittedNodeLeave(chain, zoneIndex, frontAtNode);
            var downstream = FindDownstreamEdgeStart(chain, zoneIndex);
            var tail = TailSeconds(chain[zoneIndex].FromNodeIndex, chain[zoneIndex].ToNodeIndex);
            if (downstream < 0)
            {
                if (nodeLeave > frontAtNode + 1e-6)
                {
                    return nodeLeave;
                }

                return frontAtNode + tail;
            }

            var moveStart = nodeLeave;
            var next = chain[downstream];
            if (next.ArriveSimTime > 1e-6)
            {
                var depart = next.ArriveSimTime - next.HopSeconds;
                if (depart > moveStart)
                {
                    moveStart = depart;
                }
            }

            var cargo = _topology?.Map != null
                ? (double)ConveyorMapMath.GetCargoOccupancyLengthMeters(_topology.Map)
                : 1.2d;
            var time = TravelCargoLength(chain, downstream, moveStart, cargo);
            if (nodeLeave > frontAtNode + 1e-6 && time < nodeLeave + tail)
            {
                time = nodeLeave + tail;
            }

            return time;
        }

        private double SegmentBodyLaneDuration(
            IReadOnlyList<ConveyorPathZone> chain,
            int zoneIndex,
            double hopStart,
            double leave)
        {
            var front = hopStart + SegmentLaneDuration(chain, zoneIndex, hopStart, leave);
            var body = ProjectBodyClearTime(chain, zoneIndex, front);
            var duration = body - hopStart;
            return duration < 0d ? 0d : duration;
        }

        private static double CommittedNodeLeave(
            IReadOnlyList<ConveyorPathZone> chain,
            int zoneIndex,
            double frontAtNode)
        {
            var leave = frontAtNode;
            var edge = chain[zoneIndex].PathEdgeIndex;
            for (var i = zoneIndex; i < chain.Count; i++)
            {
                var ahead = chain[i];
                if (ahead.PathEdgeIndex != edge)
                {
                    break;
                }

                if (IsApproachNode(ahead.Kind) && ahead.LeaveSimTime > leave)
                {
                    leave = ahead.LeaveSimTime;
                }
            }

            return leave;
        }

        private static int FindDownstreamEdgeStart(IReadOnlyList<ConveyorPathZone> chain, int zoneIndex)
        {
            var edge = chain[zoneIndex].PathEdgeIndex;
            for (var i = zoneIndex + 1; i < chain.Count; i++)
            {
                if (chain[i].PathEdgeIndex != edge)
                {
                    return i;
                }
            }

            return -1;
        }

        private double TailSeconds(int fromNodeIndex, int toNodeIndex)
        {
            if (_topology != null && _topology.TryGetEdge(fromNodeIndex, toNodeIndex, out var edge))
            {
                return ConveyorMapMath.GetCargoTailClearanceSeconds(_topology.Map, edge);
            }

            var length = ConveyorMapMath.GetCargoOccupancyLengthMeters(_topology?.Map);
            var speed = _topology?.Map != null && _topology.Map.DefaultSpeedMetersPerSecond > 0f
                ? _topology.Map.DefaultSpeedMetersPerSecond
                : 0.6f;
            return length / Math.Max(0.01f, speed);
        }

        private double TravelCargoLength(
            IReadOnlyList<ConveyorPathZone> chain,
            int startIndex,
            double time,
            double remaining)
        {
            var map = _topology?.Map;
            if (map == null || chain == null)
            {
                return time;
            }

            var i = startIndex;
            while (i < chain.Count && remaining > 1e-3d)
            {
                var zone = chain[i];
                if (!_topology.TryGetEdge(zone.FromNodeIndex, zone.ToNodeIndex, out var edge))
                {
                    break;
                }

                var length = (double)Math.Max(0f, edge.DistanceMeters);
                var speed = (double)Math.Max(0.01f, ConveyorMapMath.ResolveSpeedMetersPerSecond(map, edge));
                if (length <= 1e-4d)
                {
                    break;
                }

                var edgeIndex = zone.PathEdgeIndex;
                var capacity = Math.Max(1, map.GetEdgeCapacity(edge));
                var cursor = 0d;
                var cursorTime = time;
                var sawSlot = false;
                for (var j = i; j < chain.Count && chain[j].PathEdgeIndex == edgeIndex; j++)
                {
                    var slot = chain[j];
                    if (slot.Kind != ConveyorPathZoneKind.EdgeSlot || slot.ArriveSimTime <= 1e-6d)
                    {
                        continue;
                    }

                    sawSlot = true;
                    var meters = (double)ConveyorMapMath.GetZoneNormalizedPosition(
                        map,
                        edge,
                        capacity,
                        slot.SlotIndex) * length;
                    if (ConveyorBodyClearance.TryAdvance(
                            ref cursorTime,
                            ref cursor,
                            ref remaining,
                            slot.ArriveSimTime,
                            meters,
                            speed,
                            out var clear))
                    {
                        return clear;
                    }

                    if (slot.LeaveSimTime > cursorTime)
                    {
                        cursorTime = slot.LeaveSimTime;
                    }
                }

                if (sawSlot
                    && ConveyorBodyClearance.TryAdvance(
                        ref cursorTime,
                        ref cursor,
                        ref remaining,
                        cursorTime,
                        length,
                        speed,
                        out var edgeClear))
                {
                    return edgeClear;
                }

                if (sawSlot)
                {
                    time = cursorTime;
                }
                else
                {
                    var take = remaining < length ? remaining : length;
                    time += take / speed;
                    remaining -= take;
                }

                i++;
                while (i < chain.Count && chain[i].PathEdgeIndex == edgeIndex)
                {
                    i++;
                }
            }

            if (remaining > 1e-3d)
            {
                var speed = map.DefaultSpeedMetersPerSecond > 0f ? map.DefaultSpeedMetersPerSecond : 0.6f;
                time += remaining / Math.Max(0.01d, speed);
            }

            return time;
        }

        /// <summary>两条路段令牌是否互相阻挡：同一令牌，或同一路段上的相反方向。</summary>
        public static bool LaneClaimsConflict(string laneA, string laneB)
        {
            if (string.IsNullOrEmpty(laneA) || string.IsNullOrEmpty(laneB))
            {
                return false;
            }

            if (string.Equals(laneA, laneB, StringComparison.Ordinal))
            {
                return true;
            }

            if (!TryParseLane(laneA, out var keyA, out var signA)
                || !TryParseLane(laneB, out var keyB, out var signB))
            {
                return false;
            }

            return string.Equals(keyA, keyB, StringComparison.Ordinal) && signA != signB;
        }

        /// <summary>同一物理路段上、行驶方向相反的两条令牌。</summary>
        public static bool IsOppositeLane(string laneA, string laneB)
        {
            if (string.IsNullOrEmpty(laneA)
                || string.IsNullOrEmpty(laneB)
                || string.Equals(laneA, laneB, StringComparison.Ordinal))
            {
                return false;
            }

            if (!TryParseLane(laneA, out var keyA, out var signA)
                || !TryParseLane(laneB, out var keyB, out var signB))
            {
                return false;
            }

            return string.Equals(keyA, keyB, StringComparison.Ordinal) && signA != signB;
        }

        /// <summary>令牌所属方向键，以及同一路段的反向键。形如 <c>LR1-RC2|f</c>。</summary>
        public static bool TryGetLaneDirectionKeys(string laneId, out string directionKey, out string oppositeKey)
        {
            directionKey = null;
            oppositeKey = null;
            if (!TryParseLane(laneId, out var key, out var sign))
            {
                return false;
            }

            var own = sign > 0 ? "f" : "r";
            var other = sign > 0 ? "r" : "f";
            directionKey = key + "|" + own;
            oppositeKey = key + "|" + other;
            return true;
        }

        private static bool IsSegmentEntrySlot(IReadOnlyList<ConveyorPathZone> chain, int zoneIndex)
        {
            if (chain == null || zoneIndex < 0 || zoneIndex >= chain.Count)
            {
                return false;
            }

            if (chain[zoneIndex].Kind != ConveyorPathZoneKind.EdgeSlot)
            {
                return false;
            }

            if (zoneIndex == 0)
            {
                return true;
            }

            var previous = chain[zoneIndex - 1];
            return previous.Kind != ConveyorPathZoneKind.EdgeSlot
                   || previous.PathEdgeIndex != chain[zoneIndex].PathEdgeIndex;
        }

        private static bool IsApproachNode(ConveyorPathZoneKind kind) =>
            kind is ConveyorPathZoneKind.Junction
                or ConveyorPathZoneKind.Pickup
                or ConveyorPathZoneKind.Outfeed
                or ConveyorPathZoneKind.ProcessStation
                or ConveyorPathZoneKind.VerticalTransfer;

        /// <summary>从开始驶入本槽，到车头到达本边下游节点的最短占用（含本槽已发生的等待）。</summary>
        private static double SegmentLaneDuration(
            IReadOnlyList<ConveyorPathZone> chain,
            int zoneIndex,
            double hopStart,
            double leave)
        {
            var occupied = leave - hopStart;
            if (occupied < 0)
            {
                occupied = 0;
            }

            var clear = ProjectSegmentClearTime(chain, zoneIndex, leave);
            var afterLeave = clear - leave;
            if (afterLeave < 0)
            {
                afterLeave = 0;
            }

            return occupied + afterLeave;
        }

        private double EarliestLaneStart(
            in ConveyorPathZone zone,
            double hopStart,
            double duration,
            out string tokenId)
        {
            tokenId = null;
            if (duration <= 1e-9 || _topology?.Map == null)
            {
                return hopStart;
            }

            // 对向互斥由仿真器的通道锁在出发前保证，这里只排同向容量。
            var own = CachedLaneTokens(zone, CachedLaneSign(zone));
            var best = double.MaxValue;
            tokenId = own[0];
            for (var i = 0; i < own.Length; i++)
            {
                var candidate = _reservations.GetEarliestFreeTimeForDuration(own[i], hopStart, duration);
                if (candidate < best)
                {
                    best = candidate;
                    tokenId = own[i];
                }
            }

            return best > hopStart ? best : hopStart;
        }

        private int CachedLaneSign(in ConveyorPathZone zone)
        {
            var key = (zone.FromNodeIndex, zone.ToNodeIndex);
            if (_laneSignCache.TryGetValue(key, out var sign))
            {
                return sign;
            }

            sign = LaneSign(zone);
            _laneSignCache[key] = sign;
            return sign;
        }

        private string[] CachedLaneTokens(in ConveyorPathZone zone, int sign)
        {
            var key = (zone.FromNodeIndex, zone.ToNodeIndex, sign);
            if (_laneTokenCache.TryGetValue(key, out var ids))
            {
                return ids;
            }

            ids = BuildLaneTokenIds(zone, sign);
            _laneTokenCache[key] = ids;
            return ids;
        }

        private int LaneSign(in ConveyorPathZone zone)
        {
            var from = SimEntityNaming.FormatNode(_topology.Map, zone.FromNodeIndex);
            var to = SimEntityNaming.FormatNode(_topology.Map, zone.ToNodeIndex);
            return string.CompareOrdinal(from, to) <= 0 ? 1 : -1;
        }

        private string[] BuildLaneTokenIds(in ConveyorPathZone zone, int sign)
        {
            var capacity = 1;
            if (_topology.TryGetEdge(zone.FromNodeIndex, zone.ToNodeIndex, out var edge))
            {
                capacity = Math.Max(1, _topology.Map.GetEdgeCapacity(edge));
            }

            var from = SimEntityNaming.FormatNode(_topology.Map, zone.FromNodeIndex);
            var to = SimEntityNaming.FormatNode(_topology.Map, zone.ToNodeIndex);
            var key = SimEntityNaming.CanonicalSegmentKey(from, to);
            var signText = sign > 0 ? "f" : "r";
            var ids = new string[capacity];
            for (var i = 0; i < capacity; i++)
            {
                ids[i] = $"lane|{key}|{signText}|{i}";
            }

            return ids;
        }

        private static bool TryParseLane(string id, out string key, out int sign)
        {
            key = null;
            sign = 0;
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            var parts = id.Split('|');
            if (parts.Length != 4 || !string.Equals(parts[0], "lane", StringComparison.Ordinal))
            {
                return false;
            }

            key = parts[1];
            if (string.Equals(parts[2], "f", StringComparison.Ordinal))
            {
                sign = 1;
            }
            else if (string.Equals(parts[2], "r", StringComparison.Ordinal))
            {
                sign = -1;
            }

            return sign != 0 && !string.IsNullOrEmpty(key);
        }

        /// <summary>
        /// 停留点在预约表上的结束时刻。同边下一槽多占一个 hop；
        /// 下一段是路口/取货点时，再占住端部驶入，直到车头到达下游节点。
        /// </summary>
        private static double SlotReservationEnd(in ConveyorPathZone zone, ConveyorPathZone? next, double leave)
        {
            var end = SameEdgeHoldEnd(zone, next, leave);
            if (zone.Kind != ConveyorPathZoneKind.EdgeSlot
                || !next.HasValue
                || !IsApproachNode(next.Value.Kind)
                || next.Value.HopSeconds <= 1e-9f)
            {
                return end;
            }

            var cleared = leave + next.Value.HopSeconds;
            return cleared > end ? cleared : end;
        }

        /// <summary>已写入链上的出口槽，其预约表结束时刻（含端部驶入）。</summary>
        public static double CommittedSlotReservationEnd(in ConveyorPathZone zone, in ConveyorPathZone? next) =>
            SlotReservationEnd(zone, next, zone.LeaveSimTime);

        /// <summary>
        /// 同一条边上的下一停留点尚未正式预约前，多占住一个 hop。
        /// 这样在“预计离开”时刻处理交接时，后车不能从半开区间的终点抢入；交接完成后再把尾段收回。
        /// </summary>
        private static double SameEdgeHoldEnd(in ConveyorPathZone zone, ConveyorPathZone? next, double leave)
        {
            if (!next.HasValue || !IsSameEdgeSlot(zone, next.Value) || next.Value.HopSeconds <= 1e-9f)
            {
                return leave;
            }

            return leave + next.Value.HopSeconds;
        }

        private static bool IsSameEdgeSlot(in ConveyorPathZone previous, in ConveyorPathZone next) =>
            previous.Kind == ConveyorPathZoneKind.EdgeSlot
            && next.Kind == ConveyorPathZoneKind.EdgeSlot
            && previous.PathEdgeIndex == next.PathEdgeIndex;

        /// <summary>把下一停留点的起步 hop 先占上，避免预计离开之后、正式预约之前被别的箱子抢走。</summary>
        private void HoldSameEdgeSuccessor(in ConveyorPathZone next, double leave)
        {
            var hop = next.HopSeconds;
            if (hop <= 1e-9f || leave < 0)
            {
                return;
            }

            var accepted = _reservations.TryReserve(next.ResourceId, leave, leave + hop, out var actualStart);
            if (accepted && actualStart <= leave + 1e-9)
            {
                return;
            }

            _reservations.Release(next.ResourceId, actualStart, actualStart + hop);
        }

        private void ReleaseSameEdgeSuccessorHold(in ConveyorPathZone previous, in ConveyorPathZone next)
        {
            var hop = next.HopSeconds;
            var start = previous.LeaveSimTime;
            if (hop <= 1e-9f || start < 0)
            {
                return;
            }

            _reservations.Release(next.ResourceId, start, start + hop);
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

            // 返回值是到达入口停留点的时刻（hop 结束），不是空闲窗口的起点。
            var hopStart = _reservations.GetEarliestFreeTimeForDuration(
                nextEntryZone.ResourceId,
                exitHopStart,
                nextHop);
            var arrival = hopStart + nextHop;
            if (hopStart <= exitHopStart + 1e-9)
            {
                return 0;
            }

            var holdDuration = arrival - exitHopStart;
            var holdStart = _reservations.GetEarliestFreeTimeForDuration(
                nextEntryZone.ResourceId,
                exitHopStart,
                holdDuration);
            if (holdStart > exitHopStart + 1e-9)
            {
                // 等待区间里已经有别的箱子，不能再占一段会被顺延的窗。
                return arrival;
            }

            var reservedStart = exitHopStart;
            var reservedEnd = arrival;
            CommitReservationWindow(nextEntryZone.ResourceId, ref reservedStart, ref reservedEnd);
            if (reservedStart > exitHopStart + 1e-9)
            {
                _reservations.Release(nextEntryZone.ResourceId, reservedStart, reservedEnd);
                return arrival;
            }

            return arrival;
        }

        /// <summary>
        /// 写入 [start, end)。若预约表把起点顺延了，丢掉错位窗口并按实际起点重写，
        /// 避免时刻表仍声称原来的区间、和表上的占用对不上。
        /// </summary>
        private void CommitReservationWindow(string resourceId, ref double start, ref double end)
        {
            if (string.IsNullOrEmpty(resourceId) || end <= start + 1e-9)
            {
                return;
            }

            var duration = end - start;
            _reservations.TryReserve(resourceId, start, end, out var actualStart);
            if (actualStart <= start + 1e-9)
            {
                return;
            }

            _reservations.Release(resourceId, actualStart, actualStart + duration);
            _reservations.TryReserve(resourceId, actualStart, actualStart + duration, out var placed);
            var shift = placed - start;
            start += shift;
            end = start + duration;
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
            var leave = ComputeZoneLeaveTimeCore(job, zone, next, arrive, hop, chain, zoneIndex);
            if (zone.Kind == ConveyorPathZoneKind.EdgeSlot
                && TryGetEdgeGateHoldSeconds(zone, out var gateWait)
                && leave < arrive + gateWait)
            {
                return arrive + gateWait;
            }

            return leave;
        }

        /// <summary>路段中间门：该槽位是门前停留点时，返回开门等待秒数。</summary>
        private bool TryGetEdgeGateHoldSeconds(in ConveyorPathZone zone, out float waitSeconds)
        {
            waitSeconds = 0f;
            if (zone.Kind != ConveyorPathZoneKind.EdgeSlot
                || !_topology.TryGetEdge(zone.FromNodeIndex, zone.ToNodeIndex, out var edge))
            {
                return false;
            }

            return ConveyorMapMath.TryGetGateHoldSlot(_topology.Map, in edge, out var slotIndex, out waitSeconds)
                   && slotIndex == zone.SlotIndex;
        }

        private double ComputeZoneLeaveTimeCore(
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
                if (zone.Kind == ConveyorPathZoneKind.VerticalTransfer)
                {
                    return ResolveVerticalTransferZoneLeave(zone.ToNodeIndex, arrive, hop);
                }

                return ResolveProcessStationZoneLeave(job, zone, arrive, hop);
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

                // 路过别的交互点/出库口时不建节点区。出口槽要先走完端部留白到节点，
                // 再等下一边入口槽能接住这一跳，避免箱子在两段之间没有占用。
                case ConveyorPathZoneKind.EdgeSlot when nextZone.Kind == ConveyorPathZoneKind.EdgeSlot:
                {
                    var earliestAtNode = arrive + IncomingTerminalHopSeconds(zone);
                    return _reservations.GetEarliestFreeTimeForDuration(
                        nextZone.ResourceId,
                        earliestAtNode,
                        nextZone.HopSeconds);
                }

                case ConveyorPathZoneKind.EdgeSlot when nextZone.Kind == ConveyorPathZoneKind.Junction:
                {
                    var approachHop = nextZone.HopSeconds;
                    var junctionEnter = ResolveJunctionDownstreamEnter(
                        nextZone.ResourceId,
                        arrive,
                        approachHop,
                        nextZone.ToNodeIndex,
                        nextZone.JunctionNextNodeIndex);
                    return junctionEnter - approachHop;
                }

                case ConveyorPathZoneKind.EdgeSlot when nextZone.Kind == ConveyorPathZoneKind.Pickup:
                {
                    if (job == null)
                    {
                        return arrive + nextZone.HopSeconds;
                    }

                    var approachHop = nextZone.HopSeconds;
                    var pickupEnter = ResolvePickupDownstreamEnter(
                        nextZone.ResourceId,
                        arrive,
                        approachHop,
                        enter => PickupDwellAfterEnter(job, nextZone.ToNodeIndex, enter));
                    return pickupEnter - approachHop;
                }

                case ConveyorPathZoneKind.EdgeSlot when nextZone.Kind == ConveyorPathZoneKind.Outfeed:
                {
                    if (job == null)
                    {
                        return arrive + nextZone.HopSeconds;
                    }

                    var approachHop = nextZone.HopSeconds;
                    var outfeedEnter = ResolveOutfeedDownstreamEnter(
                        nextZone.ResourceId,
                        arrive,
                        approachHop,
                        enter => OutfeedDwellAfterEnter(nextZone.ToNodeIndex, enter));
                    return outfeedEnter - approachHop;
                }

                case ConveyorPathZoneKind.EdgeSlot when nextZone.Kind == ConveyorPathZoneKind.ProcessStation:
                {
                    if (job == null)
                    {
                        return arrive + nextZone.HopSeconds;
                    }

                    var approachHop = nextZone.HopSeconds;
                    var processEnter = ResolveOutfeedDownstreamEnter(
                        nextZone.ResourceId,
                        arrive,
                        approachHop,
                        enter => ProcessDwellAfterEnter(job, nextZone.ToNodeIndex, enter));
                    return processEnter - approachHop;
                }

                case ConveyorPathZoneKind.EdgeSlot when nextZone.Kind == ConveyorPathZoneKind.VerticalTransfer:
                {
                    if (job == null)
                    {
                        return arrive + nextZone.HopSeconds;
                    }

                    var approachHop = nextZone.HopSeconds;
                    var transferEnter = ResolveOutfeedDownstreamEnter(
                        nextZone.ResourceId,
                        arrive,
                        approachHop,
                        enter => TransferDwellAfterEnter(nextZone.ToNodeIndex, enter));
                    return transferEnter - approachHop;
                }

                case ConveyorPathZoneKind.Junction when nextZone.Kind == ConveyorPathZoneKind.EdgeSlot:
                {
                    return ComputeJunctionExitHopStart(
                        zone.ToNodeIndex,
                        zone.JunctionNextNodeIndex,
                        arrive,
                        hop);
                }

                case ConveyorPathZoneKind.ProcessStation:
                    return ResolveProcessStationZoneLeave(job, zone, arrive, hop);

                case ConveyorPathZoneKind.VerticalTransfer:
                    return ResolveVerticalTransferZoneLeave(zone.ToNodeIndex, arrive, hop);

                default:
                    return arrive + hop;
            }
        }

        /// <summary>当前边出口槽到下游节点的端部留白。只有 slot 0 紧挨节点。</summary>
        private float IncomingTerminalHopSeconds(in ConveyorPathZone zone)
        {
            if (zone.SlotIndex != 0
                || _topology == null
                || !_topology.TryGetEdge(zone.FromNodeIndex, zone.ToNodeIndex, out var edge))
            {
                return 0f;
            }

            return ConveyorMapMath.GetNodeApproachHopSeconds(_topology.Map, edge);
        }

        /// <summary>加工站点 zone：匹配标签时占用至加工完成，否则仅计 hop 直通。</summary>
        private double ResolveProcessStationZoneLeave(
            WarehouseJob job,
            in ConveyorPathZone zone,
            double arrive,
            float hop)
        {
            if (zone.Kind != ConveyorPathZoneKind.ProcessStation || job == null)
            {
                return arrive + hop;
            }

            var dwell = ProcessDwellAfterEnter(job, zone.ToNodeIndex, arrive);
            return dwell > 1e-9 ? arrive + dwell : arrive + hop;
        }

        /// <summary>将一条路径边上的 zone 时刻写入路段 schedule（供回放/自检）。</summary>
        public static void AppendEdgeSegmentSchedule(
            WarehouseJob job,
            int pathEdgeIndex,
            IReadOnlyList<ConveyorPathZone> chain,
            double desiredEntrySimTime,
            int reservedZoneCount)
        {
            if (job == null || chain == null || chain.Count == 0)
            {
                return;
            }

            var slots = new List<(int SlotIndex, double Arrive, double Leave)>();
            ConveyorPathZone? junctionZone = null;
            ConveyorPathZone? pickupZone = null;
            ConveyorPathZone? outfeedZone = null;
            ConveyorPathZone? processZone = null;
            ConveyorPathZone? verticalTransferZone = null;

            for (var i = 0; i < chain.Count; i++)
            {
                var p = chain[i];
                if (p.PathEdgeIndex != pathEdgeIndex)
                {
                    continue;
                }

                // 路口/取货点还没预约时，链上可能留着上一轮的到达时刻。用它会把本段离开时刻写成未来。
                if (i >= reservedZoneCount)
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
                else if (p.Kind == ConveyorPathZoneKind.ProcessStation)
                {
                    processZone = p;
                }
                else if (p.Kind == ConveyorPathZoneKind.VerticalTransfer)
                {
                    verticalTransferZone = p;
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
                ? Math.Max(s0Leave, junctionZone.Value.ArriveSimTime - junctionZone.Value.HopSeconds)
                : pickupZone.HasValue
                    ? Math.Max(s0Leave, pickupZone.Value.ArriveSimTime - pickupZone.Value.HopSeconds)
                    : outfeedZone.HasValue
                        ? Math.Max(s0Leave, outfeedZone.Value.ArriveSimTime - outfeedZone.Value.HopSeconds)
                        : processZone.HasValue
                            ? Math.Max(s0Leave, processZone.Value.ArriveSimTime - processZone.Value.HopSeconds)
                            : verticalTransferZone.HasValue
                                ? Math.Max(s0Leave, verticalTransferZone.Value.ArriveSimTime - verticalTransferZone.Value.HopSeconds)
                                : s0Leave;
            if (exitTime < entryTime)
            {
                exitTime = entryTime;
            }

            var occupancyEnd = pickupZone?.LeaveSimTime
                               ?? outfeedZone?.LeaveSimTime
                               ?? processZone?.LeaveSimTime
                               ?? verticalTransferZone?.LeaveSimTime
                               ?? junctionZone?.LeaveSimTime
                               ?? exitTime;

            job.ConveyorSegmentSchedule ??= new List<ConveyorSegmentScheduleEntry>();
            var entry = new ConveyorSegmentScheduleEntry
            {
                FromNodeIndex = edgeAnchor.FromNodeIndex,
                ToNodeIndex = edgeAnchor.ToNodeIndex,
                PathEdgeIndex = pathEdgeIndex,
                SlotIndex = entrySlot,
                DesiredEntrySimTime = desiredEntrySimTime,
                EntrySimTime = entryTime,
                ExitSimTime = exitTime,
                OccupancyEndSimTime = occupancyEnd,
                StopArriveSimTimes = stopArrives,
            };
            for (var i = job.ConveyorSegmentSchedule.Count - 1; i >= 0; i--)
            {
                var existing = job.ConveyorSegmentSchedule[i];
                if (existing.PathEdgeIndex != pathEdgeIndex)
                {
                    continue;
                }

                job.ConveyorSegmentSchedule[i] = entry;
                ConveyorSegmentScheduleNavigation.SortByPath(
                    job.ConveyorSegmentSchedule,
                    job.ConveyorPathNodeIndices);
                return;
            }

            job.ConveyorSegmentSchedule.Add(entry);
            ConveyorSegmentScheduleNavigation.SortByPath(
                job.ConveyorSegmentSchedule,
                job.ConveyorPathNodeIndices);
        }

        private double PickupDwellAfterEnter(WarehouseJob job, int pickupNodeIndex, double enter)
        {
            ref var pickup = ref _topology.GetNode(pickupNodeIndex);
            var workDuration = ComputeStackerWorkSeconds(job, pickup);
            var stackerResources = BuildStackerResourceIds(
                pickup.StackerId, job.TargetSlot.Column, job.TargetSlot.WarehouseId);
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

        private float GetProcessServiceSeconds(int processNodeIndex)
        {
            ref var node = ref _topology.GetNode(processNodeIndex);
            return node.ProcessServiceSeconds > 0f
                ? node.ProcessServiceSeconds
                : _bindings.ProcessStationServiceSeconds;
        }

        private string BuildProcessServiceResourceId(int processNodeIndex)
        {
            ref var node = ref _topology.GetNode(processNodeIndex);
            return SimEntityNaming.ProcessStationServiceResourceId(node, processNodeIndex);
        }

        private double ProcessDwellAfterEnter(WarehouseJob job, int processNodeIndex, double enter)
        {
            ref var node = ref _topology.GetNode(processNodeIndex);
            if (!ConveyorProcessStationUtility.JobRequiresStationTag(job, in node))
            {
                return 0;
            }

            var duration = GetProcessServiceSeconds(processNodeIndex);
            var serviceResourceId = BuildProcessServiceResourceId(processNodeIndex);
            var serviceStart = _reservations.QueryEarliestStartAll(
                enter,
                duration,
                new[] { serviceResourceId });
            return serviceStart + duration - enter;
        }

        private void ApplyProcessStationServiceReserve(
            WarehouseJob job,
            int processNodeIndex,
            double processEnter,
            double processZoneLeave,
            TransitMutationAccumulator mutations)
        {
            ref var node = ref _topology.GetNode(processNodeIndex);
            if (!ConveyorProcessStationUtility.JobRequiresStationTag(job, in node))
            {
                return;
            }

            var duration = GetProcessServiceSeconds(processNodeIndex);
            var serviceResourceId = BuildProcessServiceResourceId(processNodeIndex);
            var serviceStart = _reservations.QueryEarliestStartAll(
                processEnter,
                duration,
                new[] { serviceResourceId });
            var serviceEnd = serviceStart + duration;
            _reservations.TryReserve(serviceResourceId, serviceStart, serviceEnd, out _);

            ref var stationNode = ref _topology.GetNode(processNodeIndex);
            var zoneResourceId = SimEntityNaming.ProcessStationZoneResourceId(stationNode, processNodeIndex);
            if (serviceEnd > processZoneLeave + 1e-9)
            {
                _reservations.TryReserve(zoneResourceId, processZoneLeave, serviceEnd, out _);
            }

            mutations.ApplySegmentMetrics(new SegmentMetrics(
                serviceDelta: serviceEnd - processEnter,
                entryWaitDelta: Math.Max(0d, serviceStart - processEnter),
                entryWaitResourceId: serviceResourceId,
                downstreamWaitDelta: 0,
                downstreamWaitResourceId: null));
        }

        private float GetTransferSeconds(int transferNodeIndex)
        {
            ref var node = ref _topology.GetNode(transferNodeIndex);
            return ConveyorVerticalTransferUtility.ResolveTransferSeconds(in node, _bindings);
        }

        private string BuildVerticalTransferServiceResourceId(int transferNodeIndex)
        {
            ref var node = ref _topology.GetNode(transferNodeIndex);
            return SimEntityNaming.VerticalTransferServiceResourceId(node, transferNodeIndex);
        }

        private double TransferDwellAfterEnter(int transferNodeIndex, double enter)
        {
            var duration = GetTransferSeconds(transferNodeIndex);
            var serviceResourceId = BuildVerticalTransferServiceResourceId(transferNodeIndex);
            var serviceStart = _reservations.QueryEarliestStartAll(
                enter,
                duration,
                new[] { serviceResourceId });
            return serviceStart + duration - enter;
        }

        private double ResolveVerticalTransferZoneLeave(int transferNodeIndex, double arrive, float hop)
        {
            var dwell = TransferDwellAfterEnter(transferNodeIndex, arrive);
            return dwell > 1e-9 ? arrive + dwell : arrive + hop;
        }

        private void ApplyVerticalTransferServiceReserve(
            WarehouseJob job,
            int transferNodeIndex,
            double transferEnter,
            double transferZoneLeave,
            TransitMutationAccumulator mutations)
        {
            var duration = GetTransferSeconds(transferNodeIndex);
            var serviceResourceId = BuildVerticalTransferServiceResourceId(transferNodeIndex);
            var serviceStart = _reservations.QueryEarliestStartAll(
                transferEnter,
                duration,
                new[] { serviceResourceId });
            var serviceEnd = serviceStart + duration;
            _reservations.TryReserve(serviceResourceId, serviceStart, serviceEnd, out _);

            ref var transferNode = ref _topology.GetNode(transferNodeIndex);
            var zoneResourceId = SimEntityNaming.VerticalTransferZoneResourceId(transferNode, transferNodeIndex);
            if (serviceEnd > transferZoneLeave + 1e-9)
            {
                _reservations.TryReserve(zoneResourceId, transferZoneLeave, serviceEnd, out _);
            }

            mutations.ApplySegmentMetrics(new SegmentMetrics(
                serviceDelta: serviceEnd - transferEnter,
                entryWaitDelta: Math.Max(0d, serviceStart - transferEnter),
                entryWaitResourceId: serviceResourceId,
                downstreamWaitDelta: 0,
                downstreamWaitResourceId: null));
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
            var stackerResources = BuildStackerResourceIds(
                pickup.StackerId, job.TargetSlot.Column, job.TargetSlot.WarehouseId);
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
