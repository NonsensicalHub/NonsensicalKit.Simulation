using System;
using System.Collections.Generic;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>
    /// 资源时空占用表：每个 <c>resourceId</c> 对应一组按开始时间排序的半开区间 [start, end)。
    /// </summary>
    /// <remarks>
    /// <para>同一资源在时间上不可重叠；新预约若与已有区间冲突，则将开始时刻顺延到冲突区间结束之后（FIFO 语义）。</para>
    /// <para>输送路段槽位、路口停留点、取货点、堆垛机、巷道列等均映射为不同的 resourceId 字符串。</para>
    /// <para>新预约提交时触发 <see cref="OccupancyChanged"/>（仅 committed，不含 Release），供
    /// <see cref="ReservationOccupancyNotifier"/> 在区间结束时调度唤醒事件。</para>
    /// </remarks>
    public sealed class ReservationTable
    {
        private const double TimeEpsilon = 1e-6;
        private readonly Dictionary<string, List<TimeInterval>> _intervals = new();

        /// <summary>参数：resourceId, start, end, released（true=释放，false=新预约）。</summary>
        public event Action<string, double, double, bool> OccupancyChanged;

        public void Clear() => _intervals.Clear();

        /// <summary>在指定资源上预约 [start, end)；若冲突则顺延到前一预约结束之后（FIFO）。</summary>
        public bool TryReserve(string resourceId, double start, double end, out double conflictFreeStart)
        {
            conflictFreeStart = start;
            var duration = end - start;
            if (duration <= 0)
            {
                return true;
            }

            if (!_intervals.TryGetValue(resourceId, out var list))
            {
                list = new List<TimeInterval>();
                _intervals[resourceId] = list;
            }

            PruneEndedBefore(list, start);

            var candidate = start;
            var shiftedByConflict = false;
            for (var i = 0; i < list.Count; i++)
            {
                var iv = list[i];
                if (iv.End <= candidate + TimeEpsilon)
                {
                    continue;
                }

                var candidateEnd = candidate + duration;
                if (iv.Start >= candidateEnd - TimeEpsilon)
                {
                    break;
                }

                // 与当前区间重叠，顺延到该区间尾端继续寻找可放置空档。
                candidate = iv.End;
                shiftedByConflict = true;
            }

            conflictFreeStart = candidate;
            var reserved = new TimeInterval(candidate, candidate + duration);
            InsertSorted(list, reserved);
            NotifyOccupancyChanged(resourceId, reserved.Start, reserved.End, released: false);
            return !shiftedByConflict && Math.Abs(conflictFreeStart - start) < TimeEpsilon;
        }

        /// <summary>查询资源在 fromTime 之后最早可开始时刻（不写入预约）。</summary>
        public double GetEarliestFreeTime(string resourceId, double fromTime)
        {
            if (!_intervals.TryGetValue(resourceId, out var list) || list.Count == 0)
            {
                return fromTime;
            }

            PruneEndedBefore(list, fromTime);

            var t = fromTime;
            foreach (var iv in list)
            {
                if (iv.End <= t + TimeEpsilon)
                {
                    continue;
                }

                if (iv.Start > t + TimeEpsilon)
                {
                    return t;
                }

                t = iv.End;
            }

            return t;
        }

        /// <summary>
        /// 查询资源在 fromTime 之后最早可开始且可持续 duration 的时刻（不写入预约）。
        /// 与 <see cref="TryReserve"/> 的冲突判定保持一致，避免“起点可用但区间中途冲突”。
        /// </summary>
        public double GetEarliestFreeTimeForDuration(string resourceId, double fromTime, double duration)
        {
            if (duration <= 0)
            {
                return fromTime;
            }

            if (!_intervals.TryGetValue(resourceId, out var list) || list.Count == 0)
            {
                return fromTime;
            }

            PruneEndedBefore(list, fromTime);

            var candidate = fromTime;
            for (var i = 0; i < list.Count; i++)
            {
                var iv = list[i];
                if (iv.End <= candidate + TimeEpsilon)
                {
                    continue;
                }

                var candidateEnd = candidate + duration;
                if (iv.Start >= candidateEnd - TimeEpsilon)
                {
                    return candidate;
                }

                candidate = iv.End;
            }

            return candidate;
        }

        /// <summary>移除已结束且不会再影响后续查询的历史区间，避免长仿真中列表无限增长。</summary>
        private static void PruneEndedBefore(List<TimeInterval> list, double beforeTime)
        {
            var removeCount = 0;
            while (removeCount < list.Count && list[removeCount].End <= beforeTime + TimeEpsilon)
            {
                removeCount++;
            }

            if (removeCount > 0)
            {
                list.RemoveRange(0, removeCount);
            }
        }

        private static void InsertSorted(List<TimeInterval> list, TimeInterval interval)
        {
            var insertAt = FindInsertIndex(list, interval.Start);
            list.Insert(insertAt, interval);
        }

        private static int FindInsertIndex(List<TimeInterval> list, double start)
        {
            var lo = 0;
            var hi = list.Count;
            while (lo < hi)
            {
                var mid = lo + (hi - lo) / 2;
                if (list[mid].Start <= start + TimeEpsilon)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            return lo;
        }

        /// <summary>统计在时刻 atTime 仍占用资源的预约数量。</summary>
        public int CountActiveAt(string resourceId, double atTime)
        {
            if (!_intervals.TryGetValue(resourceId, out var list) || list.Count == 0)
            {
                return 0;
            }

            var count = 0;
            foreach (var iv in list)
            {
                if (iv.Start <= atTime + TimeEpsilon && iv.End > atTime + TimeEpsilon)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>统计一组资源在时刻 atTime 的占用总数。</summary>
        public int CountActiveAmong(string[] resourceIds, double atTime)
        {
            if (resourceIds == null || resourceIds.Length == 0)
            {
                return 0;
            }

            var total = 0;
            foreach (var id in resourceIds)
            {
                total += CountActiveAt(id, atTime);
            }

            return total;
        }

        /// <summary>查询一组资源在 atTime 时刻仍占用中的最早结束时刻。</summary>
        public double GetEarliestActiveReleaseAmong(string[] resourceIds, double atTime)
        {
            if (resourceIds == null || resourceIds.Length == 0)
            {
                return atTime;
            }

            var earliest = double.MaxValue;
            var found = false;
            foreach (var id in resourceIds)
            {
                if (!_intervals.TryGetValue(id, out var list))
                {
                    continue;
                }

                foreach (var iv in list)
                {
                    if (iv.Start <= atTime + TimeEpsilon && iv.End > atTime + TimeEpsilon)
                    {
                        if (iv.End < earliest)
                        {
                            earliest = iv.End;
                            found = true;
                        }
                    }
                }
            }

            return found ? earliest : atTime;
        }

        /// <summary>查询一组资源在 atTime 时刻仍占用中的最晚结束时刻。</summary>
        public double GetLatestActiveEndAmong(string[] resourceIds, double atTime)
        {
            if (resourceIds == null || resourceIds.Length == 0)
            {
                return atTime;
            }

            var latest = atTime;
            foreach (var id in resourceIds)
            {
                if (!_intervals.TryGetValue(id, out var list))
                {
                    continue;
                }

                foreach (var iv in list)
                {
                    if (iv.Start <= atTime + TimeEpsilon && iv.End > atTime + TimeEpsilon && iv.End > latest)
                    {
                        latest = iv.End;
                    }
                }
            }

            return latest;
        }

        /// <summary>在多个同类资源（如路段槽位）中选最早可开始的，并占用其中一个。</summary>
        public double ReserveEarliestAmong(double desiredStart, double duration, string[] resourceIds)
        {
            if (resourceIds == null || resourceIds.Length == 0)
            {
                return desiredStart;
            }

            if (resourceIds.Length == 1)
            {
                return ReserveAtEarliestAll(desiredStart, duration, resourceIds);
            }

            var bestStart = double.MaxValue;
            var bestId = resourceIds[0];
            foreach (var id in resourceIds)
            {
                var candidate = GetEarliestFreeTimeForDuration(id, desiredStart, duration);
                if (candidate < bestStart)
                {
                    bestStart = candidate;
                    bestId = id;
                }
            }

            if (bestStart == double.MaxValue)
            {
                bestStart = desiredStart;
            }

            TryReserve(bestId, bestStart, bestStart + duration, out _);
            return bestStart;
        }

        /// <summary>查询若干互斥资源在 desiredStart 之后最早可同时开始的一段时长（不写入预约）。</summary>
        /// <remarks>
        /// 每次迭代至少会将候选起点推进到某一资源占用区间的结束时刻，
        /// 因此最多迭代 <c>resourceIds.Length × 最长占用链</c> 次即可收敛。
        /// 上限设为 <c>resourceIds.Length * 64 + 1</c> 以覆盖高密度占用场景，同时避免无限循环。
        /// </remarks>
        public double QueryEarliestStartAll(double desiredStart, double duration, params string[] resourceIds)
        {
            if (resourceIds == null || resourceIds.Length == 0 || duration <= 0)
            {
                return desiredStart;
            }

            var start = desiredStart;
            var maxIter = resourceIds.Length * 64 + 1;
            for (var iter = 0; iter < maxIter; iter++)
            {
                var merged = start;
                foreach (var id in resourceIds)
                {
                    merged = Math.Max(merged, GetEarliestFreeTimeForDuration(id, start, duration));
                }

                if (Math.Abs(merged - start) < TimeEpsilon)
                {
                    return start;
                }

                start = merged;
            }

            return start;
        }

        /// <summary>在若干互斥资源上同时预约一段时长，返回实际开始时刻（可能晚于 desiredStart）。</summary>
        public double ReserveAtEarliestAll(double desiredStart, double duration, params string[] resourceIds)
        {
            if (resourceIds == null || resourceIds.Length == 0)
            {
                return desiredStart;
            }

            if (duration <= 0)
            {
                return desiredStart;
            }

            var start = QueryEarliestStartAll(desiredStart, duration, resourceIds);
            foreach (var id in resourceIds)
            {
                TryReserve(id, start, start + duration, out _);
            }

            return start;
        }

        /// <summary>查询在 desiredStart 时刻阻碍预约的、结束最晚的资源 ID（用于瓶颈诊断）。</summary>
        public string GetLatestBlockingResourceId(double desiredStart, params string[] resourceIds)
        {
            if (resourceIds == null || resourceIds.Length == 0)
            {
                return null;
            }

            var latestEnd = desiredStart;
            string latestId = null;
            foreach (var id in resourceIds)
            {
                if (!_intervals.TryGetValue(id, out var list))
                {
                    continue;
                }

                foreach (var iv in list)
                {
                    if (iv.Start <= desiredStart + TimeEpsilon && iv.End > latestEnd + TimeEpsilon)
                    {
                        latestEnd = iv.End;
                        latestId = id;
                    }
                }
            }

            return latestId;
        }

        public void Release(string resourceId, double start, double end)
        {
            if (!_intervals.TryGetValue(resourceId, out var list))
            {
                return;
            }

            for (var i = list.Count - 1; i >= 0; i--)
            {
                var iv = list[i];
                if (Math.Abs(iv.Start - start) < TimeEpsilon && Math.Abs(iv.End - end) < TimeEpsilon)
                {
                    list.RemoveAt(i);
                    NotifyOccupancyChanged(resourceId, iv.Start, iv.End, released: true);
                    return;
                }
            }
        }

        private void NotifyOccupancyChanged(string resourceId, double start, double end, bool released)
        {
            if (released)
            {
                return;
            }

            OccupancyChanged?.Invoke(resourceId, start, end, released);
        }

        private readonly struct TimeInterval
        {
            public readonly double Start;
            public readonly double End;

            public TimeInterval(double start, double end)
            {
                Start = start;
                End = end;
            }
        }
    }
}
