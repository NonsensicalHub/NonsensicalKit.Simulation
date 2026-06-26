using System;
using System.Collections.Generic;
using System.Linq;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <content>
    /// 入库阶段：货物批次到达 → 等候队列 → 选入库口/货位/堆垛机 → 入库口服务 → 进入输送。
    /// 修改入库口策略见 <see cref="InfeedPortSelector"/>；无货位失败见 <see cref="FailAllRemainingCargoAtInfeed"/>。
    /// </content>
    public sealed partial class StackerWarehouseSimulator
    {
        #region 入库口放货与调度

        /// <summary>专用 payload：合并取货点释放等路径上的入库等候唤醒，不与真实入库口序号冲突。</summary>
        private const int InfeedQueueFillWakePort = -1;

        private bool _infeedQueueFillScheduled;

        private void ApplyStrategyLabels(SimRunResult result) =>
            SimStrategyLabels.ApplyToResult(result, _strategy);

        /// <summary>入库口放货员：该口有空位时从等候队列取下一箱并建任务。</summary>
        private void OnInfeedPortFeed(int portIndex)
        {
            if (portIndex == InfeedQueueFillWakePort)
            {
                _infeedQueueFillScheduled = false;
                TryFillInfeedFromQueue();
                return;
            }

            if (_infeedPortFeedScheduled == null
                || portIndex < 0
                || portIndex >= _infeedPortFeedScheduled.Length)
            {
                return;
            }

            _infeedPortFeedScheduled[portIndex] = false;
            TryFillInfeedFromQueue();
        }

        /// <summary>取货点预定释放等高频路径上合并入库等候扫描，避免同步重复全库探测。</summary>
        private void ScheduleInfeedQueueFill(double when)
        {
            if (_inboundWaitingCount <= 0
                || _inboundExhaustedAtInfeed
                || _infeedQueueFillScheduled)
            {
                return;
            }

            _infeedQueueFillScheduled = true;
            _queue.Enqueue(new ScheduledSimEvent(when, SimEventType.InfeedPortFeed, 0, InfeedQueueFillWakePort));
        }

        private void ScheduleInfeedPortFeed(int portIndex, double when)
        {
            if (_infeedPortFeedScheduled == null
                || portIndex < 0
                || portIndex >= _infeedPortFeedScheduled.Length)
            {
                return;
            }

            if (_infeedPortFeedScheduled[portIndex])
            {
                return;
            }

            _infeedPortFeedScheduled[portIndex] = true;
            _queue.Enqueue(new ScheduledSimEvent(when, SimEventType.InfeedPortFeed, 0, portIndex));
        }

        /// <summary>
        /// 从等候队列向可用入库口放货：每成功一箱创建 <see cref="WarehouseJob"/> 并进入入库服务。
        /// </summary>
        /// <remarks>
        /// 循环直至无货可放、无可用口、或库满终止。阻塞时由入库口物理释放或取货点预定释放唤醒，不做定时轮询。
        /// </remarks>
        private void TryFillInfeedFromQueue()
        {
            var maxReservations = _bindings.MaxInfeedReservationsPerPort > 0
                ? _bindings.MaxInfeedReservationsPerPort
                : 1;

            while (_inboundWaitingCount > 0 && !_inboundExhaustedAtInfeed)
            {
                if (!TryPlaceOneInboundFromQueue(maxReservations))
                {
                    return;
                }
            }
        }

        /// <summary>单次唤醒内按策略尝试所有可用入库口；阻塞时仅依赖物理释放/取货点释放/出库腾位唤醒。</summary>
        private bool TryPlaceOneInboundFromQueue(int maxReservations)
        {
            var portCount = _conveyorTopology.InfeedNodeIndices.Count;
            var candidateCount = InfeedPortSelector.FillCandidateInfeedPorts(
                _conveyorTopology,
                _strategy,
                _infeedReservationCounts,
                _infeedCargoOccupied,
                maxReservations,
                _infeedRoundRobinCursor,
                _infeedPortOrderScratch);

            for (var i = 0; i < candidateCount; i++)
            {
                var portIndex = _infeedPortOrderScratch[i];
                if (_infeedReservationCounts[portIndex] >= maxReservations)
                {
                    continue;
                }

                if (!ConveyorPathPlanner.HasOpenPickupRouteFromInfeed(
                        _conveyorTopology,
                        _bindings,
                        portIndex,
                        _pickupReservationCounts))
                {
                    continue;
                }

                if (!InfeedPortSelector.TrySelectSlotForInfeedPort(
                        _conveyorTopology,
                        _bindings,
                        _slotAllocator,
                        _pickupReservationCounts,
                        _stackerActiveJobCounts,
                        portIndex,
                        out var slot,
                        out var stackerId,
                        skipPickupCapacityPrecheck: true))
                {
                    continue;
                }

                _inboundWaitingCount--;
                var flowEntryIndex = _inboundWaitingFlowEntryIndices.Count > 0
                    ? _inboundWaitingFlowEntryIndices.Dequeue()
                    : -1;
                var job = new WarehouseJob(_nextJobId++, _clock.Now)
                {
                    Direction = SimFlowDirection.Inbound,
                    RequiredProcessTags = CopyProcessTags(_flowPlanScheduler?.GetRequiredProcessTags(flowEntryIndex)),
                };
                job.State = WarehouseJobState.PendingArrival;
                _jobs[job.JobId] = job;
                PlaceJobOnInfeedPort(job, portIndex, slot, stackerId);
                _infeedRoundRobinCursor = (portIndex + 1) % portCount;
                return true;
            }

            if (!ConveyorPathPlanner.HasOpenPickupRouteFromAnyInfeed(
                    _conveyorTopology,
                    _bindings,
                    _pickupReservationCounts))
            {
                WarehouseSimLog.Detail(
                    $"入库等候 {_inboundWaitingCount} 箱：取货点预定已满（库内仍有空货位），" +
                    "待取货点释放后重试。");
                return false;
            }

            if (_slotAllocator.TotalFreeCount <= 0)
            {
                var failPort = InfeedPortSelector.SelectInfeedPort(
                    _conveyorTopology,
                    _strategy,
                    _infeedReservationCounts,
                    _infeedCargoOccupied,
                    maxReservations,
                    ref _infeedRoundRobinCursor);
                FailAllRemainingCargoAtInfeed(
                    failPort,
                    SimJobFailureReason.InboundQueueNoAllocatableSlot);
                return false;
            }

            if (!_slotAllocator.HasAllocatableFreeSlot(_bindings, _conveyorTopology))
            {
                WarehouseSimLog.Detail(
                    $"入库等候 {_inboundWaitingCount} 箱：空闲货位暂不在堆垛机可达列，" +
                    "待出库腾位后重试。");
                return false;
            }

            WarehouseSimLog.Detail(
                $"入库等候 {_inboundWaitingCount} 箱：各入库口暂无可路由空货位，" +
                "待入库口物理释放或取货点释放后重试。");
            return false;
        }

        /// <summary>无空货位：当前箱停在入库口，其余等候箱全部记为失败且不建任务。</summary>
        private void FailAllRemainingCargoAtInfeed(int portIndex, SimJobFailureReason reason)
        {
            if (_inboundWaitingCount <= 0 || _inboundExhaustedAtInfeed)
            {
                return;
            }

            var remaining = _inboundWaitingCount;
            _inboundWaitingCount = 0;
            _inboundWaitingFlowEntryIndices.Clear();
            _inboundExhaustedAtInfeed = true;
            _failedNoSlot += remaining;
            RecordFailure(reason, remaining);

            if (_infeedCargoOccupied != null
                && portIndex >= 0
                && portIndex < _infeedCargoOccupied.Length)
            {
                _infeedCargoOccupied[portIndex] = true;
            }

            WarehouseSimLog.Info(
                $"入库口 {portIndex} {SimJobFailureReasonLabels.GetLabel(reason)}：" +
                $"1 箱停在入库口，其余 {remaining - 1} 箱未建任务即失败；" +
                $"共 {remaining} 箱入库失败 t={_clock.Now:F2}s");
        }

        private void PlaceJobOnInfeedPort(WarehouseJob job, int portIndex, GridIndex slot, int stackerId)
        {
            job.InfeedPortIndex = portIndex;
            job.TargetSlot = slot;
            job.HasSlot = true;
            job.AssignedStackerId = stackerId;
            _slotAllocator.Occupy(slot);
            _infeedCargoOccupied[portIndex] = true;
            _infeedReservationCounts[portIndex]++;
            IncrementStackerActiveJobCount(stackerId);

            ref var infeedNode = ref _conveyorTopology.GetNode(_conveyorTopology.InfeedNodeIndices[portIndex]);

            var portLabel = SimEntityNaming.FormatLogicalId(
                in infeedNode,
                _conveyorTopology.InfeedNodeIndices[portIndex]);
            WarehouseSimLog.Info(() =>
                $"入库口放货 job={job.JobId} port={portLabel} stacker={job.AssignedStackerId} " +
                $"slot={slot} t={_clock.Now:F2}s");

            job.ArrivalTime = _clock.Now;
            RecordPlayback(job, SimPlaybackPhase.Arrived, job.AssignedStackerId, job.TargetSlot);
            BeginInfeed(job);
        }

        /// <summary>标记任务失败；<paramref name="releaseResources"/> 时释放已占货位与入库口。</summary>
        private void FailJobNoSlot(
            WarehouseJob job,
            SimJobFailureReason reason,
            bool releaseResources = false)
        {
            if (job.State == WarehouseJobState.FailedNoSlot || job.State == WarehouseJobState.Completed)
            {
                return;
            }

            if (releaseResources)
            {
                if (job.HasSlot)
                {
                    ReleaseAllocatedSlot(job);
                }

                var port = job.InfeedPortIndex;
                if (port >= 0)
                {
                    ReleaseInfeedPort(job);
                    ScheduleInfeedPortFeed(port, _clock.Now);
                }
            }

            job.State = WarehouseJobState.FailedNoSlot;
            _failedNoSlot++;
            RecordFailure(reason);
            WarehouseSimLog.Info(
                $"任务失败（{SimJobFailureReasonLabels.GetLabel(reason)}）job={job.JobId} t={_clock.Now:F2}s");
        }

        private void IncrementStackerActiveJobCount(int stackerId)
        {
            if (_stackerActiveJobCounts == null
                || stackerId < 0
                || stackerId >= _stackerActiveJobCounts.Length)
            {
                return;
            }

            _stackerActiveJobCounts[stackerId]++;
        }

        private void DecrementStackerActiveJobCount(int stackerId)
        {
            if (_stackerActiveJobCounts == null
                || stackerId < 0
                || stackerId >= _stackerActiveJobCounts.Length)
            {
                return;
            }

            _stackerActiveJobCounts[stackerId] = Math.Max(0, _stackerActiveJobCounts[stackerId] - 1);
        }

        #endregion

        #region 入库服务与进入输送

        /// <summary>入库口放货并完成服务（扫描建单）；口忙时含排队等待。</summary>
        private void BeginInfeed(WarehouseJob job)
        {
            var desired = _clock.Now;
            var port = job.InfeedPortIndex;
            var node = _conveyorTopology.GetNode(_conveyorTopology.InfeedNodeIndices[port]);
            var portLabel = SimEntityNaming.FormatLogicalId(in node, _conveyorTopology.InfeedNodeIndices[port]);
            var duration = node.InfeedServiceSeconds > 0f
                ? node.InfeedServiceSeconds
                : _bindings.InfeedServiceSeconds;
            var resourceId = $"{portLabel}-infeed";

            var start = Math.Max(desired, _infeedServiceFreeByPort[port]);
            _infeedServiceFreeByPort[port] = start + duration;

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

            RecordSubTask(
                job,
                SimSubTaskKind.InfeedPlace,
                desired,
                end,
                job.AssignedStackerId,
                job.TargetSlot);

            _queue.Enqueue(new ScheduledSimEvent(end, SimEventType.InfeedServiceComplete, job.JobId));
        }

        /// <summary>入库完成：规划输送路径；入库口物理占用待货箱尾端离开碰撞区后再释放。</summary>
        private void OnInfeedComplete(WarehouseJob job)
        {
            job.InfeedCompleteSimTime = job.ScheduledCompleteTime;
            RecordPlayback(job, SimPlaybackPhase.InfeedDone, job.AssignedStackerId, job.TargetSlot);
            TryBeginConveyor(job);
        }

        /// <summary>货箱尾端已离开入库口碰撞区，释放入库口并尝试放入下一箱。</summary>
        /// <remarks>
        /// 仅释放入库口预定与物理占用，并由 <see cref="ScheduleInfeedPortFeed"/> 唤醒放货。
        /// 不调用 <see cref="NotifyConveyorRouteWaiters"/>：物理释放不改变取货点预定或输送占用，
        /// 全量任务扫描在 1000 箱场景下约 100 万次无效 <c>TryBeginConveyor</c> 调用。
        /// </remarks>
        private void OnInfeedPortPhysicalRelease(WarehouseJob job)
        {
            var port = job.InfeedPortIndex;
            ReleaseInfeedPort(job);
            if (port >= 0)
            {
                WarehouseSimLog.Detail(
                    $"入库口碰撞区释放 job={job.JobId} port={port} t={_clock.Now:F2}s");
                ScheduleInfeedPortFeed(port, _clock.Now);
            }
        }

        private void ScheduleInfeedPortPhysicalRelease(WarehouseJob job, double releaseTime)
        {
            var when = Math.Max(_clock.Now, releaseTime);
            _queue.Enqueue(new ScheduledSimEvent(when, SimEventType.InfeedPortPhysicalRelease, job.JobId));
        }

        private void ReleaseInfeedPort(WarehouseJob job)
        {
            var port = job.InfeedPortIndex;
            if (port < 0 || _infeedReservationCounts == null || port >= _infeedReservationCounts.Length)
            {
                return;
            }

            _infeedReservationCounts[port] = Math.Max(0, _infeedReservationCounts[port] - 1);
            if (_infeedReservationCounts[port] == 0
                && _infeedCargoOccupied != null
                && port < _infeedCargoOccupied.Length)
            {
                _infeedCargoOccupied[port] = false;
            }
        }

        /// <summary>查找可用取货点、加权寻路，并按路段逐步预约输送。</summary>
        /// <returns>是否已成功预约下一段输送（含已提交路径的续订）。</returns>
        private bool TryBeginConveyor(WarehouseJob job)
        {
            if (job.State == WarehouseJobState.FailedNoSlot
                || job.State == WarehouseJobState.FailedNoCargo
                || job.State == WarehouseJobState.Completed)
            {
                return false;
            }

            if (IsConveyorTransitCommitted(job))
            {
                return TryResumeConveyorSegments(job);
            }

            if (TryScheduleConveyorOnMap(job, out _, out _))
            {
                ClearConveyorRoutePending(job);
                return true;
            }

            if (job.Direction == SimFlowDirection.Outbound)
            {
                if (job.PickupPointIndex < 0
                    || _conveyorTopology?.OutfeedNodeIndices == null
                    || _conveyorTopology.OutfeedNodeIndices.Count == 0)
                {
                    FailJobNoCargo(
                        job,
                        SimJobFailureReason.OutboundJobNoInventory,
                        releaseResources: false);
                    return false;
                }

                MarkConveyorRoutePending(job);
                return false;
            }

            if (!ConveyorPathPlanner.CanRouteFromInfeedIgnoringPickupCapacity(
                    _conveyorTopology,
                    _bindings,
                    job.InfeedPortIndex,
                    job.TargetSlot))
            {
                FailJobNoSlot(job, SimJobFailureReason.InboundConveyorRouteUnreachable, releaseResources: true);
                return false;
            }

            MarkConveyorRoutePending(job);
            return false;
        }

        private bool TryResumeConveyorSegments(WarehouseJob job)
        {
            var next = job.NextConveyorZoneIndex;
            var total = job.ConveyorPathZones?.Count ?? 0;
            if (next >= total)
            {
                return false;
            }

            if (TryScheduleConveyorZone(job, _clock.Now, out _, out _))
            {
                ClearConveyorRoutePending(job);
                return true;
            }

            var chain = job.ConveyorPathZones;
            var blockingResource = chain != null && next >= 0 && next < chain.Count
                ? chain[next].ResourceId
                : null;
            MarkConveyorRoutePending(job, blockingResource);
            return false;
        }

        /// <summary>已有 zone 链时不重复选路；仅状态为 OnConveyor 但尚无路径时仍视为未提交（出库放货后常见）。</summary>
        private static bool IsConveyorTransitCommitted(WarehouseJob job) =>
            job.ConveyorPathZones != null && job.ConveyorPathZones.Count > 0;

        #endregion

        private static string[] CopyProcessTags(string[] source)
        {
            if (source == null || source.Length == 0)
            {
                return null;
            }

            var copy = new string[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                copy[i] = source[i];
            }

            return copy;
        }
    }
}
