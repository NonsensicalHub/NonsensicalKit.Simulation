using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>
    /// 输送资源占用结束后的异步唤醒。占用结束排 <see cref="SimEventType.OccupancyReleased"/>，
    /// 再排已有的 <see cref="SimEventType.ConveyorRouteRetry"/>。
    /// </summary>
    internal sealed class OccupancyWakeProcess : ISimProcess
    {
        private const string ConveyorRoutePlanWaitResource = "conveyor-route-plan";

        private readonly SimRunContext _ctx;
        private ReservationOccupancyNotifier _notifier;
        private readonly ResourceWaitQueueRegistry _waitQueues = new();
        private readonly Dictionary<int, string> _releaseResourcesBySeq = new();
        private readonly HashSet<int> _scheduledRetryJobs = new();
        private readonly Dictionary<int, int> _retryTokenByJob = new();
        private readonly Dictionary<int, double> _retryAtByJob = new();
        private int _nextRetryToken = 1;
        private readonly HashSet<int> _inboundAwaiting = new();
        private readonly HashSet<int> _outboundAwaiting = new();
        private int _releaseSeq;

        public OccupancyWakeProcess(SimRunContext ctx) => _ctx = ctx;

        public bool Handles(SimEventType type) => type == SimEventType.OccupancyReleased;

        public void OnEvent(ScheduledSimEvent evt) => OnReleased(evt.Payload);

        internal void Init()
        {
            var delay = _ctx.Bindings != null ? _ctx.Bindings.OccupancyNotifyDelaySeconds : 0.1f;
            _notifier = new ReservationOccupancyNotifier(delay);
            _notifier.SetScheduleReleaseCallback(ScheduleReleaseEvent);
            _releaseResourcesBySeq.Clear();
            _releaseSeq = 0;
            _scheduledRetryJobs.Clear();
            _retryTokenByJob.Clear();
            _retryAtByJob.Clear();
            _nextRetryToken = 1;
            _inboundAwaiting.Clear();
            _outboundAwaiting.Clear();
            _waitQueues.Clear();
            _ctx.Reservations.OccupancyChanged -= OnReservationOccupancyChanged;
            _ctx.Reservations.OccupancyChanged += OnReservationOccupancyChanged;
        }

        internal bool TryAcceptRetry(int jobId, int token)
        {
            if (!_retryTokenByJob.TryGetValue(jobId, out var expected) || expected != token)
            {
                return false;
            }

            _retryTokenByJob.Remove(jobId);
            _retryAtByJob.Remove(jobId);
            return true;
        }

        internal void OnRetryDispatched(int jobId) => _scheduledRetryJobs.Remove(jobId);

        internal void ScheduleRetry(WarehouseJob job, double when = -1, bool postpone = false)
        {
            if (job == null)
            {
                return;
            }

            var delay = _ctx.Bindings != null ? _ctx.Bindings.OccupancyNotifyDelaySeconds : 0.1f;
            var retryAt = when >= 0 ? Math.Max(_ctx.Clock.Now, when) : _ctx.Clock.Now + delay;
            if (_retryAtByJob.TryGetValue(job.JobId, out var pendingAt))
            {
                var alreadyCovers = postpone
                    ? pendingAt >= retryAt - 1e-9
                    : pendingAt <= retryAt + 1e-9;
                if (alreadyCovers)
                {
                    return;
                }
            }

            var token = _nextRetryToken++;
            if (_nextRetryToken <= 0)
            {
                _nextRetryToken = 1;
            }

            _retryTokenByJob[job.JobId] = token;
            _retryAtByJob[job.JobId] = retryAt;
            _scheduledRetryJobs.Add(job.JobId);
            _ctx.Queue.Enqueue(new ScheduledSimEvent(retryAt, SimEventType.ConveyorRouteRetry, job.JobId, token));
        }

        internal void MarkPending(WarehouseJob job, params string[] blockingResources)
        {
            if (job == null)
            {
                return;
            }

            var enqueued = false;
            if (blockingResources != null)
            {
                for (var i = 0; i < blockingResources.Length; i++)
                {
                    var resourceId = blockingResources[i];
                    if (string.IsNullOrEmpty(resourceId))
                    {
                        continue;
                    }

                    _waitQueues.Enqueue(resourceId, job.JobId);
                    enqueued = true;
                }
            }

            if (!enqueued)
            {
                _waitQueues.Enqueue(ConveyorRoutePlanWaitResource, job.JobId);
            }

            TrackPending(job);
        }

        internal void ClearPending(WarehouseJob job)
        {
            if (job == null)
            {
                return;
            }

            _waitQueues.RemoveJob(job.JobId);
            _scheduledRetryJobs.Remove(job.JobId);
            _retryTokenByJob.Remove(job.JobId);
            _retryAtByJob.Remove(job.JobId);
            _inboundAwaiting.Remove(job.JobId);
            _outboundAwaiting.Remove(job.JobId);
        }

        internal void RetryInboundWaiters()
        {
            if (_inboundAwaiting.Count == 0)
            {
                return;
            }

            var pending = new int[_inboundAwaiting.Count];
            _inboundAwaiting.CopyTo(pending);
            for (var i = 0; i < pending.Length; i++)
            {
                var jobId = pending[i];
                if (!_ctx.Jobs.TryGetValue(jobId, out var job) || job == null)
                {
                    _inboundAwaiting.Remove(jobId);
                    continue;
                }

                if (job.State != WarehouseJobState.WaitingInfeed
                    || job.InfeedPortIndex < 0
                    || ConveyorProcess.IsTransitCommitted(job)
                    || _ctx.Clock.Now + 1e-9 < job.ScheduledCompleteTime)
                {
                    _inboundAwaiting.Remove(jobId);
                    continue;
                }

                _ctx.TryBeginConveyor(job);
                if (!ConveyorPathPlanner.HasOpenPickupRouteFromAnyInfeed(
                        _ctx.Topology,
                        _ctx.Bindings,
                        _ctx.PickupReservationCounts,
                        ConveyorRouteAccess.FromJob(job)))
                {
                    return;
                }
            }
        }

        internal void RetryOutboundWaiters()
        {
            if (_outboundAwaiting.Count == 0)
            {
                return;
            }

            var pending = new int[_outboundAwaiting.Count];
            _outboundAwaiting.CopyTo(pending);
            for (var i = 0; i < pending.Length; i++)
            {
                var jobId = pending[i];
                if (!_ctx.Jobs.TryGetValue(jobId, out var job) || job == null)
                {
                    _outboundAwaiting.Remove(jobId);
                    continue;
                }

                if (job.State is WarehouseJobState.FailedNoCargo or WarehouseJobState.FailedNoSlot
                        or WarehouseJobState.Completed
                    || job.PickupPointIndex < 0
                    || job.PickupCompleteSimTime <= 0)
                {
                    _outboundAwaiting.Remove(jobId);
                    continue;
                }

                _ctx.TryBeginConveyor(job);
            }
        }

        private void OnReservationOccupancyChanged(string resourceId, double start, double end, bool released)
        {
            if (released || _notifier == null)
            {
                return;
            }

            _notifier.OnReservationCommitted(resourceId, start, end);
        }

        private void ScheduleReleaseEvent(double releaseTime, string resourceId)
        {
            var seq = ++_releaseSeq;
            _releaseResourcesBySeq[seq] = resourceId;
            _ctx.Queue.Enqueue(new ScheduledSimEvent(releaseTime, SimEventType.OccupancyReleased, 0, seq));
        }

        private void OnReleased(int releaseSeq)
        {
            if (!_releaseResourcesBySeq.TryGetValue(releaseSeq, out var resourceId))
            {
                return;
            }

            _releaseResourcesBySeq.Remove(releaseSeq);
            if (!_waitQueues.TryDequeueNext(resourceId, out var jobId)
                || !_ctx.Jobs.TryGetValue(jobId, out var waiting)
                || waiting == null)
            {
                return;
            }

            var delay = _ctx.Bindings != null ? _ctx.Bindings.OccupancyNotifyDelaySeconds : 0.1f;
            ScheduleRetry(waiting, _ctx.Clock.Now + delay);
        }

        private void TrackPending(WarehouseJob job)
        {
            if (job.Direction == SimFlowDirection.Outbound)
            {
                if (job.PickupPointIndex >= 0 && job.PickupCompleteSimTime > 0)
                {
                    _outboundAwaiting.Add(job.JobId);
                }

                return;
            }

            if (job.State == WarehouseJobState.WaitingInfeed
                && !ConveyorProcess.IsTransitCommitted(job)
                && _ctx.Clock.Now + 1e-9 >= job.ScheduledCompleteTime)
            {
                _inboundAwaiting.Add(job.JobId);
            }
        }
    }
}
