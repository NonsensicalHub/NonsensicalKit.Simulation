using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <content>
    /// 出库阶段：等候队列 → 选源货位/堆垛机/取货点 → 堆垛机取移放 → 输送至出库口 → 发运服务。
    /// </content>
    public sealed partial class StackerWarehouseSimulator
    {
        /// <summary>合并堆垛机释放等路径上的出库等候唤醒，不与真实出库口序号冲突。</summary>
        private const int OutfeedQueueFillWakePort = -1;

        private bool[] _outfeedCargoOccupied;
        private int[] _outfeedReservationCounts;
        private double[] _outfeedServiceFreeByPort;
        private bool[] _outfeedPortDispatchScheduled;
        private int[] _outfeedPortOrderScratch;
        private bool _outfeedQueueFillScheduled;
        private int _outfeedRoundRobinCursor;
        private readonly HashSet<GridIndex> _outboundReservedSlots = new();
        /// <summary>已在交互点放货后、输送接走前释放堆垛机负载计数的出库任务。</summary>
        private readonly HashSet<int> _outboundStackerReleasedAfterPickup = new();
        /// <summary>出库交互点当前预约窗（替换更新，避免叠加 86400s 占位导致仿真时间爆炸）。</summary>
        private readonly Dictionary<int, (string ResourceId, double Start, double End)> _outboundPickupHolds = new();

        /// <summary>交互点等待出库输送路径时的占位时长（会被选路成功后的真实离开时刻替换）。</summary>
        private const double OutboundPickupPendingRouteHoldSeconds = 3600d;

        private void InitOutfeedPorts()
        {
            var outfeedCount = _conveyorTopology.OutfeedNodeIndices?.Count ?? 0;
            if (outfeedCount <= 0)
            {
                _outfeedCargoOccupied = Array.Empty<bool>();
                _outfeedReservationCounts = Array.Empty<int>();
                _outfeedServiceFreeByPort = Array.Empty<double>();
                _outfeedPortDispatchScheduled = Array.Empty<bool>();
                _outfeedPortOrderScratch = Array.Empty<int>();
                return;
            }

            _outfeedCargoOccupied = new bool[outfeedCount];
            _outfeedReservationCounts = new int[outfeedCount];
            _outfeedServiceFreeByPort = new double[outfeedCount];
            _outfeedPortDispatchScheduled = new bool[outfeedCount];
            _outfeedPortOrderScratch = new int[Math.Max(1, outfeedCount)];
            _outfeedRoundRobinCursor = 0;
            _outfeedQueueFillScheduled = false;
        }

        /// <summary>出库等候队列有货时，为每个出库口排定首次建单。</summary>
        private void ScheduleInitialOutfeedDispatches()
        {
            var outfeedCount = _conveyorTopology.OutfeedNodeIndices?.Count ?? 0;
            for (var port = 0; port < outfeedCount; port++)
            {
                ScheduleOutfeedPortDispatch(port, _clock.Now);
            }
        }

        private void OnOutfeedPortDispatch(int portIndex)
        {
            if (portIndex == OutfeedQueueFillWakePort)
            {
                _outfeedQueueFillScheduled = false;
                TryFillOutboundFromQueue();
                return;
            }

            if (_outfeedPortDispatchScheduled == null
                || portIndex < 0
                || portIndex >= _outfeedPortDispatchScheduled.Length)
            {
                return;
            }

            _outfeedPortDispatchScheduled[portIndex] = false;
            TryFillOutboundFromQueue();
        }

        /// <summary>堆垛机释放等高频路径上合并出库等候扫描，避免同步重复全库探测。</summary>
        private void ScheduleOutfeedQueueFill(double when)
        {
            if (_outboundWaitingCount <= 0 || _outfeedQueueFillScheduled)
            {
                return;
            }

            _outfeedQueueFillScheduled = true;
            _queue.Enqueue(new ScheduledSimEvent(when, SimEventType.OutfeedPortDispatch, 0, OutfeedQueueFillWakePort));
        }

        private void ScheduleOutfeedPortDispatch(int portIndex, double when)
        {
            if (_outfeedPortDispatchScheduled == null
                || portIndex < 0
                || portIndex >= _outfeedPortDispatchScheduled.Length)
            {
                return;
            }

            if (_outfeedPortDispatchScheduled[portIndex])
            {
                return;
            }

            _outfeedPortDispatchScheduled[portIndex] = true;
            _queue.Enqueue(new ScheduledSimEvent(when, SimEventType.OutfeedPortDispatch, 0, portIndex));
        }

        /// <summary>
        /// 从等候队列向可用出库口建单：每成功一箱创建 <see cref="WarehouseJob"/> 并进入堆垛机阶段。
        /// </summary>
        /// <remarks>
        /// 循环直至无货可放或无可用口。阻塞时由出库口物理释放或堆垛机释放唤醒，不做定时轮询。
        /// </remarks>
        private void TryFillOutboundFromQueue()
        {
            if (_outboundWaitingCount <= 0
                || _conveyorTopology.OutfeedNodeIndices == null
                || _conveyorTopology.OutfeedNodeIndices.Count == 0)
            {
                return;
            }

            while (_outboundWaitingCount > 0)
            {
                if (!TryPlaceOneOutboundFromQueue())
                {
                    return;
                }
            }
        }

        /// <summary>单次唤醒内为空闲堆垛机建单；出库口在输送选路时再占，交互点未释放前不再派同机任务。</summary>
        private bool TryPlaceOneOutboundFromQueue()
        {
            if (!OutfeedPortSelector.TrySelectOutboundPlacement(
                    _conveyorTopology,
                    _bindings,
                    _slotAllocator,
                    _pickupReservationCounts,
                    _stackerActiveJobCounts,
                    _outboundReservedSlots,
                    out var slot,
                    out var stackerId,
                    out var pickupIndex,
                    out _))
            {
                if (!_slotAllocator.HasRetrievableOccupiedSlot(_bindings, _conveyorTopology))
                {
                    FailAllRemainingOutboundRequests();
                    return false;
                }

                WarehouseSimLog.Detail(
                    $"出库等候 {_outboundWaitingCount} 箱：暂无空闲堆垛机或交互点，" +
                    "待交互点释放后重试。");
                return false;
            }

            _outboundWaitingCount--;
            var job = new WarehouseJob(_nextJobId++, _clock.Now)
            {
                Direction = SimFlowDirection.Outbound,
                State = WarehouseJobState.WaitingStacker,
            };
            _jobs[job.JobId] = job;
            PlaceOutboundJob(job, slot, stackerId, pickupIndex);
            return true;
        }

        private void PlaceOutboundJob(
            WarehouseJob job,
            GridIndex slot,
            int stackerId,
            int pickupIndex)
        {
            job.TargetSlot = slot;
            job.HasSlot = true;
            job.AssignedStackerId = stackerId;
            job.PickupPointIndex = pickupIndex;
            job.OutfeedPortIndex = -1;

            _outboundReservedSlots.Add(slot);
            IncrementStackerActiveJobCount(stackerId);

            WarehouseSimLog.Info(() =>
                $"出库建单 job={job.JobId} stacker={stackerId} slot={slot} pickup={pickupIndex} " +
                $"outfeed=待选 t={_clock.Now:F2}s");

            job.ArrivalTime = _clock.Now;
            RecordPlayback(job, SimPlaybackPhase.Arrived, stackerId, slot);
            TryBeginStackerPhase(job);
        }

        private void FailAllRemainingOutboundRequests()
        {
            if (_outboundWaitingCount <= 0)
            {
                return;
            }

            var remaining = _outboundWaitingCount;
            _outboundWaitingCount = 0;
            _failedNoCargo += remaining;
            RecordFailure(SimJobFailureReason.OutboundQueueNoInventory, remaining);
            WarehouseSimLog.Info(
                $"无可用库存：{remaining} 箱出库请求失败 t={_clock.Now:F2}s");
        }

        private void FailJobNoCargo(
            WarehouseJob job,
            SimJobFailureReason reason,
            bool releaseResources = false)
        {
            if (job.State == WarehouseJobState.FailedNoCargo || job.State == WarehouseJobState.Completed)
            {
                return;
            }

            if (job.Direction == SimFlowDirection.Outbound)
            {
                var port = job.OutfeedPortIndex;
                ReleaseOutfeedPortReservation(job);
                if (port >= 0)
                {
                    ScheduleOutfeedPortDispatch(port, _clock.Now);
                }
            }

            if (releaseResources)
            {
                ReleaseOutboundSlotReservation(job);
            }

            ReleaseOutboundStackerAfterPickupDeparture(job);
            ClearOutboundPickupHold(job);

            job.State = WarehouseJobState.FailedNoCargo;
            _failedNoCargo++;
            RecordFailure(reason);
            WarehouseSimLog.Info(
                $"任务失败（{SimJobFailureReasonLabels.GetLabel(reason)}）job={job.JobId} t={_clock.Now:F2}s");
        }

        private void ReleaseOutboundSlotReservation(WarehouseJob job)
        {
            if (!job.HasSlot)
            {
                return;
            }

            _outboundReservedSlots.Remove(job.TargetSlot);
            job.HasSlot = false;
        }

        private void OnOutboundStackerPlaceComplete(WarehouseJob job)
        {
            job.PickupCompleteSimTime = _clock.Now;
            if (job.AssignedStackerId >= 0 && job.PickupPointIndex >= 0)
            {
                var pickupNode = _conveyorTopology.GetNode(job.PickupPointIndex);
                _stackerCarriage.SetCarriagePosition(job.AssignedStackerId, pickupNode.PickupRow, 0);
            }

            RecordPlayback(job, SimPlaybackPhase.StackerPlace, job.AssignedStackerId, job.TargetSlot);
            ReleaseOutboundSlotReservation(job);
            _slotAllocator.Release(job.TargetSlot);
            if (_inboundWaitingCount > 0 && !_inboundExhaustedAtInfeed)
            {
                ScheduleInfeedQueueFill(_clock.Now);
            }

            HoldOutboundPickupUntilConveyorDeparture(job);
            if (!TryBeginConveyor(job))
            {
                WarehouseSimLog.Detail(
                    $"出库交互点待发运 job={job.JobId} pickup={job.PickupPointIndex} t={_clock.Now:F2}s，" +
                    "暂无可用出库口，在交互点等待。");
            }
        }

        /// <summary>更新出库交互点占用：先释放旧窗再写入，避免预约表叠加长占位区间。</summary>
        internal void SetOutboundPickupHold(WarehouseJob job, double holdEnd)
        {
            if (job?.PickupPointIndex < 0)
            {
                return;
            }

            ref var pickupNode = ref _conveyorTopology.GetNode(job.PickupPointIndex);
            var resourceId = SimEntityNaming.PickupResourceId(pickupNode, job.PickupPointIndex);
            if (string.IsNullOrEmpty(resourceId))
            {
                return;
            }

            var holdStart = job.PickupCompleteSimTime > 0 ? job.PickupCompleteSimTime : _clock.Now;
            if (holdEnd <= holdStart + 1e-9)
            {
                return;
            }

            ClearOutboundPickupHold(job);
            _reservations.TryReserve(resourceId, holdStart, holdEnd, out _);
            _outboundPickupHolds[job.JobId] = (resourceId, holdStart, holdEnd);
        }

        internal void ClearOutboundPickupHold(WarehouseJob job)
        {
            if (job == null || !_outboundPickupHolds.TryGetValue(job.JobId, out var hold))
            {
                return;
            }

            _reservations.Release(hold.ResourceId, hold.Start, hold.End);
            _outboundPickupHolds.Remove(job.JobId);
        }

        /// <summary>
        /// 堆垛机放货后占用交互点，直至输送接走；无出库口时亦保持占用，待选路成功后收窄结束时刻。
        /// </summary>
        private void HoldOutboundPickupUntilConveyorDeparture(WarehouseJob job)
        {
            var holdStart = job.PickupCompleteSimTime > 0 ? job.PickupCompleteSimTime : _clock.Now;
            SetOutboundPickupHold(job, holdStart + OutboundPickupPendingRouteHoldSeconds);
        }

        /// <summary>货箱离开交互点（出库输送首段入口 slot 开始驶离）后释放堆垛机负载并尝试派下一出库任务。</summary>
        internal void ReleaseOutboundStackerAfterPickupDeparture(WarehouseJob job)
        {
            if (job == null || job.Direction != SimFlowDirection.Outbound)
            {
                return;
            }

            if (!_outboundStackerReleasedAfterPickup.Add(job.JobId))
            {
                return;
            }

            DecrementStackerActiveJobCount(job.AssignedStackerId);
            ClearOutboundPickupHold(job);
            ScheduleOutfeedQueueFill(_clock.Now);
        }

        internal void ScheduleOutboundPickupDeparture(WarehouseJob job, double departTime)
        {
            if (job == null || job.Direction != SimFlowDirection.Outbound)
            {
                return;
            }

            if (_outboundStackerReleasedAfterPickup.Contains(job.JobId))
            {
                return;
            }

            var when = Math.Max(_clock.Now, departTime);
            _queue.Enqueue(new ScheduledSimEvent(when, SimEventType.OutboundPickupDeparture, job.JobId));
        }

        /// <summary>出库首段路径边：堆垛机交互点侧入口停留点（最大 slot 序号）。</summary>
        internal static bool TryGetOutboundPickupEntrySlotIndex(
            IReadOnlyList<ConveyorPathZone> chain,
            out int entrySlotIndex)
        {
            entrySlotIndex = -1;
            if (chain == null)
            {
                return false;
            }

            for (var i = 0; i < chain.Count; i++)
            {
                var zone = chain[i];
                if (zone.Kind != ConveyorPathZoneKind.EdgeSlot || zone.PathEdgeIndex != 0)
                {
                    continue;
                }

                if (zone.SlotIndex > entrySlotIndex)
                {
                    entrySlotIndex = zone.SlotIndex;
                }
            }

            return entrySlotIndex >= 0;
        }

        internal static bool IsOutboundPickupEntryZone(
            WarehouseJob job,
            in ConveyorPathZone zone,
            IReadOnlyList<ConveyorPathZone> chain) =>
            job != null
            && job.Direction == SimFlowDirection.Outbound
            && job.PickupPointIndex >= 0
            && job.PickupCompleteSimTime > 0
            && zone.Kind == ConveyorPathZoneKind.EdgeSlot
            && zone.PathEdgeIndex == 0
            && zone.FromNodeIndex == job.PickupPointIndex
            && TryGetOutboundPickupEntrySlotIndex(chain, out var entrySlot)
            && zone.SlotIndex == entrySlot;

        internal static bool IsOutboundPickupDepartureZone(WarehouseJob job, ConveyorPathZone completedZone) =>
            IsOutboundPickupEntryZone(job, in completedZone, job?.ConveyorPathZones);

        private void BeginOutfeed(WarehouseJob job)
        {
            var port = job.OutfeedPortIndex;
            if (port < 0 || _outfeedServiceFreeByPort == null || port >= _outfeedServiceFreeByPort.Length)
            {
                _queue.Enqueue(new ScheduledSimEvent(_clock.Now, SimEventType.JobCompleted, job.JobId));
                return;
            }

            ref var node = ref _conveyorTopology.GetNode(_conveyorTopology.OutfeedNodeIndices[port]);
            var duration = node.OutfeedServiceSeconds > 0f
                ? node.OutfeedServiceSeconds
                : _bindings.OutfeedServiceSeconds;
            var resourceId = SimEntityNaming.OutfeedServiceResourceId(node, port);

            var desired = _clock.Now;
            var timelineStart = Math.Max(desired, _outfeedServiceFreeByPort[port]);
            var start = _reservations.ReserveAtEarliestAll(timelineStart, duration, resourceId);
            var end = start + duration;
            _outfeedServiceFreeByPort[port] = end;

            var wait = start - desired;
            if (wait > 1e-9)
            {
                job.WaitTimeAccum += wait;
                job.LastWaitResource = resourceId;
            }

            job.ServiceTimeAccum += end - start;
            job.State = WarehouseJobState.WaitingOutfeed;
            job.PhaseStartTime = start;
            job.ScheduledCompleteTime = end;

            _outfeedCargoOccupied[port] = true;

            RecordSubTask(
                job,
                SimSubTaskKind.OutfeedService,
                start,
                end,
                job.AssignedStackerId,
                job.TargetSlot);

            _queue.Enqueue(new ScheduledSimEvent(end, SimEventType.OutfeedServiceComplete, job.JobId));
        }

        private void ReleaseOutfeedPortReservation(WarehouseJob job)
        {
            var port = job.OutfeedPortIndex;
            if (port < 0 || _outfeedReservationCounts == null || port >= _outfeedReservationCounts.Length)
            {
                return;
            }

            _outfeedReservationCounts[port] = Math.Max(0, _outfeedReservationCounts[port] - 1);
        }

        private void OnOutfeedComplete(WarehouseJob job)
        {
            var port = job.OutfeedPortIndex;
            if (port >= 0 && _outfeedReservationCounts != null && port < _outfeedReservationCounts.Length)
            {
                _outfeedReservationCounts[port] = Math.Max(0, _outfeedReservationCounts[port] - 1);
            }

            RecordPlayback(job, SimPlaybackPhase.OutfeedDone, job.AssignedStackerId, job.TargetSlot);
            RetryOutboundConveyorRouteWaiters();
            _queue.Enqueue(new ScheduledSimEvent(_clock.Now, SimEventType.JobCompleted, job.JobId));
            ScheduleOutfeedPortPhysicalRelease(job, _clock.Now + 0.5);
        }

        private void OnOutfeedPortPhysicalRelease(WarehouseJob job)
        {
            var port = job.OutfeedPortIndex;
            if (port >= 0
                && _outfeedCargoOccupied != null
                && port < _outfeedCargoOccupied.Length
                && _outfeedReservationCounts != null
                && _outfeedReservationCounts[port] <= 0)
            {
                _outfeedCargoOccupied[port] = false;
            }

            ScheduleOutfeedPortDispatch(port, _clock.Now);
        }

        private void ScheduleOutfeedPortPhysicalRelease(WarehouseJob job, double releaseTime)
        {
            var when = Math.Max(_clock.Now, releaseTime);
            _queue.Enqueue(new ScheduledSimEvent(when, SimEventType.OutfeedPortPhysicalRelease, job.JobId));
        }

        private bool TryCommitOutboundConveyorPath(WarehouseJob job)
        {
            if (job.ConveyorPathZones != null && job.ConveyorPathZones.Count > 0)
            {
                return true;
            }

            var previousOutfeedPort = job.OutfeedPortIndex;
            if (!ConveyorPathPlanner.TrySelectBestOutboundPath(
                    _conveyorTopology,
                    _bindings,
                    _strategy,
                    _reservations,
                    _outfeedReservationCounts,
                    job.PickupPointIndex,
                    job.OutfeedPortIndex,
                    _clock.Now,
                    out var outfeedPort,
                    out var path))
            {
                WarehouseSimLog.Warn(
                    $"出库输送选路失败 job={job.JobId} pickup={job.PickupPointIndex} " +
                    $"preferredOutfeed={job.OutfeedPortIndex} t={_clock.Now:F2}s");
                return false;
            }

            if (previousOutfeedPort != outfeedPort)
            {
                if (previousOutfeedPort >= 0
                    && _outfeedReservationCounts != null
                    && previousOutfeedPort < _outfeedReservationCounts.Length)
                {
                    _outfeedReservationCounts[previousOutfeedPort] =
                        Math.Max(0, _outfeedReservationCounts[previousOutfeedPort] - 1);
                }

                if (outfeedPort >= 0
                    && _outfeedReservationCounts != null
                    && outfeedPort < _outfeedReservationCounts.Length)
                {
                    _outfeedReservationCounts[outfeedPort]++;
                }
            }

            job.OutfeedPortIndex = outfeedPort;
            job.ConveyorPathNodeIndices = path;
            job.ConveyorSegmentSchedule ??= new List<ConveyorSegmentScheduleEntry>();
            job.ConveyorSegmentSchedule.Clear();
            job.ConveyorEdgeSubTasksRecorded = new HashSet<int>();
            job.ConveyorPathZones = ConveyorPathZoneChainBuilder.Build(
                path,
                _conveyorTopology,
                _bindings.ConveyorMap);
            if (job.ConveyorPathZones == null || job.ConveyorPathZones.Count == 0)
            {
                WarehouseSimLog.Warn(
                    $"出库输送 zone 链为空 job={job.JobId} pickup={job.PickupPointIndex} pathNodes={path?.Count ?? 0}");
                return false;
            }

            job.NextConveyorZoneIndex = 0;
            WarehouseSimLog.Info(() =>
                $"出库输送路径选定 job={job.JobId} outfeed={outfeedPort} 路径节点={path.Count} " +
                $"zone数={job.ConveyorPathZones.Count}");
            return true;
        }
    }
}
