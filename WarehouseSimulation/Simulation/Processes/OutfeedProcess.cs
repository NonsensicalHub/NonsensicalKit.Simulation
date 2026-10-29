using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>出库建单、出库口服务，以及整单完成记录。</summary>
    internal sealed class OutfeedProcess : ISimProcess
    {
        private const int QueueFillWakePort = -1;

        private readonly SimRunContext _ctx;
        private bool[] _cargoOccupied;
        private int[] _reservationCounts;
        private double[] _serviceFreeByPort;
        private bool[] _dispatchScheduled;
        private int[] _portOrderScratch;
        private bool _queueFillScheduled;
        private readonly HashSet<GridIndex> _reservedSlots = new();
        private readonly HashSet<int> _stackerReleasedAfterPickup = new();

        public OutfeedProcess(SimRunContext ctx) => _ctx = ctx;

        internal HashSet<GridIndex> ReservedSlots => _reservedSlots;

        public bool Handles(SimEventType type) =>
            type is SimEventType.OutfeedServiceComplete
                or SimEventType.OutfeedPortPhysicalRelease
                or SimEventType.OutfeedPortDispatch
                or SimEventType.JobCompleted;

        public void OnEvent(ScheduledSimEvent evt)
        {
            switch (evt.Type)
            {
                case SimEventType.OutfeedPortDispatch:
                    OnPortDispatch(evt.Payload);
                    break;
                case SimEventType.OutfeedServiceComplete:
                    if (_ctx.TryGetJob(evt.JobId, evt.Type, out var completeJob))
                    {
                        OnServiceComplete(completeJob);
                    }

                    break;
                case SimEventType.OutfeedPortPhysicalRelease:
                    if (_ctx.TryGetJob(evt.JobId, evt.Type, out var releaseJob))
                    {
                        OnPhysicalRelease(releaseJob);
                    }

                    break;
                case SimEventType.JobCompleted:
                    if (_ctx.TryGetJob(evt.JobId, evt.Type, out var completedJob))
                    {
                        OnJobCompleted(completedJob);
                    }

                    break;
            }
        }

        internal void InitPorts()
        {
            _reservedSlots.Clear();
            _stackerReleasedAfterPickup.Clear();
            _queueFillScheduled = false;
            var outfeedCount = _ctx.Topology.OutfeedNodeIndices?.Count ?? 0;
            if (outfeedCount <= 0)
            {
                _cargoOccupied = Array.Empty<bool>();
                _reservationCounts = Array.Empty<int>();
                _serviceFreeByPort = Array.Empty<double>();
                _dispatchScheduled = Array.Empty<bool>();
                _portOrderScratch = Array.Empty<int>();
                return;
            }

            _cargoOccupied = new bool[outfeedCount];
            _reservationCounts = new int[outfeedCount];
            _serviceFreeByPort = new double[outfeedCount];
            _dispatchScheduled = new bool[outfeedCount];
            _portOrderScratch = new int[Math.Max(1, outfeedCount)];
        }

        internal void ScheduleInitialDispatches()
        {
            var outfeedCount = _ctx.Topology.OutfeedNodeIndices?.Count ?? 0;
            for (var port = 0; port < outfeedCount; port++)
            {
                SchedulePortDispatch(port, _ctx.Clock.Now);
            }
        }

        internal void ScheduleQueueFill(double when)
        {
            if (_ctx.OutboundWaitingCount <= 0 || _queueFillScheduled)
            {
                return;
            }

            _queueFillScheduled = true;
            _ctx.Queue.Enqueue(new ScheduledSimEvent(when, SimEventType.OutfeedPortDispatch, 0, QueueFillWakePort));
        }

        internal void SchedulePortDispatch(int portIndex, double when)
        {
            if (_dispatchScheduled == null || portIndex < 0 || portIndex >= _dispatchScheduled.Length)
            {
                return;
            }

            if (_dispatchScheduled[portIndex])
            {
                return;
            }

            _dispatchScheduled[portIndex] = true;
            _ctx.Queue.Enqueue(new ScheduledSimEvent(when, SimEventType.OutfeedPortDispatch, 0, portIndex));
        }

        internal void Begin(WarehouseJob job)
        {
            var port = job.OutfeedPortIndex;
            if (port < 0 || _serviceFreeByPort == null || port >= _serviceFreeByPort.Length)
            {
                _ctx.Queue.Enqueue(new ScheduledSimEvent(_ctx.Clock.Now, SimEventType.JobCompleted, job.JobId));
                return;
            }

            ref var node = ref _ctx.Topology.GetNode(_ctx.Topology.OutfeedNodeIndices[port]);
            var duration = node.OutfeedServiceSeconds > 0f
                ? node.OutfeedServiceSeconds
                : _ctx.Bindings.OutfeedServiceSeconds;
            var resourceId = SimEntityNaming.OutfeedServiceResourceId(node, port);
            var desired = _ctx.Clock.Now;
            var timelineStart = Math.Max(desired, _serviceFreeByPort[port]);
            var start = _ctx.Reservations.ReserveAtEarliestAll(timelineStart, duration, resourceId);
            var end = start + duration;
            _serviceFreeByPort[port] = end;

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
            _cargoOccupied[port] = true;
            _ctx.RecordSubTask(
                job,
                SimSubTaskKind.OutfeedService,
                start,
                end,
                job.AssignedStackerId,
                job.TargetSlot);
            _ctx.Queue.Enqueue(new ScheduledSimEvent(end, SimEventType.OutfeedServiceComplete, job.JobId));
        }

        internal void OnStackerPlaceComplete(WarehouseJob job)
        {
            job.PickupCompleteSimTime = _ctx.Clock.Now;
            if (job.AssignedStackerId >= 0 && job.PickupPointIndex >= 0)
            {
                var pickupNode = _ctx.Topology.GetNode(job.PickupPointIndex);
                _ctx.SetStackerCarriagePosition(
                    job.AssignedStackerId,
                    pickupNode.PickupRow,
                    Math.Max(0, pickupNode.PickupLevel));
            }

            _ctx.RecordPlayback(job, SimPlaybackPhase.StackerPlace, job.AssignedStackerId, job.TargetSlot);
            ReleaseSlotReservation(job);
            _ctx.SlotAllocator.Release(job.TargetSlot);
            if (_ctx.InboundWaitingCount > 0 && !_ctx.InboundExhaustedAtInfeed)
            {
                _ctx.ScheduleInfeedQueueFill(_ctx.Clock.Now);
            }

            if (!_ctx.TryBeginConveyor(job))
            {
                WarehouseSimLog.Detail(
                    $"出库交互点待发运 job={job.JobId} pickup={job.PickupPointIndex} t={_ctx.Clock.Now:F2}s，" +
                    "暂无可用出库口，在交互点等待。");
            }
        }

        internal void ReleaseStackerAfterPickupDeparture(WarehouseJob job)
        {
            if (job == null || job.Direction != SimFlowDirection.Outbound)
            {
                return;
            }

            if (!_stackerReleasedAfterPickup.Add(job.JobId))
            {
                return;
            }

            _ctx.DecrementStackerActiveJobCount(job.AssignedStackerId);
            ScheduleQueueFill(_ctx.Clock.Now);
        }

        internal void FailNoCargo(WarehouseJob job, SimJobFailureReason reason, bool releaseResources = false)
        {
            if (job.State == WarehouseJobState.FailedNoCargo || job.State == WarehouseJobState.Completed)
            {
                return;
            }

            if (job.Direction == SimFlowDirection.Outbound)
            {
                var port = job.OutfeedPortIndex;
                ReleasePortReservation(job);
                if (port >= 0)
                {
                    SchedulePortDispatch(port, _ctx.Clock.Now);
                }
            }

            if (releaseResources)
            {
                ReleaseSlotReservation(job);
            }

            ReleaseStackerAfterPickupDeparture(job);
            _ctx.ReleaseFailedOutboundPickup(job);
            job.State = WarehouseJobState.FailedNoCargo;
            _ctx.FailedNoCargo++;
            _ctx.RecordFailure(reason);
            WarehouseSimLog.Info(
                $"任务失败（{SimJobFailureReasonLabels.GetLabel(reason)}）job={job.JobId} t={_ctx.Clock.Now:F2}s");
        }

        internal bool TryCommitPath(WarehouseJob job)
        {
            if (job.ConveyorPathZones != null && job.ConveyorPathZones.Count > 0)
            {
                return true;
            }

            var previousOutfeedPort = job.OutfeedPortIndex;
            if (!ConveyorPathPlanner.TrySelectBestOutboundPath(
                    _ctx.Topology,
                    _ctx.Bindings,
                    _ctx.Strategy,
                    _ctx.Reservations,
                    _reservationCounts,
                    job.PickupPointIndex,
                    job.OutfeedPortIndex,
                    _ctx.Clock.Now,
                    out var outfeedPort,
                    out var path,
                    job))
            {
                WarehouseSimLog.Warn(
                    $"出库输送选路失败 job={job.JobId} pickup={job.PickupPointIndex} " +
                    $"preferredOutfeed={job.OutfeedPortIndex} t={_ctx.Clock.Now:F2}s");
                return false;
            }

            if (previousOutfeedPort != outfeedPort)
            {
                if (previousOutfeedPort >= 0
                    && _reservationCounts != null
                    && previousOutfeedPort < _reservationCounts.Length)
                {
                    _reservationCounts[previousOutfeedPort] =
                        Math.Max(0, _reservationCounts[previousOutfeedPort] - 1);
                }

                if (outfeedPort >= 0 && _reservationCounts != null && outfeedPort < _reservationCounts.Length)
                {
                    _reservationCounts[outfeedPort]++;
                }
            }

            job.OutfeedPortIndex = outfeedPort;
            job.ConveyorPathNodeIndices = path;
            job.ConveyorSegmentSchedule ??= new List<ConveyorSegmentScheduleEntry>();
            job.ConveyorSegmentSchedule.Clear();
            job.ConveyorEdgeSubTasksRecorded = new HashSet<int>();
            job.ConveyorPathZones = ConveyorPathZoneChainBuilder.Build(path, _ctx.Topology, _ctx.Bindings.ConveyorMap);
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

        internal void FillDiagnostics(SimEventDiagnostics diag)
        {
            if (_reservationCounts == null)
            {
                return;
            }

            for (var i = 0; i < _reservationCounts.Length; i++)
            {
                diag.OutfeedReservations.Add(new SimNamedCount
                {
                    Name = $"port{i}",
                    Count = _reservationCounts[i],
                });
            }
        }

        private void OnPortDispatch(int portIndex)
        {
            if (portIndex == QueueFillWakePort)
            {
                _queueFillScheduled = false;
                TryFillFromQueue();
                return;
            }

            if (_dispatchScheduled == null || portIndex < 0 || portIndex >= _dispatchScheduled.Length)
            {
                return;
            }

            _dispatchScheduled[portIndex] = false;
            TryFillFromQueue();
        }

        private void TryFillFromQueue()
        {
            if (_ctx.OutboundWaitingCount <= 0
                || _ctx.Topology.OutfeedNodeIndices == null
                || _ctx.Topology.OutfeedNodeIndices.Count == 0)
            {
                return;
            }

            while (_ctx.OutboundWaitingCount > 0)
            {
                if (!TryPlaceOne())
                {
                    return;
                }
            }
        }

        private bool TryPlaceOne()
        {
            var access = ConveyorRouteAccess.FromTags(_ctx.PeekOutboundProcessTags());
            if (!OutfeedPortSelector.TrySelectOutboundPlacement(
                    _ctx.Topology,
                    _ctx.Bindings,
                    _ctx.SlotAllocator,
                    _ctx.PickupReservationCounts,
                    _ctx.StackerActiveJobCounts,
                    _reservedSlots,
                    out var slot,
                    out var stackerId,
                    out var pickupIndex,
                    out _,
                    access))
            {
                if (!_ctx.SlotAllocator.HasRetrievableOccupiedSlot(_ctx.Bindings, _ctx.Topology))
                {
                    FailAllRemaining();
                    return false;
                }

                WarehouseSimLog.Detail(
                    $"出库等候 {_ctx.OutboundWaitingCount} 箱：暂无空闲堆垛机或交互点，" +
                    "待交互点释放后重试。");
                return false;
            }

            _ctx.OutboundWaitingCount--;
            var processTags = _ctx.PeekOutboundProcessTags();
            _ctx.DequeueOutboundFlowEntry();
            var job = new WarehouseJob(_ctx.NextJobId++, _ctx.Clock.Now)
            {
                Direction = SimFlowDirection.Outbound,
                State = WarehouseJobState.WaitingStacker,
                RequiredProcessTags = SimRunContext.CopyProcessTags(processTags),
            };
            _ctx.Jobs[job.JobId] = job;
            Place(job, slot, stackerId, pickupIndex);
            return true;
        }

        private void Place(WarehouseJob job, GridIndex slot, int stackerId, int pickupIndex)
        {
            job.TargetSlot = slot;
            job.HasSlot = true;
            job.AssignedStackerId = stackerId;
            job.PickupPointIndex = pickupIndex;
            job.OutfeedPortIndex = -1;
            _reservedSlots.Add(slot);
            _ctx.IncrementStackerActiveJobCount(stackerId);
            WarehouseSimLog.Info(() =>
                $"出库建单 job={job.JobId} stacker={stackerId} slot={slot} pickup={pickupIndex} " +
                $"outfeed=待选 t={_ctx.Clock.Now:F2}s");
            job.ArrivalTime = _ctx.Clock.Now;
            _ctx.RecordPlayback(job, SimPlaybackPhase.Arrived, stackerId, slot);
            _ctx.TryBeginStackerPhase(job);
        }

        private void FailAllRemaining()
        {
            if (_ctx.OutboundWaitingCount <= 0)
            {
                return;
            }

            var remaining = _ctx.OutboundWaitingCount;
            _ctx.OutboundWaitingCount = 0;
            _ctx.ClearOutboundFlowEntries();
            _ctx.FailedNoCargo += remaining;
            _ctx.RecordFailure(SimJobFailureReason.OutboundQueueNoInventory, remaining);
            WarehouseSimLog.Info($"无可用库存：{remaining} 箱出库请求失败 t={_ctx.Clock.Now:F2}s");
        }

        private void ReleaseSlotReservation(WarehouseJob job)
        {
            if (!job.HasSlot)
            {
                return;
            }

            _reservedSlots.Remove(job.TargetSlot);
            job.HasSlot = false;
        }

        private void ReleasePortReservation(WarehouseJob job)
        {
            var port = job.OutfeedPortIndex;
            if (port < 0 || _reservationCounts == null || port >= _reservationCounts.Length)
            {
                return;
            }

            _reservationCounts[port] = Math.Max(0, _reservationCounts[port] - 1);
        }

        private void OnServiceComplete(WarehouseJob job)
        {
            var port = job.OutfeedPortIndex;
            if (port >= 0 && _reservationCounts != null && port < _reservationCounts.Length)
            {
                _reservationCounts[port] = Math.Max(0, _reservationCounts[port] - 1);
            }

            _ctx.RecordPlayback(job, SimPlaybackPhase.OutfeedDone, job.AssignedStackerId, job.TargetSlot);
            _ctx.ReleasePrimaryOutfeed(job);
            _ctx.RetryOutboundConveyorRouteWaiters();
            _ctx.Queue.Enqueue(new ScheduledSimEvent(_ctx.Clock.Now, SimEventType.JobCompleted, job.JobId));
            SchedulePhysicalRelease(job, _ctx.Clock.Now + 0.5);
        }

        private void OnPhysicalRelease(WarehouseJob job)
        {
            var port = job.OutfeedPortIndex;
            if (port >= 0
                && _cargoOccupied != null
                && port < _cargoOccupied.Length
                && _reservationCounts != null
                && _reservationCounts[port] <= 0)
            {
                _cargoOccupied[port] = false;
            }

            SchedulePortDispatch(port, _ctx.Clock.Now);
        }

        private void SchedulePhysicalRelease(WarehouseJob job, double releaseTime)
        {
            var when = Math.Max(_ctx.Clock.Now, releaseTime);
            _ctx.Queue.Enqueue(new ScheduledSimEvent(when, SimEventType.OutfeedPortPhysicalRelease, job.JobId));
        }

        private void OnJobCompleted(WarehouseJob job)
        {
            _ctx.CompletedCount++;
            if (job.Direction == SimFlowDirection.Inbound)
            {
                _ctx.InboundCompletedCount++;
            }

            _ctx.ReportLiveProgress();
            if (job.Direction != SimFlowDirection.Outbound)
            {
                _ctx.DecrementStackerActiveJobCount(job.AssignedStackerId);
                if (_ctx.OutboundWaitingCount > 0)
                {
                    ScheduleQueueFill(_ctx.Clock.Now);
                }
            }

            job.State = WarehouseJobState.Completed;
            var endTime = _ctx.Clock.Now;
            var duration = endTime - job.ArrivalTime;
            _ctx.Completions.Add(new JobCompletionRecord
            {
                JobId = job.JobId,
                Direction = job.Direction,
                CompletedAt = endTime,
                Duration = duration,
                ServiceTime = job.ServiceTimeAccum,
                WaitTime = job.WaitTimeAccum,
                BottleneckResource = job.LastWaitResource,
                Slot = job.TargetSlot,
                StackerId = job.AssignedStackerId,
                InfeedPortIndex = job.InfeedPortIndex,
                OutfeedPortIndex = job.OutfeedPortIndex,
                PickupPointIndex = job.PickupPointIndex,
            });
            _ctx.RecordPlayback(job, SimPlaybackPhase.Completed, job.AssignedStackerId, job.TargetSlot);
            _ctx.RecordSubTask(
                job,
                SimSubTaskKind.Completed,
                endTime,
                endTime,
                job.AssignedStackerId,
                job.TargetSlot,
                attachPathContext: true);
            WarehouseSimLog.Info(() =>
                $"任务完成 job={job.JobId} 耗时={duration:F1}s 服务={job.ServiceTimeAccum:F1}s " +
                $"等待={job.WaitTimeAccum:F1}s slot={job.TargetSlot}");
        }
    }
}
