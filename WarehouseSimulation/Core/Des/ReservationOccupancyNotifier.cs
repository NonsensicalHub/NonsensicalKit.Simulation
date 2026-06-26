using System;
using System.Collections.Generic;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>
    /// 预约表占用释放通知桥接器。
    /// </summary>
    /// <remarks>
    /// 当 <see cref="ReservationTable"/> 写入新区间时，在本组件登记「于 end 时刻释放 resourceId」；
    /// 通过回调向仿真器事件队列投递 <c>OccupancyReleased</c>，再由等待队列 FIFO 唤醒队首任务重试输送。
    /// <see cref="StartDelaySeconds"/> 为释放后的额外缓冲，避免在占用刚结束瞬间重复寻路失败。
    /// </remarks>
    public sealed class ReservationOccupancyNotifier
    {
        private readonly double _startDelaySeconds;
        private readonly HashSet<(double endTime, string resourceId)> _scheduledReleases = new();
        private Action<double, string> _scheduleReleaseCallback;

        public ReservationOccupancyNotifier(double startDelaySeconds)
        {
            _startDelaySeconds = Math.Max(0d, startDelaySeconds);
        }

        public double StartDelaySeconds => _startDelaySeconds;

        public void SetScheduleReleaseCallback(Action<double, string> scheduleReleaseCallback) =>
            _scheduleReleaseCallback = scheduleReleaseCallback;

        public void Clear() => _scheduledReleases.Clear();

        public void OnReservationCommitted(string resourceId, double start, double end)
        {
            if (string.IsNullOrEmpty(resourceId) || end <= start + 1e-9)
            {
                return;
            }

            ScheduleRelease(end, resourceId);
        }

        private void ScheduleRelease(double releaseTime, string resourceId)
        {
            if (_scheduleReleaseCallback == null)
            {
                return;
            }

            var key = (releaseTime, resourceId);
            if (!_scheduledReleases.Add(key))
            {
                return;
            }

            _scheduleReleaseCallback(releaseTime, resourceId);
        }
    }
}
