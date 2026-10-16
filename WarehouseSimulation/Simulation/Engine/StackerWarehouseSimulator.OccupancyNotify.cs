using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <content>
    /// 输送资源占用结束后的异步唤醒：避免在占用尚未释放时反复寻路失败。
    /// <para>流程：预约表提交占用 → 占用结束时刻 + delay → <see cref="SimEventType.OccupancyReleased"/> →
    /// 从该资源 FIFO 队列取出队首 Job → <see cref="SimEventType.ConveyorRouteRetry"/> 重试 <c>TryBeginConveyor</c>。
    /// 等取货点容量的任务不走这条路径，改由取货点释放时批量唤醒。</para>
    /// </content>
    public sealed partial class StackerWarehouseSimulator
    {
        /// <summary>无具体阻塞资源时，任务进入此虚拟队列等待任意输送相关释放。</summary>
        private const string ConveyorRoutePlanWaitResource = "conveyor-route-plan";

        private ReservationOccupancyNotifier _occupancyNotifier;
        private readonly ResourceWaitQueueRegistry _resourceWaitQueues = new();
        private readonly Dictionary<int, string> _occupancyReleaseResourcesBySeq = new();
        private readonly HashSet<int> _scheduledConveyorRouteRetryJobs = new();
        private readonly Dictionary<int, int> _conveyorRetryTokenByJob = new();
        private readonly Dictionary<int, double> _conveyorRetryAtByJob = new();
        private int _nextConveyorRetryToken = 1;
        private readonly HashSet<int> _inboundAwaitingConveyorRoute = new();
        private readonly HashSet<int> _outboundAwaitingConveyorRoute = new();
        private int _occupancyReleaseSeq;

        private void InitOccupancyNotifier()
        {
            var delay = _bindings != null ? _bindings.OccupancyNotifyDelaySeconds : 0.1f;
            _occupancyNotifier = new ReservationOccupancyNotifier(delay);
            _occupancyNotifier.SetScheduleReleaseCallback(ScheduleOccupancyReleaseEvent);
            _occupancyReleaseResourcesBySeq.Clear();
            _occupancyReleaseSeq = 0;
            _scheduledConveyorRouteRetryJobs.Clear();
            _conveyorRetryTokenByJob.Clear();
            _conveyorRetryAtByJob.Clear();
            _nextConveyorRetryToken = 1;
            _inboundAwaitingConveyorRoute.Clear();
            _outboundAwaitingConveyorRoute.Clear();
            _resourceWaitQueues.Clear();
            _reservations.OccupancyChanged -= OnReservationOccupancyChanged;
            _reservations.OccupancyChanged += OnReservationOccupancyChanged;
        }

        private void OnReservationOccupancyChanged(string resourceId, double start, double end, bool released)
        {
            if (released || _occupancyNotifier == null)
            {
                return;
            }

            _occupancyNotifier.OnReservationCommitted(resourceId, start, end);
        }

        private void ScheduleOccupancyReleaseEvent(double releaseTime, string resourceId)
        {
            var seq = ++_occupancyReleaseSeq;
            _occupancyReleaseResourcesBySeq[seq] = resourceId;
            _queue.Enqueue(new ScheduledSimEvent(releaseTime, SimEventType.OccupancyReleased, 0, seq));
        }

        /// <summary>资源占用结束：仅唤醒该资源 FIFO 队首，并在 delay 后触发路径重试。</summary>
        /// <remarks>
        /// 不回落到 <see cref="ConveyorRoutePlanWaitResource"/>。
        /// 等取货点/出库口容量的任务由 <c>NotifyConveyorRouteWaiters</c> 唤醒；
        /// 否则每条预约释放都会重跑寻路，1000 入库会把重试数放大到数万次。
        /// </remarks>
        private void OnOccupancyReleased(int releaseSeq)
        {
            if (!_occupancyReleaseResourcesBySeq.TryGetValue(releaseSeq, out var resourceId))
            {
                return;
            }

            _occupancyReleaseResourcesBySeq.Remove(releaseSeq);
            if (!TryGrantNextOccupancyWaiter(resourceId, out var jobId)
                || !_jobs.TryGetValue(jobId, out var waiting)
                || waiting == null)
            {
                return;
            }

            var delay = _bindings != null ? _bindings.OccupancyNotifyDelaySeconds : 0.1f;
            ScheduleConveyorRouteRetry(waiting, _clock.Now + delay);
        }

        private bool TryAcceptConveyorRouteRetry(int jobId, int token)
        {
            if (!_conveyorRetryTokenByJob.TryGetValue(jobId, out var expected) || expected != token)
            {
                return false;
            }

            _conveyorRetryTokenByJob.Remove(jobId);
            _conveyorRetryAtByJob.Remove(jobId);
            return true;
        }

        private bool TryGrantNextOccupancyWaiter(string resourceId, out int jobId) =>
            _resourceWaitQueues.TryDequeueNext(resourceId, out jobId);

        /// <summary>
        /// 排定输送路径重试。常规唤醒仅由 <see cref="OnOccupancyReleased"/> 触发；
        /// 路口重排等已知未来时刻的场景可传入 <paramref name="when"/>。
        /// <paramref name="postpone"/> 为真时，已排定的更早重试改到这次时刻之后，避免占用延长后仍按旧时刻抢回。
        /// </summary>
        private void ScheduleConveyorRouteRetry(WarehouseJob job, double when = -1, bool postpone = false)
        {
            if (job == null)
            {
                return;
            }

            var delay = _bindings != null ? _bindings.OccupancyNotifyDelaySeconds : 0.1f;
            var retryAt = when >= 0 ? Math.Max(_clock.Now, when) : _clock.Now + delay;
            if (_conveyorRetryAtByJob.TryGetValue(job.JobId, out var pendingAt))
            {
                var alreadyCovers = postpone
                    ? pendingAt >= retryAt - 1e-9
                    : pendingAt <= retryAt + 1e-9;
                if (alreadyCovers)
                {
                    return;
                }
            }

            var token = _nextConveyorRetryToken++;
            if (_nextConveyorRetryToken <= 0)
            {
                _nextConveyorRetryToken = 1;
            }

            _conveyorRetryTokenByJob[job.JobId] = token;
            _conveyorRetryAtByJob[job.JobId] = retryAt;
            _scheduledConveyorRouteRetryJobs.Add(job.JobId);
            _queue.Enqueue(new ScheduledSimEvent(retryAt, SimEventType.ConveyorRouteRetry, job.JobId, token));
        }

        private void OnConveyorRouteRetryDispatched(int jobId) =>
            _scheduledConveyorRouteRetryJobs.Remove(jobId);

        /// <summary>
        /// 输送 zone 预约失败：将任务挂入阻塞资源的 FIFO 等待队列。
        /// </summary>
        /// <param name="blockingResources">已知阻塞资源 ID 列表；为空则进入通用 <c>conveyor-route-plan</c> 队列。</param>
        private void MarkConveyorRoutePending(WarehouseJob job, params string[] blockingResources)
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

                    _resourceWaitQueues.Enqueue(resourceId, job.JobId);
                    enqueued = true;
                }
            }

            if (!enqueued)
            {
                _resourceWaitQueues.Enqueue(ConveyorRoutePlanWaitResource, job.JobId);
            }

            TrackConveyorRoutePending(job);
        }

        private void TrackConveyorRoutePending(WarehouseJob job)
        {
            if (job == null)
            {
                return;
            }

            if (job.Direction == SimFlowDirection.Outbound)
            {
                if (job.PickupPointIndex >= 0 && job.PickupCompleteSimTime > 0)
                {
                    _outboundAwaitingConveyorRoute.Add(job.JobId);
                }

                return;
            }

            if (job.State == WarehouseJobState.WaitingInfeed
                && !IsConveyorTransitCommitted(job)
                && _clock.Now + 1e-9 >= job.ScheduledCompleteTime)
            {
                _inboundAwaitingConveyorRoute.Add(job.JobId);
            }
        }

        /// <summary>输送已成功推进：从所有等待队列中移除该任务。</summary>
        private void ClearConveyorRoutePending(WarehouseJob job)
        {
            if (job == null)
            {
                return;
            }

            _resourceWaitQueues.RemoveJob(job.JobId);
            _scheduledConveyorRouteRetryJobs.Remove(job.JobId);
            _conveyorRetryTokenByJob.Remove(job.JobId);
            _conveyorRetryAtByJob.Remove(job.JobId);
            _inboundAwaitingConveyorRoute.Remove(job.JobId);
            _outboundAwaitingConveyorRoute.Remove(job.JobId);
        }
    }
}
