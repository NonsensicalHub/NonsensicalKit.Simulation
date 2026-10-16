using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <content>
    /// 输送阶段：路径择优（<see cref="ConveyorPathPlanner"/>）→ 整条 zone 链交给 ConveyorZoneKernel 调度
    /// （见 StackerWarehouseSimulator.PrimaryKernel.cs）→ 终点（入库取货点 / 出库口）。
    /// 选路失败的任务进入等待队列，取货点 / 出库口释放后重试。旧的 zone 逐步预约（ConveyorTransitScheduler）已删除。
    /// </content>
    public sealed partial class StackerWarehouseSimulator
    {
        #region 路径选定

        /// <summary>为任务选定输送路径（首次）并整条提交给输送内核；已提交则直接成功。</summary>
        private bool TryScheduleConveyorOnMap(WarehouseJob job, out double endTime, out double infeedPhysicalReleaseTime)
        {
            endTime = _clock.Now;
            infeedPhysicalReleaseTime = _clock.Now;
            if (_primaryKernel == null)
            {
                WarehouseSimLog.Error($"[ConveyorKernel] job={job.JobId} 输送内核未初始化（ConveyorMap 缺失？），无法调度输送。");
                return false;
            }

            return TrySubmitToPrimaryKernel(job);
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

        #region 取货点与路径重试

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
