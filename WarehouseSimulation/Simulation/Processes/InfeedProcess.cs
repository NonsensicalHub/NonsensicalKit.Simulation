using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>入库口放货、扫描服务和物理释放。货位与输送由上下文交给对应阶段。</summary>
    internal sealed class InfeedProcess : ISimProcess
    {
        private const int QueueFillWakePort = -1;

        private readonly SimRunContext _ctx;
        private bool[] _cargoOccupied;
        private int[] _reservationCounts;
        private double[] _serviceFreeByPort;
        private bool[] _portFeedScheduled;
        private int[] _portOrderScratch;
        private readonly HashSet<int> _allowedPorts = new();
        private int _roundRobinCursor;
        private bool _queueFillScheduled;

        public InfeedProcess(SimRunContext ctx) => _ctx = ctx;

        public bool Handles(SimEventType type) =>
            type is SimEventType.InfeedPortFeed
                or SimEventType.InfeedServiceComplete
                or SimEventType.InfeedPortPhysicalRelease;

        public void OnEvent(ScheduledSimEvent evt)
        {
            switch (evt.Type)
            {
                case SimEventType.InfeedPortFeed:
                    OnPortFeed(evt.Payload);
                    break;
                case SimEventType.InfeedServiceComplete:
                    if (_ctx.TryGetJob(evt.JobId, evt.Type, out var completeJob))
                    {
                        completeJob.InfeedCompleteSimTime = completeJob.ScheduledCompleteTime;
                        _ctx.RecordPlayback(
                            completeJob,
                            SimPlaybackPhase.InfeedDone,
                            completeJob.AssignedStackerId,
                            completeJob.TargetSlot);
                        _ctx.TryBeginConveyor(completeJob);
                    }

                    break;
                case SimEventType.InfeedPortPhysicalRelease:
                    if (_ctx.TryGetJob(evt.JobId, evt.Type, out var releaseJob))
                    {
                        OnPhysicalRelease(releaseJob);
                    }

                    break;
            }
        }

        internal void InitPorts()
        {
            var infeedCount = _ctx.Topology.InfeedNodeIndices.Count;
            _cargoOccupied = new bool[infeedCount];
            _reservationCounts = new int[infeedCount];
            _serviceFreeByPort = new double[infeedCount];
            _portFeedScheduled = new bool[infeedCount];
            _portOrderScratch = new int[Math.Max(1, infeedCount)];
            _roundRobinCursor = 0;
            _queueFillScheduled = false;
            _allowedPorts.Clear();
        }

        internal void ScheduleInitialFeeds()
        {
            var infeedCount = _ctx.Topology.InfeedNodeIndices.Count;
            for (var port = 0; port < infeedCount; port++)
            {
                SchedulePortFeed(port, 0);
            }
        }

        internal void ScheduleQueueFill(double when)
        {
            if (_ctx.InboundWaitingCount <= 0 || _ctx.InboundExhaustedAtInfeed || _queueFillScheduled)
            {
                return;
            }

            _queueFillScheduled = true;
            _ctx.Queue.Enqueue(new ScheduledSimEvent(when, SimEventType.InfeedPortFeed, 0, QueueFillWakePort));
        }

        internal void SchedulePortFeed(int portIndex, double when)
        {
            if (_portFeedScheduled == null || portIndex < 0 || portIndex >= _portFeedScheduled.Length)
            {
                return;
            }

            if (_portFeedScheduled[portIndex])
            {
                return;
            }

            _portFeedScheduled[portIndex] = true;
            _ctx.Queue.Enqueue(new ScheduledSimEvent(when, SimEventType.InfeedPortFeed, 0, portIndex));
        }

        internal void SchedulePhysicalRelease(WarehouseJob job, double releaseTime)
        {
            var when = Math.Max(_ctx.Clock.Now, releaseTime);
            _ctx.Queue.Enqueue(new ScheduledSimEvent(when, SimEventType.InfeedPortPhysicalRelease, job.JobId));
        }

        internal void FailNoSlot(WarehouseJob job, SimJobFailureReason reason, bool releaseResources = false)
        {
            if (job.State == WarehouseJobState.FailedNoSlot || job.State == WarehouseJobState.Completed)
            {
                return;
            }

            _ctx.ReleaseInfeedOriginClaim(job);
            if (releaseResources)
            {
                if (job.HasSlot)
                {
                    _ctx.ReleaseAllocatedSlot(job);
                }

                var port = job.InfeedPortIndex;
                if (port >= 0)
                {
                    ReleasePort(job);
                    SchedulePortFeed(port, _ctx.Clock.Now);
                }
            }

            job.State = WarehouseJobState.FailedNoSlot;
            _ctx.FailedNoSlot++;
            _ctx.RecordFailure(reason);
            WarehouseSimLog.Info(
                $"任务失败（{SimJobFailureReasonLabels.GetLabel(reason)}）job={job.JobId} t={_ctx.Clock.Now:F2}s");
        }

        internal void FillDiagnostics(SimEventDiagnostics diag)
        {
            if (_reservationCounts == null)
            {
                return;
            }

            for (var i = 0; i < _reservationCounts.Length; i++)
            {
                diag.InfeedReservations.Add(new SimNamedCount
                {
                    Name = $"port{i}",
                    Count = _reservationCounts[i],
                });
            }
        }

        private void OnPortFeed(int portIndex)
        {
            if (portIndex == QueueFillWakePort)
            {
                _queueFillScheduled = false;
                TryFillFromQueue();
                return;
            }

            if (_portFeedScheduled == null || portIndex < 0 || portIndex >= _portFeedScheduled.Length)
            {
                return;
            }

            _portFeedScheduled[portIndex] = false;
            TryFillFromQueue();
        }

        private void TryFillFromQueue()
        {
            var maxReservations = _ctx.Bindings.MaxInfeedReservationsPerPort > 0
                ? _ctx.Bindings.MaxInfeedReservationsPerPort
                : 1;
            while (_ctx.InboundWaitingCount > 0 && !_ctx.InboundExhaustedAtInfeed)
            {
                if (!TryPlaceOne(maxReservations))
                {
                    return;
                }
            }
        }

        private bool TryPlaceOne(int maxReservations)
        {
            var portCount = _ctx.Topology.InfeedNodeIndices.Count;
            var processTags = _ctx.PeekInboundProcessTags();
            var access = ConveyorRouteAccess.FromTags(processTags);
            ConveyorProcessStationUtility.TryResolveSupportedInfeedPortUnion(
                _ctx.Topology,
                processTags,
                _allowedPorts,
                out var restrictInfeedByProcessStation);
            var candidateCount = InfeedPortSelector.FillCandidateInfeedPorts(
                _ctx.Topology,
                _ctx.Strategy,
                _reservationCounts,
                _cargoOccupied,
                maxReservations,
                _roundRobinCursor,
                _portOrderScratch);

            for (var i = 0; i < candidateCount; i++)
            {
                var portIndex = _portOrderScratch[i];
                if (restrictInfeedByProcessStation && !_allowedPorts.Contains(portIndex))
                {
                    continue;
                }

                if (_reservationCounts[portIndex] >= maxReservations)
                {
                    continue;
                }

                if (!ConveyorPathPlanner.HasOpenPickupRouteFromInfeed(
                        _ctx.Topology,
                        _ctx.Bindings,
                        portIndex,
                        _ctx.PickupReservationCounts,
                        access))
                {
                    continue;
                }

                if (!InfeedPortSelector.TrySelectSlotForInfeedPort(
                        _ctx.Topology,
                        _ctx.Bindings,
                        _ctx.SlotAllocator,
                        _ctx.PickupReservationCounts,
                        _ctx.StackerActiveJobCounts,
                        portIndex,
                        out var slot,
                        out var stackerId,
                        skipPickupCapacityPrecheck: true,
                        access: access))
                {
                    continue;
                }

                if (!_ctx.TryClaimInfeedOrigin(portIndex, _ctx.NextJobId))
                {
                    continue;
                }

                var job = new WarehouseJob(_ctx.NextJobId++, _ctx.Clock.Now)
                {
                    Direction = SimFlowDirection.Inbound,
                    RequiredProcessTags = SimRunContext.CopyProcessTags(processTags),
                    InfeedPortIndex = portIndex,
                };
                job.State = WarehouseJobState.PendingArrival;
                _ctx.Jobs[job.JobId] = job;
                if (!PlaceOnPort(job, portIndex, slot, stackerId))
                {
                    _ctx.Jobs.Remove(job.JobId);
                    _ctx.ReleaseInfeedOriginClaim(job);
                    continue;
                }

                _ctx.InboundWaitingCount--;
                _ctx.DequeueInboundFlowEntry();
                _roundRobinCursor = (portIndex + 1) % portCount;
                return true;
            }

            if (restrictInfeedByProcessStation && _allowedPorts.Count == 0)
            {
                WarehouseSimLog.Detail(
                    $"入库等候 {_ctx.InboundWaitingCount} 箱：加工站「支持的入库口」并集为空或无法解析，" +
                    "无法放货。请检查加工站配置的入库口逻辑 ID。");
                return false;
            }

            if (!HasOpenPickupRoute(access, restrictInfeedByProcessStation))
            {
                WarehouseSimLog.Detail(
                    $"入库等候 {_ctx.InboundWaitingCount} 箱：取货点预定已满（库内仍有空货位），" +
                    "待取货点释放后重试。");
                return false;
            }

            if (_ctx.SlotAllocator.TotalFreeCount <= 0)
            {
                var failPort = InfeedPortSelector.SelectInfeedPort(
                    _ctx.Topology,
                    _ctx.Strategy,
                    _reservationCounts,
                    _cargoOccupied,
                    maxReservations,
                    ref _roundRobinCursor);
                FailAllRemaining(failPort, SimJobFailureReason.InboundQueueNoAllocatableSlot);
                return false;
            }

            if (!_ctx.SlotAllocator.HasAllocatableFreeSlot(_ctx.Bindings, _ctx.Topology))
            {
                WarehouseSimLog.Detail(
                    $"入库等候 {_ctx.InboundWaitingCount} 箱：空闲货位暂不在堆垛机可达列，" +
                    "待出库腾位后重试。");
                return false;
            }

            WarehouseSimLog.Detail(
                $"入库等候 {_ctx.InboundWaitingCount} 箱：各入库口暂无可路由空货位，" +
                "待入库口物理释放或取货点释放后重试。");
            return false;
        }

        private void FailAllRemaining(int portIndex, SimJobFailureReason reason)
        {
            if (_ctx.InboundWaitingCount <= 0 || _ctx.InboundExhaustedAtInfeed)
            {
                return;
            }

            var remaining = _ctx.InboundWaitingCount;
            _ctx.InboundWaitingCount = 0;
            _ctx.ClearInboundFlowEntries();
            _ctx.InboundExhaustedAtInfeed = true;
            _ctx.FailedNoSlot += remaining;
            _ctx.RecordFailure(reason, remaining);

            if (_cargoOccupied != null && portIndex >= 0 && portIndex < _cargoOccupied.Length)
            {
                _cargoOccupied[portIndex] = true;
            }

            WarehouseSimLog.Info(
                $"入库口 {portIndex} {SimJobFailureReasonLabels.GetLabel(reason)}：" +
                $"1 箱停在入库口，其余 {remaining - 1} 箱未建任务即失败；" +
                $"共 {remaining} 箱入库失败 t={_ctx.Clock.Now:F2}s");
        }

        private bool PlaceOnPort(WarehouseJob job, int portIndex, GridIndex slot, int stackerId)
        {
            if (!_ctx.SlotAllocator.TryOccupy(slot))
            {
                WarehouseSimLog.Info(
                    $"入库口放货拒绝：货位已不可用或已被占用 job={job.JobId} slot={slot} t={_ctx.Clock.Now:F2}s");
                return false;
            }

            job.InfeedPortIndex = portIndex;
            job.TargetSlot = slot;
            job.HasSlot = true;
            job.AssignedStackerId = stackerId;
            _cargoOccupied[portIndex] = true;
            _reservationCounts[portIndex]++;
            _ctx.IncrementStackerActiveJobCount(stackerId);

            ref var infeedNode = ref _ctx.Topology.GetNode(_ctx.Topology.InfeedNodeIndices[portIndex]);
            var portLabel = SimEntityNaming.FormatLogicalId(in infeedNode, _ctx.Topology.InfeedNodeIndices[portIndex]);
            WarehouseSimLog.Info(() =>
                $"入库口放货 job={job.JobId} port={portLabel} stacker={job.AssignedStackerId} " +
                $"slot={slot} t={_ctx.Clock.Now:F2}s");

            job.ArrivalTime = _ctx.Clock.Now;
            _ctx.RecordPlayback(job, SimPlaybackPhase.Arrived, job.AssignedStackerId, job.TargetSlot);
            BeginService(job);
            return true;
        }

        private void BeginService(WarehouseJob job)
        {
            var desired = _ctx.Clock.Now;
            var port = job.InfeedPortIndex;
            var node = _ctx.Topology.GetNode(_ctx.Topology.InfeedNodeIndices[port]);
            var portLabel = SimEntityNaming.FormatLogicalId(in node, _ctx.Topology.InfeedNodeIndices[port]);
            var duration = node.InfeedServiceSeconds > 0f
                ? node.InfeedServiceSeconds
                : _ctx.Bindings.InfeedServiceSeconds;
            var resourceId = $"{portLabel}-infeed";
            var start = Math.Max(desired, _serviceFreeByPort[port]);
            _serviceFreeByPort[port] = start + duration;

            var wait = start - desired;
            if (wait > 1e-9)
            {
                job.WaitTimeAccum += wait;
                job.LastWaitResource = resourceId;
            }

            var end = start + duration;
            job.ServiceTimeAccum += duration;
            job.State = WarehouseJobState.WaitingInfeed;
            job.PhaseStartTime = start;
            job.ScheduledCompleteTime = end;
            _ctx.RecordSubTask(
                job,
                SimSubTaskKind.InfeedPlace,
                desired,
                end,
                job.AssignedStackerId,
                job.TargetSlot);
            _ctx.Queue.Enqueue(new ScheduledSimEvent(end, SimEventType.InfeedServiceComplete, job.JobId));
        }

        private void OnPhysicalRelease(WarehouseJob job)
        {
            var port = job.InfeedPortIndex;
            ReleasePort(job);
            if (port >= 0)
            {
                WarehouseSimLog.Detail($"入库口碰撞区释放 job={job.JobId} port={port} t={_ctx.Clock.Now:F2}s");
                SchedulePortFeed(port, _ctx.Clock.Now);
            }
        }

        private void ReleasePort(WarehouseJob job)
        {
            var port = job.InfeedPortIndex;
            if (port < 0 || _reservationCounts == null || port >= _reservationCounts.Length)
            {
                return;
            }

            _reservationCounts[port] = Math.Max(0, _reservationCounts[port] - 1);
            if (_reservationCounts[port] == 0 && _cargoOccupied != null && port < _cargoOccupied.Length)
            {
                _cargoOccupied[port] = false;
            }
        }

        private bool HasOpenPickupRoute(ConveyorRouteAccess access, bool restrictInfeedByProcessStation)
        {
            if (!restrictInfeedByProcessStation)
            {
                return ConveyorPathPlanner.HasOpenPickupRouteFromAnyInfeed(
                    _ctx.Topology,
                    _ctx.Bindings,
                    _ctx.PickupReservationCounts,
                    access);
            }

            foreach (var portIndex in _allowedPorts)
            {
                if (ConveyorPathPlanner.HasOpenPickupRouteFromInfeed(
                        _ctx.Topology,
                        _ctx.Bindings,
                        portIndex,
                        _ctx.PickupReservationCounts,
                        access))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
