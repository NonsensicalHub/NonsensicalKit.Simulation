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
        /// <summary>路段方向键 → 正在占用该方向令牌的任务。对向推迟时只查这一份，不扫全部在途任务。</summary>
        private readonly Dictionary<string, List<int>> _jobsByLaneDirection = new();
        /// <summary>zone 资源 → 已预约过该资源的任务。延长占用时只查这一份。</summary>
        private readonly Dictionary<string, List<int>> _jobsByZoneResource = new();
        private readonly List<int> _zoneJobScratch = new();
        private readonly List<(WarehouseJob Job, int ZoneIndex)> _evictVictims = new();

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

        /// <summary>预约 zone 前先拿该处要求的通道锁（见 CorridorLocks partial），拿不到就停在当前位置等。</summary>
        private bool TryScheduleConveyorZone(
            WarehouseJob job,
            double desiredStart,
            out double stepEndTime,
            out double infeedPhysicalReleaseTime)
        {
            var zoneIndex = job.NextConveyorZoneIndex;
            if (zoneIndex == 0 && desiredStart < _clock.Now)
            {
                desiredStart = _clock.Now;
            }

            if (!TryAcquireCorridorAt(job, zoneIndex))
            {
                stepEndTime = desiredStart;
                infeedPhysicalReleaseTime = desiredStart;
                return false;
            }

            if (!TryScheduleConveyorZoneCore(job, desiredStart, out stepEndTime, out infeedPhysicalReleaseTime))
            {
                RollbackCorridorAcquisition(job);
                return false;
            }

            CommitCorridorStep(job);
            EvaluateCorridorRelease(job);
            return true;
        }

        private bool TryScheduleConveyorZoneCore(
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
                if (WarehouseSimLog.WarnEnabled)
                {
                    WarehouseSimLog.Warn(
                        $"输送 zone 预约失败 job={job.JobId} zone={zoneIndex} resource={chain[zoneIndex].ResourceId}，将重试。");
                }
                return false;
            }

            chain[zoneIndex] = reservation.Zone;
            if (!string.IsNullOrEmpty(reservation.Zone.LaneResourceId))
            {
                RememberSegmentLane(job.JobId, reservation.Zone.LaneResourceId);
            }

            if (!string.IsNullOrEmpty(reservation.Zone.AxisResourceId))
            {
                RememberZoneResource(job.JobId, reservation.Zone.AxisResourceId);
            }

            if (!string.IsNullOrEmpty(reservation.Zone.ExitLaneResourceId))
            {
                RememberSegmentLane(job.JobId, reservation.Zone.ExitLaneResourceId);
            }

            RememberZoneResource(job.JobId, reservation.Zone.ResourceId);

            TrimSameEdgePredecessorTail(job, zoneIndex);

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

            if (zoneIndex > 0
                && reservation.Zone.Kind == ConveyorPathZoneKind.EdgeSlot
                && chain[zoneIndex - 1].Kind == ConveyorPathZoneKind.EdgeSlot
                && chain[zoneIndex - 1].PathEdgeIndex != reservation.Zone.PathEdgeIndex)
            {
                var hopStart = reservation.Zone.ArriveSimTime - reservation.Zone.HopSeconds;
                SyncPassThroughExitLeave(job, zoneIndex, hopStart);
                UpdatePassThroughScheduleExit(job, chain[zoneIndex - 1], hopStart);
                if (_recordPlayback)
                {
                    _conveyorSubTaskRecorder.RecordEdgeSlotSubTasksFromChain(
                        job,
                        chain[zoneIndex - 1].PathEdgeIndex);
                }
            }

            if (zoneIndex > 0 && chain[zoneIndex - 1].Kind == ConveyorPathZoneKind.Junction)
            {
                if (ReconcileJunctionZoneAfterNextZoneReserved(job, zoneIndex - 1, reservation.Zone))
                {
                    return true;
                }

                if (_recordPlayback)
                {
                    var junctionZone = chain[zoneIndex - 1];
                    RemoveJunctionSubTasks(job.JobId, junctionZone.FromNodeIndex, junctionZone.ToNodeIndex);
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

            ExtendSegmentLane(job, zoneIndex);

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
                var completeAt = ResolveZoneCompleteEventTime(chain, zoneIndex, stepEndTime);
                var nextStepTime = stepEndTime;
                _queue.Enqueue(new ScheduledSimEvent(
                    completeAt,
                    SimEventType.ConveyorZoneComplete,
                    job.JobId,
                    zoneIndex));
                WarehouseSimLog.Info(() =>
                    $"输送 zone 预约 job={job.JobId} zone={zoneIndex} kind={reservation.Zone.Kind} " +
                    $"resource={reservation.Zone.ResourceId} 下一时刻 t={nextStepTime:F2}s");
            }

            return true;
        }

        /// <summary>
        /// 下一段入口 zone 就绪后，按最终时刻补全路口 zone 占用（释放旧窗再写入扩展窗）。
        /// 路口 zone 首次预约时下游仅估算，实际等待可能更长；不补全会导致后续任务路口占用重叠。
        /// 返回 true 表示下一段订得太早，已经退回去重试，调用方不要再提交这一段。
        /// </summary>
        private bool ReconcileJunctionZoneAfterNextZoneReserved(
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
                return false;
            }

            var junction = chain[junctionZoneChainIndex];
            if (junction.Kind != ConveyorPathZoneKind.Junction)
            {
                return false;
            }

            var segIdx = ConveyorSegmentScheduleNavigation.FindPathEdgeIndex(
                schedule,
                junction.PathEdgeIndex);
            if (segIdx < 0)
            {
                return false;
            }

            var seg = schedule[segIdx];

            var hop = junction.HopSeconds;
            var nextHop = nextZone.HopSeconds > 1e-9f ? nextZone.HopSeconds : hop;
            var nextHopStart = nextZone.ArriveSimTime - nextHop;
            // 下一段的 hop 在箱子到达路口之前就开始了：这轮预约作废，等路口真正放行再订。
            if (nextZone.ArriveSimTime > 0 && nextHopStart + 1e-9 < junction.ArriveSimTime)
            {
                RewindConveyorFromZoneIndex(
                    job,
                    junctionZoneChainIndex + 1,
                    Math.Max(junction.LeaveSimTime, junction.ArriveSimTime),
                    nextZone.ResourceId);
                return true;
            }

            var oldStart = junction.ArriveSimTime;
            if (oldStart < 0)
            {
                oldStart = 0;
            }

            var oldEnd = junction.LeaveSimTime;
            var holdStart = oldStart;
            var holdEnd = Math.Max(oldEnd, nextHopStart);
            if (holdEnd > oldEnd + 1e-9)
            {
                EvictOverlappingJunctionHolds(
                    junction.ResourceId,
                    job.JobId,
                    oldEnd,
                    holdEnd,
                    junctionZoneChainIndex);
                if (TryReplaceReservation(junction.ResourceId, oldStart, oldEnd, holdStart, holdEnd))
                {
                    junction.LeaveSimTime = holdEnd;
                    chain[junctionZoneChainIndex] = junction;
                    oldEnd = holdEnd;
                }
            }

            // 路段退出时刻是驶入路口的起点，不能用时刻表里可能已经过期的更晚退出时刻。
            var slotRelease = junction.ArriveSimTime - hop;
            if (slotRelease < seg.EntrySimTime)
            {
                slotRelease = seg.EntrySimTime;
            }

            seg.ExitSimTime = slotRelease;
            seg.OccupancyEndSimTime = Math.Max(seg.OccupancyEndSimTime, oldEnd);
            schedule[segIdx] = seg;
            return false;
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
            CollectActiveZoneJobs(junctionResourceId, extendingJobId, _zoneJobScratch);
            var candidateCount = _zoneJobScratch.Count;
            for (var n = 0; n < candidateCount; n++)
            {
                if (!_jobs.TryGetValue(_zoneJobScratch[n], out var other) || other == null)
                {
                    continue;
                }

                var chain = other.ConveyorPathZones;
                if (chain == null)
                {
                    continue;
                }

                var rewindFrom = -1;
                for (var zi = 0; zi < chain.Count; zi++)
                {
                    if (chain[zi].Kind != ConveyorPathZoneKind.Junction
                        || !string.Equals(chain[zi].ResourceId, junctionResourceId, StringComparison.Ordinal)
                        || other.NextConveyorZoneIndex <= zi)
                    {
                        continue;
                    }

                    var otherJunction = chain[zi];
                    var theirHoldStart = otherJunction.ArriveSimTime;
                    var theirHoldEnd = otherJunction.LeaveSimTime;
                    var overlapsExtension = theirHoldEnd > previousHoldEnd + 1e-9
                                            && theirHoldStart < newHoldEnd - 1e-9;
                    if (!overlapsExtension)
                    {
                        continue;
                    }

                    if (rewindFrom < 0 || zi < rewindFrom)
                    {
                        rewindFrom = zi;
                    }
                }

                if (rewindFrom >= 0)
                {
                    RewindConveyorFromZoneIndex(other, rewindFrom, newHoldEnd, junctionResourceId);
                }
            }
        }

        private void RewindConveyorFromZoneIndex(
            WarehouseJob job,
            int zoneIndex,
            double retryWhen,
            string blockingResourceId)
        {
            var chain = job?.ConveyorPathZones;
            DiscardRewoundConveyorSubTasks(job, zoneIndex);
            ReleaseConveyorZoneReservations(job, zoneIndex);
            if (chain != null
                && zoneIndex >= 0
                && zoneIndex < chain.Count
                && chain[zoneIndex].Kind == ConveyorPathZoneKind.EdgeSlot)
            {
                var slot = chain[zoneIndex];
                RemoveEdgeSubTasks(job, slot.FromNodeIndex, slot.ToNodeIndex);
                job.ConveyorEdgeSubTasksRecorded?.Remove(slot.PathEdgeIndex);
                RemoveScheduleEntryByPathEdge(job, slot.PathEdgeIndex);
            }
            else if (chain != null
                     && zoneIndex >= 0
                     && zoneIndex < chain.Count
                     && chain[zoneIndex].Kind == ConveyorPathZoneKind.Junction)
            {
                // 路口重排后到达时刻会变，来向边停留要按新时刻重记。
                var junction = chain[zoneIndex];
                RemoveEdgeSubTasks(job, junction.FromNodeIndex, junction.ToNodeIndex);
                job.ConveyorEdgeSubTasksRecorded?.Remove(junction.PathEdgeIndex);
            }

            // 首段的起点排队/驶离子任务只在订首个 zone 时记一次，上面按边清掉后要补回，否则时间轴留空白。
            if (_recordPlayback
                && chain != null
                && zoneIndex > 0
                && zoneIndex < chain.Count
                && chain[zoneIndex].PathEdgeIndex == 0)
            {
                _conveyorSubTaskRecorder.RecordForZone(job, chain[0], 0, isFirstZoneOnPath: true);
            }

            ClearZoneTimingFrom(job, zoneIndex);
            TruncateConveyorScheduleFromZone(job, zoneIndex);
            InvalidateJobScheduleSnapshots(job);
            job.NextConveyorZoneIndex = zoneIndex;
            MarkConveyorRoutePending(job, blockingResourceId);
            ScheduleConveyorRouteRetry(job, retryWhen, postpone: true);
            if (WarehouseSimLog.WarnEnabled)
            {
                WarehouseSimLog.Warn(
                    $"占用延长，重排输送 job={job.JobId} 从 zone={zoneIndex} 重试 t={retryWhen:F2}s resource={blockingResourceId}");
            }
        }

        /// <summary>
        /// 重排后旧子任务上的路段快照可能比截断后的表更长，自检会优先拿那份过期表。
        /// 清掉之后，只保留接下来重新记下来的时刻。
        /// </summary>
        private void InvalidateJobScheduleSnapshots(WarehouseJob job)
        {
            if (job == null)
            {
                return;
            }

            job.PlaybackScheduleSnapshot = null;
            if (!_recordPlayback || !TryGetJobSubTasks(job.JobId, out var tasks))
            {
                return;
            }

            for (var i = 0; i < tasks.Count; i++)
            {
                if (tasks[i].SegmentSchedule == null)
                {
                    continue;
                }

                var task = tasks[i];
                task.SegmentSchedule = null;
                tasks[i] = task;
            }
        }

        private static void ClearZoneTimingFrom(WarehouseJob job, int zoneIndex)
        {
            var chain = job?.ConveyorPathZones;
            if (chain == null || zoneIndex < 0)
            {
                return;
            }

            for (var i = zoneIndex; i < chain.Count; i++)
            {
                var zone = chain[i];
                zone.DesiredArriveSimTime = 0;
                zone.ArriveSimTime = 0;
                zone.LeaveSimTime = 0;
                zone.LaneResourceId = null;
                zone.LaneReserveStart = 0;
                zone.LaneReserveEnd = 0;
                zone.AxisResourceId = null;
                zone.AxisReserveStart = 0;
                zone.AxisReserveEnd = 0;
                zone.ExitLaneResourceId = null;
                zone.ExitLaneReserveStart = 0;
                zone.ExitLaneReserveEnd = 0;
                zone.SlotReserveStart = 0;
                zone.SlotReserveEnd = 0;
                chain[i] = zone;
            }
        }

        private void ReleaseConveyorZoneReservations(WarehouseJob job, int fromZoneIndex)
        {
            var chain = job?.ConveyorPathZones;
            if (chain == null || fromZoneIndex < 0)
            {
                return;
            }

            // 正在预约、索引还没往后拨的那一格也要放开，否则重试前旧窗口还占着。
            var releaseThrough = Math.Min(
                chain.Count,
                Math.Max(job.NextConveyorZoneIndex, fromZoneIndex + 1));
            for (var i = fromZoneIndex; i < releaseThrough; i++)
            {
                var zone = chain[i];
                var releaseStart = zone.Kind == ConveyorPathZoneKind.Junction
                    ? zone.ArriveSimTime
                    : zone.ArriveSimTime - zone.HopSeconds;
                if (releaseStart < 0)
                {
                    releaseStart = 0;
                }

                var releaseEnd = zone.LeaveSimTime;
                if (releaseEnd <= releaseStart + 1e-9)
                {
                    releaseEnd = zone.ArriveSimTime;
                }

                if (zone.Kind == ConveyorPathZoneKind.EdgeSlot)
                {
                    ConveyorPathZone? nextForEnd = i + 1 < chain.Count ? chain[i + 1] : null;
                    var reservedEnd = ConveyorTransitScheduler.CommittedSlotReservationEnd(zone, nextForEnd);
                    if (reservedEnd > releaseEnd)
                    {
                        releaseEnd = reservedEnd;
                    }
                }

                ForgetZoneResource(job.JobId, zone.ResourceId);
                if (releaseEnd > releaseStart + 1e-9)
                {
                    _reservations.Release(zone.ResourceId, releaseStart, releaseEnd);
                }

                if (!string.IsNullOrEmpty(zone.LaneResourceId)
                    && zone.LaneReserveEnd > zone.LaneReserveStart + 1e-9)
                {
                    ForgetSegmentLane(job.JobId, zone.LaneResourceId);
                    _reservations.Release(zone.LaneResourceId, zone.LaneReserveStart, zone.LaneReserveEnd);
                }

                if (!string.IsNullOrEmpty(zone.AxisResourceId)
                    && zone.AxisReserveEnd > zone.AxisReserveStart + 1e-9)
                {
                    ForgetZoneResource(job.JobId, zone.AxisResourceId);
                    _reservations.Release(zone.AxisResourceId, zone.AxisReserveStart, zone.AxisReserveEnd);
                }

                if (!string.IsNullOrEmpty(zone.ExitLaneResourceId)
                    && zone.ExitLaneReserveEnd > zone.ExitLaneReserveStart + 1e-9)
                {
                    ForgetSegmentLane(job.JobId, zone.ExitLaneResourceId);
                    _reservations.Release(zone.ExitLaneResourceId, zone.ExitLaneReserveStart, zone.ExitLaneReserveEnd);
                }

                if (i + 1 >= chain.Count || chain[i + 1].HopSeconds <= 1e-9f)
                {
                    continue;
                }

                var nextZone = chain[i + 1];
                if (nextZone.Kind != ConveyorPathZoneKind.EdgeSlot
                    || nextZone.PathEdgeIndex != zone.PathEdgeIndex)
                {
                    continue;
                }

                _reservations.Release(
                    nextZone.ResourceId,
                    zone.LeaveSimTime,
                    zone.LeaveSimTime + nextZone.HopSeconds);
            }
        }

        /// <summary>
        /// 下一停留点正式预约后，收回前一停留点为交接多占的 hop。
        /// 若实际起步晚于预计离开，则把前一停留点延长到真正让出的时刻。
        /// </summary>
        private void TrimSameEdgePredecessorTail(WarehouseJob job, int zoneIndex)
        {
            var chain = job?.ConveyorPathZones;
            if (chain == null || zoneIndex <= 0 || zoneIndex >= chain.Count)
            {
                return;
            }

            var prev = chain[zoneIndex - 1];
            var zone = chain[zoneIndex];
            if (prev.Kind != ConveyorPathZoneKind.EdgeSlot
                || zone.Kind != ConveyorPathZoneKind.EdgeSlot
                || prev.PathEdgeIndex != zone.PathEdgeIndex
                || zone.HopSeconds <= 1e-9f)
            {
                return;
            }

            var reserveStart = prev.ArriveSimTime - prev.HopSeconds;
            if (reserveStart < 0)
            {
                reserveStart = 0;
            }

            var oldEnd = prev.LeaveSimTime + zone.HopSeconds;
            var hopStart = zone.ArriveSimTime - zone.HopSeconds;
            var newEnd = hopStart > prev.LeaveSimTime + 1e-9 ? hopStart : prev.LeaveSimTime;
            if (newEnd <= reserveStart + 1e-9 || Math.Abs(newEnd - oldEnd) <= 1e-9)
            {
                return;
            }

            if (newEnd > prev.LeaveSimTime + 1e-9)
            {
                EvictEdgeSlotOccupants(prev.ResourceId, job.JobId, prev.LeaveSimTime, newEnd);
            }

            if (!TryReplaceReservation(prev.ResourceId, reserveStart, oldEnd, reserveStart, newEnd))
            {
                return;
            }

            if (newEnd <= prev.LeaveSimTime + 1e-9)
            {
                return;
            }

            prev.LeaveSimTime = newEnd;
            chain[zoneIndex - 1] = prev;
        }

        private static void TruncateConveyorScheduleFromZone(WarehouseJob job, int fromZoneIndex)
        {
            var schedule = job?.ConveyorSegmentSchedule;
            var chain = job?.ConveyorPathZones;
            if (schedule == null || schedule.Count == 0 || chain == null || fromZoneIndex >= chain.Count)
            {
                return;
            }

            // 按路径边删除，不按列表下标。中间段被单独摘掉再补记时，下游段会留在更前面，
            // 自检若把下一条当成下一段，会把路口等待算到十几秒。
            var dropFrom = chain[fromZoneIndex].Kind == ConveyorPathZoneKind.Junction
                ? fromZoneIndex + 1
                : fromZoneIndex;
            var dropEdge = int.MaxValue;
            for (var i = dropFrom; i < chain.Count; i++)
            {
                if (chain[i].PathEdgeIndex < dropEdge)
                {
                    dropEdge = chain[i].PathEdgeIndex;
                }
            }

            if (dropEdge < int.MaxValue)
            {
                schedule.RemoveAll(seg => seg.PathEdgeIndex >= dropEdge);
            }

            job.PlaybackScheduleSnapshot = null;

            if (job.ConveyorEdgeSubTasksRecorded == null)
            {
                return;
            }

            // 来向边已经走完，保留其子任务；只允许重记路口下游的边。
            var edgeIndex = chain[fromZoneIndex].PathEdgeIndex;
            job.ConveyorEdgeSubTasksRecorded.RemoveWhere(e => e > edgeIndex);
        }

        /// <summary>路口重排前丢掉该路口相位，以及已经记过、即将重走的下游边子任务。</summary>
        private void DiscardRewoundConveyorSubTasks(WarehouseJob job, int junctionZoneIndex)
        {
            var chain = job?.ConveyorPathZones;
            if (!_recordPlayback || chain == null || !TryGetJobSubTasks(job.JobId, out var tasks)
                || junctionZoneIndex < 0 || junctionZoneIndex >= chain.Count)
            {
                return;
            }

            var junction = chain[junctionZoneIndex];
            var through = Math.Min(job.NextConveyorZoneIndex, chain.Count);
            var downstream = new List<(int From, int To)>();
            for (var i = junctionZoneIndex + 1; i < through; i++)
            {
                var zone = chain[i];
                var exists = false;
                for (var k = 0; k < downstream.Count; k++)
                {
                    if (downstream[k].From == zone.FromNodeIndex && downstream[k].To == zone.ToNodeIndex)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    downstream.Add((zone.FromNodeIndex, zone.ToNodeIndex));
                }
            }

            tasks.RemoveAll(task =>
            {
                if (task.JobId != job.JobId)
                {
                    return false;
                }

                if (IsJunctionPhase(task.Kind)
                    && task.FromNodeIndex == junction.FromNodeIndex
                    && task.ToNodeIndex == junction.ToNodeIndex)
                {
                    return true;
                }

                if (!IsRewindableConveyorKind(task.Kind))
                {
                    return false;
                }

                for (var i = 0; i < downstream.Count; i++)
                {
                    if (task.FromNodeIndex == downstream[i].From && task.ToNodeIndex == downstream[i].To)
                    {
                        return true;
                    }
                }

                return false;
            });
        }

        private void RemoveJunctionSubTasks(int jobId, int fromNodeIndex, int toNodeIndex)
        {
            if (!_recordPlayback || !TryGetJobSubTasks(jobId, out var tasks))
            {
                return;
            }

            tasks.RemoveAll(task =>
                task.JobId == jobId
                && task.FromNodeIndex == fromNodeIndex
                && task.ToNodeIndex == toNodeIndex
                && IsJunctionPhase(task.Kind));
        }

        private static bool IsJunctionPhase(SimSubTaskKind kind) =>
            kind is SimSubTaskKind.JunctionEnter
                or SimSubTaskKind.JunctionWait
                or SimSubTaskKind.JunctionExit;

        private static bool IsRewindableConveyorKind(SimSubTaskKind kind) =>
            kind is SimSubTaskKind.SegmentHopMove
                or SimSubTaskKind.SegmentStopDwell
                or SimSubTaskKind.SegmentGateWait
                or SimSubTaskKind.SegmentTransit
                or SimSubTaskKind.SegmentQueue
                or SimSubTaskKind.InfeedMove
                or SimSubTaskKind.OutboundMove
                or SimSubTaskKind.ProcessStationService
                or SimSubTaskKind.VerticalTransferMove;

        /// <summary>
        /// 路口 zone 预约（含 FIFO 顺延）完成后，用其真实预约窗回填来向边路段时刻表。
        /// 来向边在路口之前 flush，故其退出/占用结束时刻原本不含路口的实际占用，需在此校正。
        /// </summary>
        private static void UpdateApproachSegmentScheduleForJunction(WarehouseJob job, in ConveyorPathZone junctionZone)
        {
            var schedule = job.ConveyorSegmentSchedule;
            var index = schedule == null
                ? -1
                : ConveyorSegmentScheduleNavigation.FindPathEdgeIndex(schedule, junctionZone.PathEdgeIndex);
            if (index < 0)
            {
                return;
            }

            var seg = schedule[index];
            // 路口占用起点 = 驶入 hop 起点 = 到达路口时刻 - 一跳；终点 = 路口 zone 实际离开时刻。
            seg.ExitSimTime = junctionZone.ArriveSimTime - junctionZone.HopSeconds;
            seg.OccupancyEndSimTime = junctionZone.LeaveSimTime;
            schedule[index] = seg;
        }

        /// <summary>
        /// 用新窗口替换旧预约。新窗口若不能落在请求的起点上，则撤销本次写入并恢复旧窗口。
        /// 调用方据此决定是否把更长的占用写进时刻表，避免表上已经顺延、时刻表仍声称原区间。
        /// </summary>
        private bool TryReplaceReservation(
            string resourceId,
            double oldStart,
            double oldEnd,
            double newStart,
            double newEnd)
        {
            if (string.IsNullOrEmpty(resourceId) || newEnd <= newStart + 1e-9)
            {
                return false;
            }

            var released = oldEnd > oldStart + 1e-9
                           && _reservations.Release(resourceId, oldStart, oldEnd);
            var accepted = _reservations.TryReserve(resourceId, newStart, newEnd, out var actualStart);
            if (accepted && actualStart <= newStart + 1e-9)
            {
                return true;
            }

            var duration = newEnd - newStart;
            _reservations.Release(resourceId, actualStart, actualStart + duration);
            if (released)
            {
                _reservations.TryReserve(resourceId, oldStart, oldEnd, out _);
            }

            return false;
        }

        /// <summary>
        /// 停留点占用延长前，把已经订进这段空档的箱子退回到该停留点重试。
        /// 已经往下游走的也要退，否则时刻表上两箱会叠在同一停留点。
        /// </summary>
        private void DeferApproachSlotsBookedIntoGap(
            string resourceId,
            int extendingJobId,
            double oldLeave,
            double newLeave) =>
            EvictEdgeSlotOccupants(resourceId, extendingJobId, oldLeave, newLeave);

        /// <summary>
        /// 出口等待变长后，把同向容量令牌续到车尾离开。
        /// 车尾离开 = 下游节点放行后再开出一个车身。对向与直线互斥由通道锁保证，这里不再顶开别的车。
        /// </summary>
        private void ExtendSegmentLane(WarehouseJob job, int zoneIndex)
        {
            var chain = job?.ConveyorPathZones;
            if (chain == null || zoneIndex < 0 || zoneIndex >= chain.Count)
            {
                return;
            }

            ExtendEdgeBodyHold(job, zoneIndex, chain[zoneIndex].PathEdgeIndex);
            if (zoneIndex > 0
                && chain[zoneIndex - 1].PathEdgeIndex != chain[zoneIndex].PathEdgeIndex)
            {
                ExtendEdgeBodyHold(job, zoneIndex, chain[zoneIndex - 1].PathEdgeIndex);
            }
        }

        private void ExtendEdgeBodyHold(WarehouseJob job, int scheduledZoneIndex, int pathEdgeIndex)
        {
            var chain = job?.ConveyorPathZones;
            if (chain == null)
            {
                return;
            }

            var entryIndex = -1;
            var exitIndex = -1;
            for (var i = 0; i < chain.Count; i++)
            {
                var candidate = chain[i];
                if (candidate.PathEdgeIndex != pathEdgeIndex
                    || candidate.Kind != ConveyorPathZoneKind.EdgeSlot)
                {
                    continue;
                }

                if (entryIndex < 0)
                {
                    entryIndex = i;
                }

                exitIndex = i;
            }

            if (entryIndex < 0 || exitIndex > scheduledZoneIndex)
            {
                return;
            }

            var entry = chain[entryIndex];
            if (string.IsNullOrEmpty(entry.LaneResourceId))
            {
                return;
            }

            var front = ConveyorTransitScheduler.ProjectSegmentClearTime(
                chain,
                exitIndex,
                chain[exitIndex].LeaveSimTime);
            var body = _conveyorScheduler.ProjectBodyClearTime(chain, exitIndex, front);
            if (body <= _clock.Now + 1e-3)
            {
                return;
            }

            if (body <= entry.LaneReserveEnd + 1e-3)
            {
                return;
            }

            if (!TryReplaceReservation(
                    entry.LaneResourceId,
                    entry.LaneReserveStart,
                    entry.LaneReserveEnd,
                    entry.LaneReserveStart,
                    body))
            {
                return;
            }

            entry.LaneReserveEnd = body;
            chain[entryIndex] = entry;
        }

        private void RememberSegmentLane(int jobId, string laneId)
        {
            if (!ConveyorTransitScheduler.TryGetLaneDirectionKeys(laneId, out var directionKey, out _))
            {
                return;
            }

            if (!_jobsByLaneDirection.TryGetValue(directionKey, out var jobs))
            {
                jobs = new List<int>();
                _jobsByLaneDirection[directionKey] = jobs;
            }

            for (var i = 0; i < jobs.Count; i++)
            {
                if (jobs[i] == jobId)
                {
                    return;
                }
            }

            jobs.Add(jobId);
        }

        private void ForgetSegmentLane(int jobId, string laneId)
        {
            if (!ConveyorTransitScheduler.TryGetLaneDirectionKeys(laneId, out var directionKey, out _)
                || !_jobsByLaneDirection.TryGetValue(directionKey, out var jobs))
            {
                return;
            }

            for (var i = jobs.Count - 1; i >= 0; i--)
            {
                if (jobs[i] == jobId)
                {
                    jobs.RemoveAt(i);
                }
            }
        }

        private void RememberZoneResource(int jobId, string resourceId)
        {
            if (jobId < 0 || string.IsNullOrEmpty(resourceId))
            {
                return;
            }

            if (!_jobsByZoneResource.TryGetValue(resourceId, out var jobs))
            {
                jobs = new List<int>();
                _jobsByZoneResource[resourceId] = jobs;
            }

            for (var i = 0; i < jobs.Count; i++)
            {
                if (jobs[i] == jobId)
                {
                    return;
                }
            }

            jobs.Add(jobId);
        }

        private void ForgetZoneResource(int jobId, string resourceId)
        {
            if (string.IsNullOrEmpty(resourceId)
                || !_jobsByZoneResource.TryGetValue(resourceId, out var jobs))
            {
                return;
            }

            for (var i = jobs.Count - 1; i >= 0; i--)
            {
                if (jobs[i] == jobId)
                {
                    jobs.RemoveAt(i);
                }
            }
        }

        private void ForgetJobZoneResources(WarehouseJob job)
        {
            var chain = job?.ConveyorPathZones;
            if (chain == null)
            {
                return;
            }

            for (var i = 0; i < chain.Count; i++)
            {
                ForgetZoneResource(job.JobId, chain[i].ResourceId);
                if (!string.IsNullOrEmpty(chain[i].LaneResourceId))
                {
                    ForgetSegmentLane(job.JobId, chain[i].LaneResourceId);
                }

                if (!string.IsNullOrEmpty(chain[i].AxisResourceId))
                {
                    ForgetZoneResource(job.JobId, chain[i].AxisResourceId);
                }

                if (!string.IsNullOrEmpty(chain[i].ExitLaneResourceId))
                {
                    ForgetSegmentLane(job.JobId, chain[i].ExitLaneResourceId);
                }
            }
        }

        private void CollectActiveZoneJobs(string resourceId, int exceptJobId, List<int> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(resourceId)
                || !_jobsByZoneResource.TryGetValue(resourceId, out var jobs))
            {
                return;
            }

            for (var i = jobs.Count - 1; i >= 0; i--)
            {
                var jobId = jobs[i];
                if (jobId == exceptJobId)
                {
                    continue;
                }

                if (!_jobs.TryGetValue(jobId, out var job)
                    || job == null
                    || job.State != WarehouseJobState.OnConveyor)
                {
                    jobs.RemoveAt(i);
                    continue;
                }

                into.Add(jobId);
            }
        }

        private void EvictEdgeSlotOccupants(
            string resourceId,
            int extendingJobId,
            double oldLeave,
            double newLeave)
        {
            if (string.IsNullOrEmpty(resourceId) || newLeave <= oldLeave + 1e-9)
            {
                return;
            }

            CollectActiveZoneJobs(resourceId, extendingJobId, _zoneJobScratch);
            var candidateCount = _zoneJobScratch.Count;
            _evictVictims.Clear();
            for (var n = 0; n < candidateCount; n++)
            {
                if (!_jobs.TryGetValue(_zoneJobScratch[n], out var other) || other == null)
                {
                    continue;
                }

                var chain = other.ConveyorPathZones;
                if (chain == null)
                {
                    continue;
                }

                for (var zi = 0; zi < chain.Count; zi++)
                {
                    var slot = chain[zi];
                    if (slot.Kind != ConveyorPathZoneKind.EdgeSlot
                        || !string.Equals(slot.ResourceId, resourceId, StringComparison.Ordinal)
                        || other.NextConveyorZoneIndex <= zi)
                    {
                        continue;
                    }

                    var theirStart = slot.ArriveSimTime - slot.HopSeconds;
                    var theirEnd = slot.LeaveSimTime;
                    if (theirEnd <= oldLeave + 1e-9 || theirStart >= newLeave - 1e-9)
                    {
                        continue;
                    }

                    _evictVictims.Add((other, zi));
                    break;
                }
            }

            var victimCount = _evictVictims.Count;
            for (var i = 0; i < victimCount; i++)
            {
                var victim = _evictVictims[i];
                RewindConveyorFromZoneIndex(
                    victim.Job,
                    victim.ZoneIndex,
                    newLeave,
                    resourceId);
            }
        }

        private static void RemoveScheduleEntryByPathEdge(WarehouseJob job, int pathEdgeIndex)
        {
            var schedule = job?.ConveyorSegmentSchedule;
            if (schedule == null || pathEdgeIndex < 0)
            {
                return;
            }

            for (var i = schedule.Count - 1; i >= 0; i--)
            {
                if (schedule[i].PathEdgeIndex != pathEdgeIndex)
                {
                    continue;
                }

                schedule.RemoveAt(i);
                return;
            }
        }

        private void RemoveEdgeSubTasks(WarehouseJob job, int fromNodeIndex, int toNodeIndex)
        {
            if (!_recordPlayback || job == null || !TryGetJobSubTasks(job.JobId, out var tasks))
            {
                return;
            }

            tasks.RemoveAll(task =>
                task.JobId == job.JobId
                && task.FromNodeIndex == fromNodeIndex
                && task.ToNodeIndex == toNodeIndex
                && IsRewindableConveyorKind(task.Kind));
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
                var reserveStart = zone.ArriveSimTime - zone.HopSeconds;
                if (reserveStart < 0)
                {
                    reserveStart = 0;
                }

                var approach = chain[downstreamZoneIndex].HopSeconds;
                if (approach < 0f)
                {
                    approach = 0f;
                }

                var oldReservedEnd = oldLeave + approach;
                var newReservedEnd = s0LeaveSimTime + approach;
                DeferApproachSlotsBookedIntoGap(zone.ResourceId, job.JobId, oldReservedEnd, newReservedEnd);
                if (!TryReplaceReservation(
                        zone.ResourceId,
                        reserveStart,
                        oldReservedEnd,
                        reserveStart,
                        newReservedEnd))
                {
                    return;
                }

                zone.LeaveSimTime = s0LeaveSimTime;
                zone.SlotReserveStart = reserveStart;
                zone.SlotReserveEnd = newReservedEnd;
                chain[i] = zone;
                return;
            }
        }

        /// <summary>
        /// 下一段入口槽订上之后，把上一段出口槽的占用延到这一跳真正开始。
        /// 路过交互点/出库口时没有节点区，出口槽在预约时只能估算下游空闲时间。
        /// </summary>
        private void SyncPassThroughExitLeave(WarehouseJob job, int entryZoneIndex, double hopStart)
        {
            var chain = job?.ConveyorPathZones;
            if (chain == null || entryZoneIndex <= 0 || hopStart < 0)
            {
                return;
            }

            var exitIndex = entryZoneIndex - 1;
            var exit = chain[exitIndex];
            if (exit.Kind != ConveyorPathZoneKind.EdgeSlot
                || exit.SlotIndex != 0
                || exit.LeaveSimTime >= hopStart - 1e-9)
            {
                return;
            }

            var oldLeave = exit.LeaveSimTime;
            var reserveStart = exit.ArriveSimTime - exit.HopSeconds;
            if (reserveStart < 0)
            {
                reserveStart = 0;
            }

            if (!TryReplaceReservation(exit.ResourceId, reserveStart, oldLeave, reserveStart, hopStart))
            {
                return;
            }

            exit.LeaveSimTime = hopStart;
            exit.SlotReserveStart = reserveStart;
            exit.SlotReserveEnd = hopStart;
            chain[exitIndex] = exit;
        }

        private static void UpdatePassThroughScheduleExit(
            WarehouseJob job,
            in ConveyorPathZone exitZone,
            double hopStart)
        {
            var schedule = job?.ConveyorSegmentSchedule;
            if (schedule == null || schedule.Count == 0 || hopStart < 0)
            {
                return;
            }

            var index = ConveyorSegmentScheduleNavigation.FindPathEdgeIndex(schedule, exitZone.PathEdgeIndex);
            if (index < 0)
            {
                return;
            }

            var seg = schedule[index];
            if (hopStart <= seg.ExitSimTime + 1e-9)
            {
                return;
            }

            seg.ExitSimTime = hopStart;
            if (seg.OccupancyEndSimTime < hopStart)
            {
                seg.OccupancyEndSimTime = hopStart;
            }

            schedule[index] = seg;
        }

        /// <summary>
        /// 下游节点 zone 预约完成后，用其真实预约窗回填来向边路段时刻表。
        /// 加工站/提升机与 <see cref="ConveyorTransitScheduler"/> 的 lane 规则一致：
        /// 来向边占用止于进站（Arrive），服务 dwell 不计入 approach occupancy。
        /// </summary>
        private void UpdateApproachSegmentScheduleForPickup(WarehouseJob job, in ConveyorPathZone nodeZone)
        {
            var schedule = job.ConveyorSegmentSchedule;
            var idx = schedule == null
                ? -1
                : ConveyorSegmentScheduleNavigation.FindPathEdgeIndex(schedule, nodeZone.PathEdgeIndex);
            if (idx < 0)
            {
                return;
            }

            var seg = schedule[idx];
            seg.ExitSimTime = nodeZone.ArriveSimTime - nodeZone.HopSeconds;
            // Pickup/Outfeed：货物仍压在来向边上直到节点 Leave。
            // ProcessStation/VerticalTransfer：进站后由站内资源占用，来向边止于 Arrive。
            var holdThroughLeave = nodeZone.Kind is ConveyorPathZoneKind.Pickup
                or ConveyorPathZoneKind.Outfeed;
            seg.OccupancyEndSimTime = holdThroughLeave
                ? nodeZone.LeaveSimTime
                : Math.Max(seg.ExitSimTime, nodeZone.ArriveSimTime);
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
                desiredEntry,
                completedZoneIndex + 1);

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
            var endsAtPassThrough = completedZoneIndex + 1 < chain.Count
                                    && chain[completedZoneIndex + 1].Kind == ConveyorPathZoneKind.EdgeSlot
                                    && chain[completedZoneIndex + 1].PathEdgeIndex != completed.PathEdgeIndex;
            if (_recordPlayback
                && !endsAtJunction
                && !endsAtPickup
                && !endsAtOutfeed
                && !endsAtProcess
                && !endsAtVerticalTransfer
                && !endsAtPassThrough)
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

        /// <summary>
        /// 出口槽的下一格是路口/取货点时，完成事件略早于预约结束。
        /// 这样能先按真实路口时刻延长出口槽，再让同一时刻想抢入的后车看到新的占用。
        /// 预约和时刻表仍用原来的离开时刻，不把时间整体提前。
        /// </summary>
        private double ResolveZoneCompleteEventTime(
            IReadOnlyList<ConveyorPathZone> chain,
            int zoneIndex,
            double leaveSimTime)
        {
            const double HandoffLeadSeconds = 1e-4;
            if (chain == null
                || zoneIndex < 0
                || zoneIndex + 1 >= chain.Count
                || chain[zoneIndex].Kind != ConveyorPathZoneKind.EdgeSlot)
            {
                return leaveSimTime;
            }

            var nextKind = chain[zoneIndex + 1].Kind;
            if (nextKind != ConveyorPathZoneKind.Junction
                && nextKind != ConveyorPathZoneKind.Pickup
                && nextKind != ConveyorPathZoneKind.Outfeed
                && nextKind != ConveyorPathZoneKind.ProcessStation
                && nextKind != ConveyorPathZoneKind.VerticalTransfer)
            {
                return leaveSimTime;
            }

            var early = leaveSimTime - HandoffLeadSeconds;
            return early > _clock.Now + 1e-9 ? early : leaveSimTime;
        }

        private double ResolveZoneCompleteDesiredStart(WarehouseJob job, int completedZoneIndex)
        {
            var desired = _clock.Now;
            var chain = job?.ConveyorPathZones;
            if (chain == null || completedZoneIndex < 0 || completedZoneIndex >= chain.Count)
            {
                return desired;
            }

            var logicalLeave = chain[completedZoneIndex].LeaveSimTime;
            return logicalLeave > desired ? logicalLeave : desired;
        }

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

            if (!TryScheduleConveyorZone(job, ResolveZoneCompleteDesiredStart(job, completedZoneIndex), out _, out _))
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
                if (!ConveyorPathPlanner.HasOpenPickupRouteFromAnyInfeed(
                        _conveyorTopology,
                        _bindings,
                        _pickupReservationCounts,
                        ConveyorRouteAccess.FromJob(job)))
                {
                    return;
                }
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
