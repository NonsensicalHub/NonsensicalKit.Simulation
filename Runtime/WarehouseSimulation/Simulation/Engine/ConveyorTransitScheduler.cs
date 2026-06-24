using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>
    /// 输送时间与资源预约计算（零压力积放 ZPA）。
    /// <para>每条边按容量拆成多个停留点（slot）；货箱从入口 slot 逐级推进，下游空闲才能前进。</para>
    /// <para>分片：<c>.cs</c> 整路径/单段预约；<c>.Zones.cs</c> 按 zone 链逐步预约（仿真主路径）。</para>
    /// <para>取货点/路口会反推上游到达时刻，并与堆垛机作业时长耦合（见 <see cref="ReservePickupSegment"/>）。</para>
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

        #region 公开 API（整路径 / 单段）

        /// <summary>
        /// 沿路径预约输送资源并返回计划对象；
        /// <see cref="ConveyorTransitPlan.InfeedPhysicalReleaseSimTime"/> 为货箱尾端离开入库口碰撞区的时刻。
        /// </summary>
        public ConveyorTransitPlan BuildTransitPlan(
            WarehouseJob job,
            IReadOnlyList<int> pathNodeIndices,
            double startTime)
        {
            var result = BuildTransitPlanCore(job, pathNodeIndices, startTime);
            if (result.Plan.PathComplete)
            {
                ConveyorTransitApplier.ApplyToJob(job, result.Mutations);
            }

            return result.Plan;
        }

        /// <summary>
        /// 预约路径上的一条边（segmentIndex 对应 path[segmentIndex]→path[segmentIndex+1]），并立即累计到 job。
        /// </summary>
        public ConveyorPathSegmentReservation TryReservePathSegment(
            WarehouseJob job,
            IReadOnlyList<int> pathNodeIndices,
            int segmentIndex,
            double desiredStart)
        {
            var result = new ConveyorPathSegmentReservation { Success = false };
            if (pathNodeIndices == null
                || pathNodeIndices.Count < 2
                || segmentIndex < 0
                || segmentIndex >= pathNodeIndices.Count - 1)
            {
                result.PathComplete = false;
                return result;
            }

            var edgeIndex = segmentIndex + 1;
            var prev = pathNodeIndices[segmentIndex];
            var curr = pathNodeIndices[edgeIndex];
            if (!_topology.TryGetEdge(prev, curr, out var edge))
            {
                WarehouseSimLog.Warn(
                    $"输送地图缺边 job={job?.JobId} from={prev} to={curr}，中止路段预约。");
                result.PathComplete = false;
                return result;
            }

            ref var toNode = ref _topology.GetNode(curr);
            var nextNode = edgeIndex + 1 < pathNodeIndices.Count ? pathNodeIndices[edgeIndex + 1] : -1;
            var mutations = new TransitMutationAccumulator();
            var segment = ReserveSegmentTransit(
                job, mutations, prev, curr, edge, toNode.Kind, curr, nextNode, desiredStart);

            result.Success = true;
            result.ScheduleEntry = new ConveyorSegmentScheduleEntry
            {
                FromNodeIndex = prev,
                ToNodeIndex = curr,
                SlotIndex = segment.EntrySlotIndex,
                DesiredEntrySimTime = desiredStart,
                EntrySimTime = segment.EntryTime,
                ExitSimTime = segment.ExitTime,
                OccupancyEndSimTime = segment.OccupancyEndTime,
                StopArriveSimTimes = segment.StopArriveTimes,
            };
            result.NextSegmentStartTime = segment.NextSegmentStartTime;

            if (segmentIndex == 0)
            {
                result.InfeedPhysicalReleaseSimTime = segment.EntryTime
                    + ConveyorMapMath.GetCargoTailClearanceSeconds(_topology.Map, edge);
            }

            ConveyorTransitApplier.ApplyToJob(job, mutations);
            return result;
        }

        #endregion

        #region 整路径构建

        private TransitBuildResult BuildTransitPlanCore(
            WarehouseJob job,
            IReadOnlyList<int> pathNodeIndices,
            double startTime)
        {
            var mutations = new TransitMutationAccumulator();
            var plan = new ConveyorTransitPlan
            {
                StartSimTime = startTime,
                EndSimTime = startTime,
                InfeedPhysicalReleaseSimTime = startTime,
            };
            var t = startTime;
            if (pathNodeIndices == null || pathNodeIndices.Count < 2)
            {
                return new TransitBuildResult(plan, mutations);
            }

            var trackedInfeedRelease = false;
            for (var i = 1; i < pathNodeIndices.Count; i++)
            {
                var prev = pathNodeIndices[i - 1];
                var curr = pathNodeIndices[i];
                if (!_topology.TryGetEdge(prev, curr, out var edge))
                {
                    WarehouseSimLog.Warn(
                        $"输送地图缺边 job={job?.JobId} from={prev} to={curr}，中止路径规划。");
                    plan.PathComplete = false;
                    return new TransitBuildResult(plan, mutations);
                }

                ref var toNode = ref _topology.GetNode(curr);
                var nextNode = i + 1 < pathNodeIndices.Count ? pathNodeIndices[i + 1] : -1;
                var segmentDesiredStart = t;
                var segment = ReserveSegmentTransit(
                    job, mutations, prev, curr, edge, toNode.Kind, curr, nextNode, t);
                t = segment.NextSegmentStartTime;

                plan.SegmentSchedule.Add(new ConveyorSegmentScheduleEntry
                {
                    FromNodeIndex = prev,
                    ToNodeIndex = curr,
                    SlotIndex = segment.EntrySlotIndex,
                    DesiredEntrySimTime = segmentDesiredStart,
                    EntrySimTime = segment.EntryTime,
                    ExitSimTime = segment.ExitTime,
                    OccupancyEndSimTime = segment.OccupancyEndTime,
                    StopArriveSimTimes = segment.StopArriveTimes,
                });

                if (!trackedInfeedRelease && i == 1)
                {
                    plan.InfeedPhysicalReleaseSimTime = segment.EntryTime
                        + ConveyorMapMath.GetCargoTailClearanceSeconds(_topology.Map, edge);
                    trackedInfeedRelease = true;
                }
            }

            ReconcileJunctionStopReservations(plan.SegmentSchedule);
            plan.EndSimTime = t;
            return new TransitBuildResult(plan, mutations);
        }

        #endregion

        #region 路口占用补全

        /// <summary>
        /// 路径全部路段规划完成后，按最终时刻表补全路口停留点占用（含驶入后等待与驶出 hop）。
        /// 单段规划时无法预知下一段阻塞，此前可能只预约了驶入 hop 而未覆盖路口等待窗。
        /// </summary>
        private void ReconcileJunctionStopReservations(IReadOnlyList<ConveyorSegmentScheduleEntry> schedule)
        {
            if (schedule == null || schedule.Count == 0)
            {
                return;
            }

            for (var i = 0; i < schedule.Count; i++)
            {
                ReconcileJunctionHoldAt(schedule, i);
            }
        }

        /// <summary>在相邻下一段已预约后，补全该路口 zone 的占用窗。</summary>
        public void ReconcileJunctionHoldAt(
            IReadOnlyList<ConveyorSegmentScheduleEntry> schedule,
            int junctionSegmentIndex)
        {
            if (schedule == null
                || junctionSegmentIndex < 0
                || junctionSegmentIndex >= schedule.Count)
            {
                return;
            }

            var seg = schedule[junctionSegmentIndex];
            if (seg.ToNodeIndex < 0
                || seg.ToNodeIndex >= _topology.Map.Nodes.Length
                || _topology.GetNode(seg.ToNodeIndex).Kind != SimConveyorNodeKind.Junction)
            {
                return;
            }

            var stops = seg.StopArriveSimTimes;
            if (stops == null || stops.Length == 0)
            {
                return;
            }

            if (!_topology.TryGetEdge(seg.FromNodeIndex, seg.ToNodeIndex, out var edge))
            {
                return;
            }

            var map = _topology.Map;
            var hop = ConveyorMapMath.GetZoneHopSeconds(map, edge);
            if (hop <= 1e-9f)
            {
                return;
            }

            ConveyorSegmentScheduleEntry? nextSeg = junctionSegmentIndex + 1 < schedule.Count
                ? schedule[junctionSegmentIndex + 1]
                : null;
            if (!JunctionSubTaskTiming.TryGetJunctionHoldWindow(
                    seg,
                    nextSeg,
                    _topology,
                    map,
                    hop,
                    out var holdStart,
                    out var holdEnd))
            {
                return;
            }

            var junctionStopId = _topology.GetJunctionZoneResourceId(seg.ToNodeIndex);
            _reservations.TryReserve(junctionStopId, holdStart, holdEnd, out _);
        }

        #endregion

        #region 单段预约（按终点类型分支）

        private readonly struct SegmentReservation
        {
            public readonly double EntryTime;
            public readonly double ExitTime;
            public readonly double OccupancyEndTime;
            public readonly double NextSegmentStartTime;
            public readonly int EntrySlotIndex;
            public readonly double[] StopArriveTimes;
            public readonly SegmentMetrics Metrics;

            public SegmentReservation(
                double entryTime,
                double exitTime,
                double occupancyEndTime,
                double nextSegmentStartTime,
                int entrySlotIndex,
                double[] stopArriveTimes,
                SegmentMetrics metrics)
            {
                EntryTime = entryTime;
                ExitTime = exitTime;
                OccupancyEndTime = occupancyEndTime;
                NextSegmentStartTime = nextSegmentStartTime;
                EntrySlotIndex = entrySlotIndex;
                StopArriveTimes = stopArriveTimes;
                Metrics = metrics;
            }
        }

        private readonly struct SegmentMetrics
        {
            public readonly double ServiceDelta;
            public readonly double EntryWaitDelta;
            public readonly string EntryWaitResourceId;
            public readonly double DownstreamWaitDelta;
            public readonly string DownstreamWaitResourceId;

            public SegmentMetrics(
                double serviceDelta,
                double entryWaitDelta,
                string entryWaitResourceId,
                double downstreamWaitDelta,
                string downstreamWaitResourceId)
            {
                ServiceDelta = serviceDelta;
                EntryWaitDelta = entryWaitDelta;
                EntryWaitResourceId = entryWaitResourceId;
                DownstreamWaitDelta = downstreamWaitDelta;
                DownstreamWaitResourceId = downstreamWaitResourceId;
            }
        }

        private SegmentReservation ReserveSegmentTransit(
            WarehouseJob job,
            TransitMutationAccumulator mutations,
            int fromNode,
            int toNode,
            SimConveyorMapEdge edge,
            SimConveyorNodeKind toKind,
            int toNodeIndex,
            int nextNodeIndex,
            double desiredStart)
        {
            var hop = ConveyorMapMath.GetZoneHopSeconds(_topology.Map, edge);
            if (hop <= 1e-9f)
            {
                var immediate = desiredStart;
                var metrics = BuildSegmentMetrics(
                    desiredStart,
                    immediate,
                    entryWaitResourceId: null,
                    downstreamEnter: immediate,
                    downstreamIdealEnter: immediate,
                    downstreamWaitResourceId: null,
                    serviceDelta: 0);
                return new SegmentReservation(
                    immediate, immediate, immediate, immediate, 0, Array.Empty<double>(), metrics);
            }

            var capacity = _topology.Map.GetEdgeCapacity(edge);
            var slotIds = ConveyorMapMath.BuildSegmentSlotIds(_topology.Map, fromNode, toNode, capacity);

            return toKind switch
            {
                SimConveyorNodeKind.PickupPoint => ReservePickupSegment(
                    job, mutations, toNodeIndex, slotIds, capacity, hop, desiredStart),
                SimConveyorNodeKind.Junction => ReserveZpaSegmentWithDownstreamStop(
                    mutations,
                    slotIds,
                    capacity,
                    hop,
                    desiredStart,
                    toNodeIndex,
                    nextNodeIndex,
                    _topology.GetJunctionZoneResourceId(toNodeIndex)),
                _ => ReservePlainSegment(mutations, slotIds, capacity, hop, desiredStart),
            };
        }

        /// <summary>路段终点为停留点（路口）：slot-0 下游即该路口 ZPA 停留点，无额外互斥判断。</summary>
        private SegmentReservation ReserveZpaSegmentWithDownstreamStop(
            TransitMutationAccumulator mutations,
            string[] slotIds,
            int capacity,
            float hop,
            double desiredStart,
            int junctionNodeIndex,
            int nextNodeIndex,
            string downstreamStopId)
        {
            var entrySlot = capacity - 1;
            var stopArrives = ScheduleZpaStopChain(
                slotIds,
                capacity,
                hop,
                desiredStart,
                downstreamStopId,
                out var downstreamEnter,
                out _,
                transaction: null,
                junctionNodeIndex: junctionNodeIndex,
                nextNodeIndex: nextNodeIndex);
            var occupancyEnd = ComputeJunctionOccupancyEnd(
                junctionNodeIndex,
                nextNodeIndex,
                downstreamEnter,
                hop);
            var metrics = BuildSegmentMetrics(
                desiredStart,
                stopArrives[entrySlot],
                slotIds[entrySlot],
                downstreamEnter,
                stopArrives[0] + hop,
                downstreamStopId,
                downstreamEnter - stopArrives[entrySlot] + hop);
            mutations.ApplySegmentMetrics(metrics);
            return new SegmentReservation(
                stopArrives[entrySlot],
                downstreamEnter,
                occupancyEnd,
                occupancyEnd,
                entrySlot,
                stopArrives,
                metrics);
        }

        /// <summary>
        /// 路口驶出占用尾端：从路口中心驶出至下一段入口停留点抵达（仅判相邻一段）。
        /// </summary>
        private double ComputeJunctionOccupancyEnd(
            int junctionNodeIndex,
            int nextNodeIndex,
            double downstreamEnter,
            float hop)
        {
            if (nextNodeIndex < 0
                || !_topology.TryGetEdge(junctionNodeIndex, nextNodeIndex, out var nextEdge))
            {
                return downstreamEnter;
            }

            var nextHop = ConveyorMapMath.GetZoneHopSeconds(_topology.Map, nextEdge);
            if (nextHop <= 1e-9f)
            {
                nextHop = hop;
            }

            var nextCapacity = _topology.Map.GetEdgeCapacity(nextEdge);
            if (nextCapacity <= 0)
            {
                return downstreamEnter + nextHop;
            }

            var nextSlotIds = ConveyorMapMath.BuildSegmentSlotIds(
                _topology.Map,
                junctionNodeIndex,
                nextNodeIndex,
                nextCapacity);
            var nextEntrySlot = nextCapacity - 1;
            if (nextEntrySlot < 0 || nextEntrySlot >= nextSlotIds.Length)
            {
                return downstreamEnter + nextHop;
            }

            var nextEntryHopStart = _reservations.GetEarliestFreeTimeForDuration(
                nextSlotIds[nextEntrySlot],
                downstreamEnter,
                nextHop);
            return nextEntryHopStart + nextHop;
        }

        /// <summary>普通路段：ZPA 链式推进至终点节点。</summary>
        private SegmentReservation ReservePlainSegment(
            TransitMutationAccumulator mutations,
            string[] slotIds,
            int capacity,
            float hop,
            double desiredStart)
        {
            var entrySlot = capacity - 1;
            var stopArrives = ScheduleZpaStopChain(
                slotIds, capacity, hop, desiredStart, downstreamResourceId: null, out var downstreamEnter, out _);
            var exitTime = downstreamEnter;
            var occupancyEnd = downstreamEnter + hop;
            var metrics = BuildSegmentMetrics(
                desiredStart,
                stopArrives[entrySlot],
                slotIds[entrySlot],
                downstreamEnter,
                stopArrives[0] + hop,
                downstreamWaitResourceId: null,
                serviceDelta: exitTime - stopArrives[entrySlot]);
            mutations.ApplySegmentMetrics(metrics);
            return new SegmentReservation(
                stopArrives[entrySlot],
                exitTime,
                occupancyEnd,
                exitTime,
                entrySlot,
                stopArrives,
                metrics);
        }

        /// <summary>取货点前最后一段：slot-0 下游为取货点停留区。</summary>
        private SegmentReservation ReservePickupSegment(
            WarehouseJob job,
            TransitMutationAccumulator mutations,
            int pickupNode,
            string[] slotIds,
            int capacity,
            float hop,
            double desiredStart)
        {
            ref var pickup = ref _topology.GetNode(pickupNode);
            var pickupZoneId = BuildPickupZoneResourceId(pickup, pickupNode, _bindings, _topology);
            var workDuration = ComputeStackerWorkSeconds(job, pickup);
            var stackerResources = BuildStackerResourceIds(pickup.StackerId, job.TargetSlot.Column);
            var desired = desiredStart;
            var entrySlot = capacity - 1;

            for (var iter = 0; iter < 128; iter++)
            {
                var transaction = new ReservationTransaction(_reservations);
                double PickupDwellAfterEnter(double enter)
                {
                    var stackerStart = stackerResources.Length > 0
                        ? _reservations.QueryEarliestStartAll(enter, workDuration, stackerResources)
                        : enter;
                    return stackerStart + workDuration - enter;
                }

                var stopArrives = ScheduleZpaStopChain(
                    slotIds,
                    capacity,
                    hop,
                    desired,
                    pickupZoneId,
                    out var pickupEnter,
                    out _,
                    transaction,
                    downstreamDwellResolver: PickupDwellAfterEnter);
                var stackerStart = stackerResources.Length > 0
                    ? _reservations.QueryEarliestStartAll(pickupEnter, workDuration, stackerResources)
                    : pickupEnter;
                var holdEnd = stackerStart + workDuration;

                if (stackerResources.Length > 0)
                {
                    foreach (var resourceId in stackerResources)
                    {
                        transaction.TryReserve(resourceId, stackerStart, holdEnd, out _);
                    }
                }

                transaction.Commit();
                mutations.SetStackerReserve(stackerStart, holdEnd);
                CommitInboundStackerBooking(job, pickup, holdEnd);

                var metrics = BuildSegmentMetrics(
                    desiredStart,
                    stopArrives[entrySlot],
                    slotIds[entrySlot],
                    pickupEnter,
                    stopArrives[0] + hop,
                    pickupZoneId,
                    pickupEnter - stopArrives[entrySlot]);
                mutations.ApplySegmentMetrics(metrics);
                var occupancyEnd = pickupEnter + hop;
                return new SegmentReservation(
                    stopArrives[entrySlot],
                    pickupEnter,
                    occupancyEnd,
                    pickupEnter,
                    entrySlot,
                    stopArrives,
                    metrics);
            }

            return ReservePlainSegment(mutations, slotIds, capacity, hop, desiredStart);
        }

        #endregion

        #region ZPA 停留点链（计算 + 写入预约表）

        /// <summary>
        /// ZPA：按停留点顺序单次写入预约表（FIFO）。
        /// 停留只占用当前停留点；移动至下一停留点只占用下一停留点。
        /// </summary>
        private double[] ScheduleZpaStopChain(
            string[] slotIds,
            int capacity,
            float hop,
            double desiredStart,
            string downstreamResourceId,
            out double downstreamEnter,
            out double downstreamReservationStart,
            ReservationTransaction transaction = null,
            double? downstreamHoldSeconds = null,
            Func<double, double> downstreamDwellResolver = null,
            int junctionNodeIndex = -1,
            int nextNodeIndex = -1)
        {
            var stopArrives = ComputeZpaStopArrivals(
                slotIds,
                capacity,
                hop,
                desiredStart,
                downstreamResourceId,
                out downstreamEnter,
                junctionNodeIndex,
                nextNodeIndex,
                downstreamDwellResolver);
            downstreamReservationStart = downstreamEnter - hop;

            if (capacity > 0)
            {
                var entrySlot = capacity - 1;
                var entryArrive = stopArrives[entrySlot];
                var entryMoveStart = entryArrive - hop;
                if (entryArrive > entryMoveStart + 1e-9)
                {
                    TryReserve(transaction, slotIds[entrySlot], entryMoveStart, entryArrive, out _);
                }
            }

            for (var s = capacity - 1; s >= 1; s--)
            {
                var arrive = stopArrives[s];
                var moveOutStart = stopArrives[s - 1] - hop;
                if (moveOutStart > arrive + 1e-9)
                {
                    TryReserve(transaction, slotIds[s], arrive, moveOutStart, out _);
                }

                if (stopArrives[s - 1] > moveOutStart + 1e-9)
                {
                    TryReserve(transaction, slotIds[s - 1], moveOutStart, stopArrives[s - 1], out _);
                }
            }

            if (capacity > 0)
            {
                var s0Arrive = stopArrives[0];
                var moveOutFromS0 = downstreamEnter - hop;
                if (moveOutFromS0 > s0Arrive + 1e-9)
                {
                    TryReserve(transaction, slotIds[0], s0Arrive, moveOutFromS0, out var reservedStart);
                    if (reservedStart > s0Arrive + 1e-9)
                    {
                        var dwellDuration = moveOutFromS0 - s0Arrive;
                        stopArrives[0] = reservedStart;
                        moveOutFromS0 = reservedStart + dwellDuration;
                        downstreamEnter = moveOutFromS0 + hop;
                    }
                }

                if (string.IsNullOrEmpty(downstreamResourceId)
                    && downstreamEnter + hop > moveOutFromS0 + 1e-9)
                {
                    TryReserve(transaction, slotIds[0], moveOutFromS0, downstreamEnter + hop, out _);
                }
            }

            var originalDownstreamEnter = downstreamEnter;

            if (!string.IsNullOrEmpty(downstreamResourceId))
            {
                if (junctionNodeIndex >= 0)
                {
                    ReserveJunctionDownstreamChain(
                        transaction,
                        downstreamResourceId,
                        junctionNodeIndex,
                        nextNodeIndex,
                        hop,
                        ref downstreamEnter,
                        out downstreamReservationStart);
                }
                else
                {
                    ReservePickupDownstreamChain(
                        transaction,
                        downstreamResourceId,
                        downstreamEnter,
                        hop,
                        downstreamHoldSeconds,
                        downstreamDwellResolver,
                        ref downstreamEnter,
                        out downstreamReservationStart);
                }

                // 如果下游进入时刻被顺延（例如堆垛机或路口繁忙），
                // 货箱在 slot 0 的实际停留时间变长，必须更新 slot 0 的预约区间。
                if (capacity > 0 && downstreamEnter > originalDownstreamEnter + 1e-9)
                {
                    var s0Arrive = stopArrives[0];
                    var oldMoveOutFromS0 = originalDownstreamEnter - hop;
                    var newMoveOutFromS0 = downstreamEnter - hop;

                    if (oldMoveOutFromS0 > s0Arrive + 1e-9)
                    {
                        ReleaseReservation(transaction, slotIds[0], s0Arrive, oldMoveOutFromS0);
                    }

                    if (newMoveOutFromS0 > s0Arrive + 1e-9)
                    {
                        TryReserve(transaction, slotIds[0], s0Arrive, newMoveOutFromS0, out var reservedStart);
                        if (reservedStart > s0Arrive + 1e-9)
                        {
                            var dwellDuration = newMoveOutFromS0 - s0Arrive;
                            stopArrives[0] = reservedStart;
                            newMoveOutFromS0 = reservedStart + dwellDuration;
                            downstreamEnter = newMoveOutFromS0 + hop;
                        }
                    }
                }
            }

            return stopArrives;
        }

        /// <summary>驶入路口、路口等待、驶出 hop：整段作为连续占用写入路口停留点，避免分段预约留下空档。</summary>
        private void ReserveJunctionDownstreamChain(
            ReservationTransaction transaction,
            string junctionStopId,
            int junctionNodeIndex,
            int nextNodeIndex,
            float hop,
            ref double downstreamEnter,
            out double downstreamReservationStart)
        {
            ReserveJunctionStopHold(
                transaction,
                junctionStopId,
                junctionNodeIndex,
                nextNodeIndex,
                hop,
                ref downstreamEnter,
                out downstreamReservationStart);

            var exitHopStart = ComputeJunctionExitHopStart(
                junctionNodeIndex,
                nextNodeIndex,
                downstreamEnter,
                hop);
            var occupancyEnd = ComputeJunctionOccupancyEnd(
                junctionNodeIndex,
                nextNodeIndex,
                downstreamEnter,
                hop);

            if (nextNodeIndex >= 0
                && _topology.TryGetEdge(junctionNodeIndex, nextNodeIndex, out var nextEdge))
            {
                var nextHop = ConveyorMapMath.GetZoneHopSeconds(_topology.Map, nextEdge);
                if (nextHop <= 1e-9f)
                {
                    nextHop = hop;
                }

                var nextCapacity = _topology.Map.GetEdgeCapacity(nextEdge);
                if (nextCapacity > 0)
                {
                    var nextSlotIds = ConveyorMapMath.BuildSegmentSlotIds(
                        _topology.Map,
                        junctionNodeIndex,
                        nextNodeIndex,
                        nextCapacity);
                    var nextEntrySlot = nextCapacity - 1;
                    if (nextEntrySlot >= 0 && nextEntrySlot < nextSlotIds.Length
                        && occupancyEnd > exitHopStart + 1e-9)
                    {
                        TryReserve(transaction, nextSlotIds[nextEntrySlot], exitHopStart, occupancyEnd, out _);
                    }
                }
            }
        }

        private void ReserveJunctionStopHold(
            ReservationTransaction transaction,
            string junctionStopId,
            int junctionNodeIndex,
            int nextNodeIndex,
            float hop,
            ref double downstreamEnter,
            out double downstreamReservationStart)
        {
            for (var iter = 0; iter < 16; iter++)
            {
                var holdStart = downstreamEnter - hop;
                var exitHopStart = ComputeJunctionExitHopStart(
                    junctionNodeIndex,
                    nextNodeIndex,
                    downstreamEnter,
                    hop);
                var holdEnd = exitHopStart;
                TryReserve(transaction, junctionStopId, holdStart, holdEnd, out var reservedStart);
                if (reservedStart > holdStart + 1e-9)
                {
                    ReleaseReservation(transaction, junctionStopId, reservedStart, reservedStart + (holdEnd - holdStart));
                    downstreamEnter = reservedStart + hop;
                    continue;
                }

                break;
            }

            downstreamReservationStart = downstreamEnter - hop;
        }

        /// <summary>取货点：驶入停留区 hop + 持有时长。</summary>
        private void ReservePickupDownstreamChain(
            ReservationTransaction transaction,
            string downstreamResourceId,
            double downstreamEnter,
            float hop,
            double? downstreamHoldSeconds,
            Func<double, double> downstreamDwellResolver,
            ref double downstreamEnterRef,
            out double downstreamReservationStart)
        {
            for (var iter = 0; iter < 16; iter++)
            {
                var enterReserveStart = downstreamEnterRef - hop;
                var downstreamDwell = ResolveDownstreamDwellAfterEnter(
                    downstreamEnterRef,
                    hop,
                    downstreamHoldSeconds,
                    downstreamDwellResolver);
                var serviceEnd = downstreamEnterRef + downstreamDwell;
                TryReserve(
                    transaction,
                    downstreamResourceId,
                    enterReserveStart,
                    serviceEnd,
                    out var reservedStart);
                if (reservedStart > enterReserveStart + 1e-9)
                {
                    var shiftedEnd = reservedStart + (serviceEnd - enterReserveStart);
                    ReleaseReservation(transaction, downstreamResourceId, reservedStart, shiftedEnd);
                    downstreamEnterRef = reservedStart + hop;
                    continue;
                }

                break;
            }

            downstreamReservationStart = downstreamEnterRef - hop;
        }

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

            var nextHop = ConveyorMapMath.GetZoneHopSeconds(_topology.Map, nextEdge);
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

        /// <summary>
        /// 仅计算各停留点到达时刻（不写预约表）。
        /// 下游路口/取货点约束会反向推迟上游到达时刻，避免把预约冲突起点误当作到达时刻。
        /// </summary>
        private double[] ComputeZpaStopArrivals(
            string[] slotIds,
            int capacity,
            float hop,
            double desiredStart,
            string downstreamResourceId,
            out double downstreamEnter,
            int junctionNodeIndex = -1,
            int nextNodeIndex = -1,
            Func<double, double> pickupDwellResolver = null)
        {
            var stopArrives = new double[capacity];
            if (capacity <= 0)
            {
                downstreamEnter = desiredStart;
                return stopArrives;
            }

            var entrySlot = capacity - 1;
            var entryHopStart = _reservations.GetEarliestFreeTimeForDuration(
                slotIds[entrySlot],
                desiredStart,
                hop);
            stopArrives[entrySlot] = entryHopStart + hop;

            for (var s = capacity - 1; s >= 1; s--)
            {
                var hopStart = _reservations.GetEarliestFreeTimeForDuration(
                    slotIds[s - 1],
                    stopArrives[s],
                    hop);
                stopArrives[s - 1] = hopStart + hop;
            }

            var idealDownstream = stopArrives[0] + hop;
            if (string.IsNullOrEmpty(downstreamResourceId))
            {
                downstreamEnter = idealDownstream;
            }
            else if (junctionNodeIndex >= 0)
            {
                downstreamEnter = ResolveJunctionDownstreamEnter(
                    downstreamResourceId,
                    stopArrives[0],
                    hop,
                    junctionNodeIndex,
                    nextNodeIndex);
            }
            else if (pickupDwellResolver != null)
            {
                downstreamEnter = ResolvePickupDownstreamEnter(
                    downstreamResourceId,
                    stopArrives[0],
                    hop,
                    pickupDwellResolver);
            }
            else
            {
                downstreamEnter = _reservations.GetEarliestFreeTimeForDuration(
                    downstreamResourceId,
                    stopArrives[0],
                    hop) + hop;
            }

            ReconcileS0DwellAvailability(
                stopArrives,
                slotIds,
                hop,
                ref downstreamEnter,
                downstreamResourceId,
                junctionNodeIndex,
                nextNodeIndex);

            return stopArrives;
        }

        /// <summary>
        /// S0 整段停留 [stopArrives[0], downstreamEnter-hop) 必须与预约表已有占用不重叠；
        /// 若理想到达时刻冲突，则推迟 S0 到达并联动下游进入时刻（保留停留时长语义）。
        /// </summary>
        private void ReconcileS0DwellAvailability(
            double[] stopArrives,
            string[] slotIds,
            float hop,
            ref double downstreamEnter,
            string downstreamResourceId,
            int junctionNodeIndex,
            int nextNodeIndex)
        {
            if (stopArrives == null || stopArrives.Length == 0 || slotIds == null || slotIds.Length == 0)
            {
                return;
            }

            var moveOutFromS0 = downstreamEnter - hop;
            if (moveOutFromS0 <= stopArrives[0] + 1e-9)
            {
                return;
            }

            for (var iter = 0; iter < 16; iter++)
            {
                var dwellDuration = moveOutFromS0 - stopArrives[0];
                if (dwellDuration <= 1e-9)
                {
                    break;
                }

                var actualS0Start = _reservations.GetEarliestFreeTimeForDuration(
                    slotIds[0],
                    stopArrives[0],
                    dwellDuration);
                if (actualS0Start <= stopArrives[0] + 1e-9)
                {
                    break;
                }

                var dwellShift = actualS0Start - stopArrives[0];
                stopArrives[0] = actualS0Start;
                downstreamEnter += dwellShift;
                moveOutFromS0 = downstreamEnter - hop;

                if (!string.IsNullOrEmpty(downstreamResourceId))
                {
                    var resolvedDownstream = junctionNodeIndex >= 0
                        ? ResolveJunctionDownstreamEnter(
                            downstreamResourceId,
                            stopArrives[0],
                            hop,
                            junctionNodeIndex,
                            nextNodeIndex)
                        : _reservations.GetEarliestFreeTimeForDuration(
                              downstreamResourceId,
                              stopArrives[0],
                              hop) + hop;
                    if (resolvedDownstream > downstreamEnter + 1e-9)
                    {
                        downstreamEnter = resolvedDownstream;
                        moveOutFromS0 = downstreamEnter - hop;
                    }
                }
            }
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

        /// <summary>按整段出库口占用（驶入 hop + 发运服务）反推可进入时刻。</summary>
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

        #endregion

        #region 预约表辅助

        private void TryReserve(
            ReservationTransaction transaction,
            string resourceId,
            double start,
            double end,
            out double reservedStart)
        {
            if (transaction != null)
            {
                transaction.TryReserve(resourceId, start, end, out reservedStart);
                return;
            }

            _reservations.TryReserve(resourceId, start, end, out reservedStart);
        }

        private void ReleaseReservation(
            ReservationTransaction transaction,
            string resourceId,
            double start,
            double end)
        {
            if (transaction != null)
            {
                transaction.Release(resourceId, start, end);
                return;
            }

            _reservations.Release(resourceId, start, end);
        }

        private static SegmentMetrics BuildSegmentMetrics(
            double desiredStart,
            double entryTime,
            string entryWaitResourceId,
            double downstreamEnter,
            double downstreamIdealEnter,
            string downstreamWaitResourceId,
            double serviceDelta)
        {
            var entryWait = Math.Max(0d, entryTime - desiredStart);
            var downstreamWait = Math.Max(0d, downstreamEnter - downstreamIdealEnter);
            return new SegmentMetrics(
                serviceDelta,
                entryWait,
                entryWaitResourceId,
                downstreamWait,
                downstreamWaitResourceId);
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

        private static string BuildPickupZoneResourceId(
            in SimConveyorMapNode pickupNode,
            int pickupNodeIndex,
            IWarehouseSimulationBindings _,
            ConveyorMapTopology __) =>
            SimEntityNaming.PickupResourceId(pickupNode, pickupNodeIndex);

        #endregion
    }
}
