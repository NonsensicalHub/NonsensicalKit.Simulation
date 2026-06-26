using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <content>
    /// 输送阶段：路径择优（<see cref="ConveyorPathPlanner"/>）→ zone 链逐步预约（<see cref="ConveyorTransitScheduler"/>）→ 取货点。
    /// 预约失败时任务进入等待队列，资源释放后由 <c>OccupancyReleased</c> 触发 <c>ConveyorRouteRetry</c>（见 OccupancyNotify partial）。
    /// </content>
    public sealed partial class StackerWarehouseSimulator
    {
        #region 路径选定

        /// <summary>
        /// 为任务选定输送路径并预约第一个 zone；后续 zone 由 <see cref="SimEventType.ConveyorZoneComplete"/> 链式触发。
        /// </summary>
        /// <remarks>
        /// 路径仅在首次成功时由 <see cref="ConveyorPathPlanner"/> 写入 job；已提交路径则只续订下一 zone。
        /// 预约失败时任务进入资源等待队列，待 <see cref="SimEventType.ConveyorRouteRetry"/> 唤醒。
        /// </remarks>
        private bool TryScheduleConveyorOnMap(WarehouseJob job, out double endTime, out double infeedPhysicalReleaseTime)
        {
            endTime = _clock.Now;
            infeedPhysicalReleaseTime = _clock.Now;

            if (!TryCommitConveyorPath(job))
            {
                return false;
            }

            var desiredStart = job.Direction == SimFlowDirection.Outbound && job.PickupCompleteSimTime > 0
                ? job.PickupCompleteSimTime
                : _clock.Now;
            return TryScheduleConveyorZone(job, desiredStart, out endTime, out infeedPhysicalReleaseTime);
        }

        private bool TryCommitConveyorPath(WarehouseJob job)
        {
            if (job.Direction == SimFlowDirection.Outbound)
            {
                return TryCommitOutboundConveyorPath(job);
            }

            if (job.ConveyorPathZones != null && job.ConveyorPathZones.Count > 0)
            {
                return true;
            }

            bool pathSelected;
            using (_wallClockProfiler.Begin(SimWallClockProfilePhases.ConveyorPathPlan))
            {
                pathSelected = ConveyorPathPlanner.TrySelectBestPath(
                    _conveyorTopology,
                    _bindings,
                    _strategy,
                    _reservations,
                    _pickupReservationCounts,
                    job.InfeedPortIndex,
                    job.TargetSlot,
                    job.AssignedStackerId,
                    _clock.Now,
                    out var pickup,
                    out var stackerId,
                    out var path,
                    job);
                if (!pathSelected)
                {
                    return false;
                }

                job.PickupPointIndex = pickup;
                job.AssignedStackerId = stackerId;
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
                    WarehouseSimLog.Warn($"输送 zone 链为空 job={job.JobId}，请检查 ConveyorMap。");
                    return false;
                }

                job.NextConveyorZoneIndex = 0;
                IncrementPickupReservation(pickup);
                WarehouseSimLog.Info(() =>
                    $"输送路径选定 job={job.JobId} pickup={pickup} stacker={stackerId} " +
                    $"路径节点={path.Count} zone数={job.ConveyorPathZones.Count}");
            }

            return true;
        }

        #endregion

        #region Zone 逐步预约

        private bool TryScheduleConveyorZone(
            WarehouseJob job,
            double desiredStart,
            out double stepEndTime,
            out double infeedPhysicalReleaseTime)
        {
            stepEndTime = desiredStart;
            infeedPhysicalReleaseTime = desiredStart;
            var chain = job.ConveyorPathZones;
            var zoneIndex = job.NextConveyorZoneIndex;
            if (chain == null || zoneIndex < 0 || zoneIndex >= chain.Count)
            {
                return false;
            }

            ConveyorPathZoneReservation reservation;
            using (_wallClockProfiler.Begin(SimWallClockProfilePhases.ConveyorZoneReserve))
            {
                reservation = _conveyorScheduler.TryReservePathZone(
                    job,
                    chain,
                    zoneIndex,
                    desiredStart);
            }
            
            if (!reservation.Success)
            {
                WarehouseSimLog.Warn(
                    $"输送 zone 预约失败 job={job.JobId} zone={zoneIndex} resource={chain[zoneIndex].ResourceId}，将重试。");
                return false;
            }

            chain[zoneIndex] = reservation.Zone;

            // 路口 zone 在其来向边路段时刻表 flush 之后才预约，且可能被 FIFO 顺延。
            // 此处用路口的实际预约窗回填来向边的退出/占用结束时刻，使路口子任务记录与占用自检
            // 都基于真实预约窗（否则自检重建的路口占用窗起点偏早，误报跨任务路口冲突）。
            if (reservation.Zone.Kind == ConveyorPathZoneKind.Junction)
            {
                UpdateApproachSegmentScheduleForJunction(job, reservation.Zone);
                SyncApproachS0ZoneLeave(
                    job,
                    zoneIndex,
                    reservation.Zone.ArriveSimTime - reservation.Zone.HopSeconds);

                if (_recordPlayback)
                {
                    _conveyorSubTaskRecorder.RecordEdgeSlotSubTasksFromChain(job, reservation.Zone.PathEdgeIndex);
                }
            }
            else if (reservation.Zone.Kind == ConveyorPathZoneKind.Pickup)
            {
                UpdateApproachSegmentScheduleForPickup(job, reservation.Zone);
                SyncApproachS0ZoneLeave(
                    job,
                    zoneIndex,
                    reservation.Zone.ArriveSimTime - reservation.Zone.HopSeconds);

                if (_recordPlayback)
                {
                    _conveyorSubTaskRecorder.RecordEdgeSlotSubTasksFromChain(
                        job,
                        reservation.Zone.PathEdgeIndex,
                        attachPathContext: true);
                }
            }
            else if (reservation.Zone.Kind == ConveyorPathZoneKind.Outfeed)
            {
                UpdateApproachSegmentScheduleForPickup(job, reservation.Zone);
                SyncApproachS0ZoneLeave(
                    job,
                    zoneIndex,
                    reservation.Zone.ArriveSimTime - reservation.Zone.HopSeconds);

                if (_recordPlayback)
                {
                    _conveyorSubTaskRecorder.RecordEdgeSlotSubTasksFromChain(
                        job,
                        reservation.Zone.PathEdgeIndex,
                        attachPathContext: true);
                }
            }
            else if (reservation.Zone.Kind == ConveyorPathZoneKind.ProcessStation)
            {
                UpdateApproachSegmentScheduleForPickup(job, reservation.Zone);
                SyncApproachS0ZoneLeave(
                    job,
                    zoneIndex,
                    reservation.Zone.ArriveSimTime - reservation.Zone.HopSeconds);

                if (_recordPlayback)
                {
                    _conveyorSubTaskRecorder.RecordEdgeSlotSubTasksFromChain(
                        job,
                        reservation.Zone.PathEdgeIndex,
                        attachPathContext: true);
                    _conveyorSubTaskRecorder.RecordProcessStationService(job, reservation.Zone);
                }
            }
            else if (reservation.Zone.Kind == ConveyorPathZoneKind.VerticalTransfer)
            {
                UpdateApproachSegmentScheduleForPickup(job, reservation.Zone);
                SyncApproachS0ZoneLeave(
                    job,
                    zoneIndex,
                    reservation.Zone.ArriveSimTime - reservation.Zone.HopSeconds);

                if (_recordPlayback)
                {
                    _conveyorSubTaskRecorder.RecordEdgeSlotSubTasksFromChain(
                        job,
                        reservation.Zone.PathEdgeIndex,
                        attachPathContext: true);
                    _conveyorSubTaskRecorder.RecordVerticalTransferMove(job, reservation.Zone);
                }
            }

            if (zoneIndex > 0 && chain[zoneIndex - 1].Kind == ConveyorPathZoneKind.Junction)
            {
                ReconcileJunctionZoneAfterNextZoneReserved(job, zoneIndex - 1, reservation.Zone);
                if (_recordPlayback)
                {
                    _conveyorSubTaskRecorder.RecordJunctionAfterNextZone(job, zoneIndex - 1, reservation.Zone);
                }
            }

            if (zoneIndex == 0 && job.Direction == SimFlowDirection.Inbound)
            {
                job.State = WarehouseJobState.OnConveyor;
                if (reservation.InfeedPhysicalReleaseSimTime.HasValue)
                {
                    infeedPhysicalReleaseTime = reservation.InfeedPhysicalReleaseSimTime.Value;
                    ScheduleInfeedPortPhysicalRelease(job, infeedPhysicalReleaseTime);
                }
            }
            else if (zoneIndex == 0)
            {
                job.State = WarehouseJobState.OnConveyor;
            }

            MaybeFlushEdgeSegmentSchedule(job, zoneIndex);

            if (_recordPlayback)
            {
                _conveyorSubTaskRecorder.RecordForZone(
                    job,
                    reservation.Zone,
                    zoneIndex,
                    isFirstZoneOnPath: zoneIndex == 0);
            }

            job.NextConveyorZoneIndex = zoneIndex + 1;
            stepEndTime = reservation.NextStepStartTime;

            if (IsOutboundPickupEntryZone(job, reservation.Zone, chain))
            {
                var departTime = reservation.Zone.ArriveSimTime - reservation.Zone.HopSeconds;
                SetOutboundPickupHold(job, departTime);
                ScheduleOutboundPickupDeparture(job, departTime);
            }

            var isLast = zoneIndex >= chain.Count - 1;
            if (_recordPlayback && isLast)
            {
                EnsurePlaybackSnapshot(job);
                RecordPlaybackAt(
                    reservation.Zone.LeaveSimTime,
                    job,
                    SimPlaybackPhase.ConveyorRouted,
                    job.AssignedStackerId,
                    job.TargetSlot,
                    attachPathContext: true);
            }

            if (isLast)
            {
                // 取货点 zone：ConveyorTransitComplete 在货物"到达"取货点时触发，而非等到 zone 资源释放。
                // 这样 TryBeginStackerPhase 的 desired=_clock.Now 与 stackerStart 对齐，
                // 能正确插入 StackerWait（若堆垛机暂时占用），消除停留与 StackerPick 的时间轴重叠。
                var transitCompleteTime = reservation.Zone.Kind switch
                {
                    ConveyorPathZoneKind.Pickup => reservation.Zone.ArriveSimTime,
                    ConveyorPathZoneKind.Outfeed => reservation.Zone.ArriveSimTime,
                    _ => stepEndTime,
                };
                job.ScheduledCompleteTime = transitCompleteTime;
                _queue.Enqueue(new ScheduledSimEvent(
                    transitCompleteTime,
                    SimEventType.ConveyorTransitComplete,
                    job.JobId));
                WarehouseSimLog.Info(() =>
                    $"输送末 zone 预约 job={job.JobId} zone={zoneIndex} kind={reservation.Zone.Kind} 结束 t={transitCompleteTime:F2}s");
            }
            else
            {
                _queue.Enqueue(new ScheduledSimEvent(
                    stepEndTime,
                    SimEventType.ConveyorZoneComplete,
                    job.JobId,
                    zoneIndex));
                var nextStepTime = stepEndTime;
                WarehouseSimLog.Info(() =>
                    $"输送 zone 预约 job={job.JobId} zone={zoneIndex} kind={reservation.Zone.Kind} " +
                    $"resource={reservation.Zone.ResourceId} 下一时刻 t={nextStepTime:F2}s");
            }

            return true;
        }

        /// <summary>
        /// 下一段入口 zone 就绪后，按最终时刻补全路口 zone 占用（释放旧窗再写入扩展窗）。
        /// 路口 zone 首次预约时下游仅估算，实际等待可能更长；不补全会导致后续任务路口占用重叠。
        /// </summary>
        private void ReconcileJunctionZoneAfterNextZoneReserved(
            WarehouseJob job,
            int junctionZoneChainIndex,
            in ConveyorPathZone nextZone)
        {
            var chain = job?.ConveyorPathZones;
            var schedule = job?.ConveyorSegmentSchedule;
            if (chain == null
                || junctionZoneChainIndex < 0
                || junctionZoneChainIndex >= chain.Count
                || schedule == null
                || schedule.Count == 0)
            {
                return;
            }

            var junction = chain[junctionZoneChainIndex];
            if (junction.Kind != ConveyorPathZoneKind.Junction)
            {
                return;
            }

            var segIdx = schedule.Count - 1;
            var seg = schedule[segIdx];
            if (seg.ToNodeIndex != junction.ToNodeIndex)
            {
                segIdx = -1;
                for (var i = schedule.Count - 1; i >= 0; i--)
                {
                    if (schedule[i].ToNodeIndex == junction.ToNodeIndex)
                    {
                        segIdx = i;
                        seg = schedule[i];
                        break;
                    }
                }

                if (segIdx < 0)
                {
                    return;
                }
            }

            var hop = junction.HopSeconds;
            var nextStub = new ConveyorSegmentScheduleEntry
            {
                FromNodeIndex = nextZone.FromNodeIndex,
                ToNodeIndex = nextZone.ToNodeIndex,
                DesiredEntrySimTime = nextZone.DesiredArriveSimTime,
                EntrySimTime = nextZone.ArriveSimTime,
                ExitSimTime = nextZone.LeaveSimTime,
                StopArriveSimTimes = new[] { nextZone.ArriveSimTime },
            };

            if (!JunctionSubTaskTiming.TryGetJunctionHoldWindow(
                    seg,
                    nextStub,
                    _conveyorTopology,
                    _bindings.ConveyorMap,
                    hop,
                    out var holdStart,
                    out var holdEnd))
            {
                return;
            }

            var oldStart = junction.ArriveSimTime - junction.HopSeconds;
            var oldEnd = junction.LeaveSimTime;
            if (holdEnd <= oldEnd + 1e-9 && Math.Abs(holdStart - oldStart) < 1e-9)
            {
                return;
            }

            if (holdEnd > oldEnd + 1e-9)
            {
                EvictOverlappingJunctionHolds(
                    junction.ResourceId,
                    job.JobId,
                    oldEnd,
                    holdEnd,
                    junctionZoneChainIndex);
            }

            _reservations.Release(junction.ResourceId, oldStart, oldEnd);
            _reservations.TryReserve(junction.ResourceId, holdStart, holdEnd, out _);

            junction.LeaveSimTime = holdEnd;
            chain[junctionZoneChainIndex] = junction;

            seg.ExitSimTime = holdStart;
            seg.OccupancyEndSimTime = Math.Max(seg.OccupancyEndSimTime, holdEnd);
            schedule[segIdx] = seg;
        }

        /// <summary>
        /// 路口占用因下游实际时刻延长时，驱逐在旧尾端与新尾端之间抢入的路口预约。
        /// </summary>
        private void EvictOverlappingJunctionHolds(
            string junctionResourceId,
            int extendingJobId,
            double previousHoldEnd,
            double newHoldEnd,
            int junctionZoneChainIndex)
        {
            foreach (var other in _jobs.Values)
            {
                if (other == null || other.JobId == extendingJobId)
                {
                    continue;
                }

                if (other.State != WarehouseJobState.OnConveyor)
                {
                    continue;
                }

                var chain = other.ConveyorPathZones;
                if (chain == null)
                {
                    continue;
                }

                var otherJunctionIndex = -1;
                for (var zi = 0; zi < chain.Count; zi++)
                {
                    if (chain[zi].Kind == ConveyorPathZoneKind.Junction
                        && string.Equals(chain[zi].ResourceId, junctionResourceId, StringComparison.Ordinal))
                    {
                        otherJunctionIndex = zi;
                        break;
                    }
                }

                if (otherJunctionIndex < 0 || other.NextConveyorZoneIndex <= otherJunctionIndex)
                {
                    continue;
                }

                var otherJunction = chain[otherJunctionIndex];
                var theirHoldStart = otherJunction.ArriveSimTime - otherJunction.HopSeconds;
                if (theirHoldStart < previousHoldEnd - 1e-9 || theirHoldStart >= newHoldEnd - 1e-9)
                {
                    continue;
                }

                RewindConveyorFromZoneIndex(other, otherJunctionIndex, newHoldEnd, junctionResourceId);
            }
        }

        private void RewindConveyorFromZoneIndex(
            WarehouseJob job,
            int zoneIndex,
            double retryWhen,
            string blockingResourceId)
        {
            ReleaseConveyorZoneReservations(job, zoneIndex);
            TruncateConveyorScheduleFromZone(job, zoneIndex);
            job.NextConveyorZoneIndex = zoneIndex;
            MarkConveyorRoutePending(job, blockingResourceId);
            ScheduleConveyorRouteRetry(job, retryWhen);
            WarehouseSimLog.Warn(
                $"路口占用延长，重排输送 job={job.JobId} 从 zone={zoneIndex} 重试 t={retryWhen:F2}s resource={blockingResourceId}");
        }

        private void ReleaseConveyorZoneReservations(WarehouseJob job, int fromZoneIndex)
        {
            var chain = job?.ConveyorPathZones;
            if (chain == null || fromZoneIndex < 0)
            {
                return;
            }

            var releaseThrough = Math.Min(job.NextConveyorZoneIndex, chain.Count);
            for (var i = fromZoneIndex; i < releaseThrough; i++)
            {
                var zone = chain[i];
                var releaseStart = zone.ArriveSimTime - zone.HopSeconds;
                if (releaseStart < 0)
                {
                    releaseStart = 0;
                }

                var releaseEnd = zone.LeaveSimTime;
                if (releaseEnd <= releaseStart + 1e-9)
                {
                    releaseEnd = zone.ArriveSimTime;
                }

                if (releaseEnd > releaseStart + 1e-9)
                {
                    _reservations.Release(zone.ResourceId, releaseStart, releaseEnd);
                }
            }
        }

        private static void TruncateConveyorScheduleFromZone(WarehouseJob job, int fromZoneIndex)
        {
            var schedule = job?.ConveyorSegmentSchedule;
            var chain = job?.ConveyorPathZones;
            if (schedule == null || schedule.Count == 0 || chain == null || fromZoneIndex >= chain.Count)
            {
                return;
            }

            var anchorNode = chain[fromZoneIndex].ToNodeIndex;
            for (var i = schedule.Count - 1; i >= 0; i--)
            {
                if (schedule[i].ToNodeIndex == anchorNode)
                {
                    schedule.RemoveRange(i, schedule.Count - i);
                    break;
                }
            }

            if (job.ConveyorEdgeSubTasksRecorded == null)
            {
                return;
            }

            var edgeIndex = chain[fromZoneIndex].PathEdgeIndex;
            job.ConveyorEdgeSubTasksRecorded.RemoveWhere(e => e >= edgeIndex);
        }

        /// <summary>
        /// 路口 zone 预约（含 FIFO 顺延）完成后，用其真实预约窗回填来向边路段时刻表。
        /// 来向边在路口之前 flush，故其退出/占用结束时刻原本不含路口的实际占用，需在此校正。
        /// </summary>
        private static void UpdateApproachSegmentScheduleForJunction(WarehouseJob job, in ConveyorPathZone junctionZone)
        {
            var schedule = job.ConveyorSegmentSchedule;
            if (schedule == null || schedule.Count == 0)
            {
                return;
            }

            var idx = schedule.Count - 1;
            var seg = schedule[idx];
            if (seg.ToNodeIndex != junctionZone.ToNodeIndex)
            {
                return;
            }

            // 路口占用起点 = 驶入 hop 起点 = 到达路口时刻 - 一跳；终点 = 路口 zone 实际离开时刻。
            seg.ExitSimTime = junctionZone.ArriveSimTime - junctionZone.HopSeconds;
            seg.OccupancyEndSimTime = junctionZone.LeaveSimTime;
            schedule[idx] = seg;
        }

        /// <summary>路口/取货点实际占用确定后，同步来向边 slot-0 zone 的 Leave，并更新预约表占用，供后续子任务补记使用。</summary>
        private void SyncApproachS0ZoneLeave(
            WarehouseJob job,
            int downstreamZoneIndex,
            double s0LeaveSimTime)
        {
            var chain = job?.ConveyorPathZones;
            if (chain == null
                || downstreamZoneIndex < 0
                || downstreamZoneIndex >= chain.Count
                || s0LeaveSimTime <= 0)
            {
                return;
            }

            var approachEdgeIndex = chain[downstreamZoneIndex].PathEdgeIndex;
            for (var i = downstreamZoneIndex - 1; i >= 0; i--)
            {
                var zone = chain[i];
                if (zone.Kind != ConveyorPathZoneKind.EdgeSlot
                    || zone.SlotIndex != 0
                    || zone.PathEdgeIndex != approachEdgeIndex)
                {
                    continue;
                }

                if (zone.LeaveSimTime >= s0LeaveSimTime - 1e-9)
                {
                    return;
                }

                var oldLeave = zone.LeaveSimTime;
                zone.LeaveSimTime = s0LeaveSimTime;
                chain[i] = zone;

                // 同步更新预约表中的 slot 0 占用，确保其他货箱在规划路径时能看到最新的占用时间，防止冲突
                _reservations.Release(zone.ResourceId, zone.ArriveSimTime, oldLeave);
                _reservations.TryReserve(zone.ResourceId, zone.ArriveSimTime, s0LeaveSimTime, out _);
                return;
            }
        }

        /// <summary>取货点 zone 预约完成后，用其真实预约窗回填来向边路段时刻表。</summary>
        private static void UpdateApproachSegmentScheduleForPickup(WarehouseJob job, in ConveyorPathZone pickupZone)
        {
            var schedule = job.ConveyorSegmentSchedule;
            if (schedule == null || schedule.Count == 0)
            {
                return;
            }

            var idx = schedule.Count - 1;
            var seg = schedule[idx];
            if (seg.ToNodeIndex != pickupZone.ToNodeIndex)
            {
                return;
            }

            seg.ExitSimTime = pickupZone.ArriveSimTime - pickupZone.HopSeconds;
            seg.OccupancyEndSimTime = pickupZone.LeaveSimTime;
            schedule[idx] = seg;
        }

        private void MaybeFlushEdgeSegmentSchedule(WarehouseJob job, int completedZoneIndex)
        {
            var chain = job.ConveyorPathZones;
            if (chain == null || completedZoneIndex < 0 || completedZoneIndex >= chain.Count)
            {
                return;
            }

            var completed = chain[completedZoneIndex];
            if (completed.Kind != ConveyorPathZoneKind.EdgeSlot)
            {
                return;
            }

            if (completedZoneIndex + 1 < chain.Count
                && chain[completedZoneIndex + 1].Kind == ConveyorPathZoneKind.EdgeSlot
                && chain[completedZoneIndex + 1].PathEdgeIndex == completed.PathEdgeIndex)
            {
                return;
            }

            var desiredEntry = completed.PathEdgeIndex == 0
                ? job.Direction == SimFlowDirection.Outbound
                    ? job.PickupCompleteSimTime > 0 ? job.PickupCompleteSimTime : _clock.Now
                    : job.InfeedCompleteSimTime > 0 ? job.InfeedCompleteSimTime : _clock.Now
                : chain[FindFirstZoneOnEdge(chain, completed.PathEdgeIndex)].DesiredArriveSimTime;

            ConveyorTransitScheduler.AppendEdgeSegmentSchedule(
                job,
                completed.PathEdgeIndex,
                chain,
                desiredEntry);

            var endsAtJunction = completedZoneIndex + 1 < chain.Count
                                 && chain[completedZoneIndex + 1].Kind == ConveyorPathZoneKind.Junction;
            var endsAtPickup = completedZoneIndex + 1 < chain.Count
                               && chain[completedZoneIndex + 1].Kind == ConveyorPathZoneKind.Pickup;
            var endsAtOutfeed = completedZoneIndex + 1 < chain.Count
                                && chain[completedZoneIndex + 1].Kind == ConveyorPathZoneKind.Outfeed;
            var endsAtProcess = completedZoneIndex + 1 < chain.Count
                                && chain[completedZoneIndex + 1].Kind == ConveyorPathZoneKind.ProcessStation;
            var endsAtVerticalTransfer = completedZoneIndex + 1 < chain.Count
                                         && chain[completedZoneIndex + 1].Kind == ConveyorPathZoneKind.VerticalTransfer;
            if (_recordPlayback && !endsAtJunction && !endsAtPickup && !endsAtOutfeed && !endsAtProcess && !endsAtVerticalTransfer)
            {
                _conveyorSubTaskRecorder.RecordEdgeSlotSubTasksFromChain(job, completed.PathEdgeIndex);
            }
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

        #endregion

        #region 取货点与路径重试

        private void OnConveyorZoneComplete(WarehouseJob job, int completedZoneIndex)
        {
            if (job.State != WarehouseJobState.OnConveyor)
            {
                return;
            }

            if (job.NextConveyorZoneIndex != completedZoneIndex + 1)
            {
                return;
            }

            if (!TryScheduleConveyorZone(job, _clock.Now, out _, out _))
            {
                var chain = job.ConveyorPathZones;
                var zoneIndex = job.NextConveyorZoneIndex;
                var blockingResource = chain != null && zoneIndex >= 0 && zoneIndex < chain.Count
                    ? chain[zoneIndex].ResourceId
                    : null;
                MarkConveyorRoutePending(job, blockingResource);
            }
        }

        private void IncrementPickupReservation(int pickupNodeIndex)
        {
            if (_pickupReservationCounts == null
                || pickupNodeIndex < 0
                || pickupNodeIndex >= _pickupReservationCounts.Length)
            {
                return;
            }

            _pickupReservationCounts[pickupNodeIndex]++;
        }

        private void DecrementPickupReservation(int pickupNodeIndex)
        {
            if (_pickupReservationCounts == null
                || pickupNodeIndex < 0
                || pickupNodeIndex >= _pickupReservationCounts.Length)
            {
                return;
            }

            _pickupReservationCounts[pickupNodeIndex] =
                Math.Max(0, _pickupReservationCounts[pickupNodeIndex] - 1);
            NotifyConveyorRouteWaiters();
        }

        private void NotifyConveyorRouteWaiters()
        {
            RetryInboundConveyorRouteWaiters();
            RetryOutboundConveyorRouteWaiters();
            ScheduleInfeedQueueFill(_clock.Now);
        }

        private void RetryInboundConveyorRouteWaiters()
        {
            if (_inboundAwaitingConveyorRoute.Count == 0)
            {
                return;
            }

            var pending = new int[_inboundAwaitingConveyorRoute.Count];
            _inboundAwaitingConveyorRoute.CopyTo(pending);
            for (var i = 0; i < pending.Length; i++)
            {
                var jobId = pending[i];
                if (!_jobs.TryGetValue(jobId, out var job) || job == null)
                {
                    _inboundAwaitingConveyorRoute.Remove(jobId);
                    continue;
                }

                if (job.State != WarehouseJobState.WaitingInfeed
                    || job.InfeedPortIndex < 0
                    || IsConveyorTransitCommitted(job)
                    || _clock.Now + 1e-9 < job.ScheduledCompleteTime)
                {
                    _inboundAwaitingConveyorRoute.Remove(jobId);
                    continue;
                }

                TryBeginConveyor(job);
            }
        }

        private void RetryOutboundConveyorRouteWaiters()
        {
            if (_outboundAwaitingConveyorRoute.Count == 0)
            {
                return;
            }

            var pending = new int[_outboundAwaitingConveyorRoute.Count];
            _outboundAwaitingConveyorRoute.CopyTo(pending);
            for (var i = 0; i < pending.Length; i++)
            {
                var jobId = pending[i];
                if (!_jobs.TryGetValue(jobId, out var job) || job == null)
                {
                    _outboundAwaitingConveyorRoute.Remove(jobId);
                    continue;
                }

                if (job.State is WarehouseJobState.FailedNoCargo or WarehouseJobState.FailedNoSlot
                        or WarehouseJobState.Completed
                    || job.PickupPointIndex < 0
                    || job.PickupCompleteSimTime <= 0)
                {
                    _outboundAwaitingConveyorRoute.Remove(jobId);
                    continue;
                }

                TryBeginConveyor(job);
            }
        }

        /// <summary>输送到达终点：入库进入堆垛机阶段，出库进入出库口服务。</summary>
        private void OnConveyorComplete(WarehouseJob job)
        {
            if (job.State != WarehouseJobState.OnConveyor)
            {
                return;
            }

            RecordPlayback(job, SimPlaybackPhase.ConveyorDone, job.AssignedStackerId, job.TargetSlot);

            if (job.Direction == SimFlowDirection.Outbound)
            {
                BeginOutfeed(job);
                return;
            }

            job.State = WarehouseJobState.WaitingStacker;
            DecrementPickupReservation(job.PickupPointIndex);
            TryBeginStackerPhase(job);
        }

        #endregion
    }
}
