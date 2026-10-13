using System;
using System.Collections.Generic;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>
    /// 资源时空占用表：每个 <c>resourceId</c> 对应一组按开始时间排序的半开区间 [start, end)。
    /// </summary>
    /// <remarks>
    /// <para>同一资源在时间上不可重叠；新预约若与已有区间冲突，则将开始时刻顺延到冲突区间结束之后（FIFO 语义）。</para>
    /// <para>已结束的区间必须保留。查询常用未来时刻做起点，若据此删掉更早的占用，后续预约仍可能从更早的时刻开始，从而叠进已被删掉的窗口。</para>
    /// <para>输送路段槽位、路口停留点、取货点、堆垛机、巷道列等均映射为不同的 resourceId 字符串。</para>
    /// <para>新预约提交时触发 <see cref="OccupancyChanged"/>（仅 committed，不含 Release），供
    /// <see cref="ReservationOccupancyNotifier"/> 在区间结束时调度唤醒事件。</para>
    /// </remarks>
    public sealed class ReservationTable
    {
        private const double TimeEpsilon = 1e-6;
        private readonly Dictionary<string, List<TimeInterval>> _intervals = new();
        private double _historyFloor = double.NegativeInfinity;
        private List<TimeInterval>[] _scanLists = Array.Empty<List<TimeInterval>>();
        private int[] _scanCursors = Array.Empty<int>();

        /// <summary>参数：resourceId, start, end, released（true=释放，false=新预约）。</summary>
        public event Action<string, double, double, bool> OccupancyChanged;

        public void Clear()
        {
            _intervals.Clear();
            _historyFloor = double.NegativeInfinity;
        }

        /// <summary>
        /// 仿真时钟推进后调用。早于该时刻结束的占用会被丢掉，避免每条资源的历史区间随任务数一直变长。
        /// </summary>
        public void NoteHistoryFloor(double time)
        {
            if (time > _historyFloor)
            {
                _historyFloor = time;
            }
        }

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

            TrimExpired(list);

            var candidate = start;
            var shiftedByConflict = false;
            for (var i = FindFirstIndexEndingAfter(list, candidate); i < list.Count; i++)
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

            TrimExpired(list);
            if (list.Count == 0)
            {
                return fromTime;
            }

            var t = fromTime;
            for (var i = FindFirstIndexEndingAfter(list, t); i < list.Count; i++)
            {
                var iv = list[i];
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

            TrimExpired(list);
            if (list.Count == 0)
            {
                return fromTime;
            }

            var candidate = fromTime;
            for (var i = FindFirstIndexEndingAfter(list, candidate); i < list.Count; i++)
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

        /// <summary>
        /// 区间按开始时刻排序且互不重叠，结束时刻同样有序。
        /// 定位第一个尚未在 <paramref name="time"/> 之前结束的区间。
        /// </summary>
        private static int FindFirstIndexEndingAfter(List<TimeInterval> list, double time)
        {
            var lo = 0;
            var hi = list.Count;
            while (lo < hi)
            {
                var mid = lo + (hi - lo) / 2;
                if (list[mid].End <= time + TimeEpsilon)
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

            var i = FindFirstIndexEndingAfter(list, atTime);
            if (i >= list.Count)
            {
                return 0;
            }

            var iv = list[i];
            return iv.Start <= atTime + TimeEpsilon && iv.End > atTime + TimeEpsilon ? 1 : 0;
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
        /// 每个资源的游标只向前走，多次推进合计与区间数成正比，避免每次都从历史占用的开头重扫。
        /// </remarks>
        public double QueryEarliestStartAll(double desiredStart, double duration, params string[] resourceIds)
        {
            if (resourceIds == null || resourceIds.Length == 0 || duration <= 0)
            {
                return desiredStart;
            }

            EnsureScanBuffers(resourceIds.Length);
            var guardMax = 1;
            for (var i = 0; i < resourceIds.Length; i++)
            {
                _scanLists[i] = null;
                _scanCursors[i] = 0;
                if (_intervals.TryGetValue(resourceIds[i], out var list) && list.Count > 0)
                {
                    TrimExpired(list);
                    _scanLists[i] = list;
                    _scanCursors[i] = FindFirstIndexEndingAfter(list, desiredStart);
                    guardMax += list.Count;
                }
            }

            var start = desiredStart;
            for (var iter = 0; iter < guardMax; iter++)
            {
                var merged = start;
                for (var i = 0; i < resourceIds.Length; i++)
                {
                    var list = _scanLists[i];
                    if (list == null)
                    {
                        continue;
                    }

                    var freeAt = EarliestFreeFromCursor(list, ref _scanCursors[i], start, duration);
                    if (freeAt > merged)
                    {
                        merged = freeAt;
                    }
                }

                if (merged <= start + TimeEpsilon)
                {
                    return start;
                }

                start = merged;
            }

            return start;
        }

        /// <summary>
        /// 从游标继续向后找可容纳 <paramref name="duration"/> 的起点。游标只前进，多次调用合计线性于区间数。
        /// </summary>
        private static double EarliestFreeFromCursor(
            List<TimeInterval> list,
            ref int cursor,
            double fromTime,
            double duration)
        {
            var candidate = fromTime;
            while (cursor < list.Count && list[cursor].End <= candidate + TimeEpsilon)
            {
                cursor++;
            }

            var i = cursor;
            while (i < list.Count)
            {
                var iv = list[i];
                if (iv.End <= candidate + TimeEpsilon)
                {
                    i++;
                    continue;
                }

                if (iv.Start >= candidate + duration - TimeEpsilon)
                {
                    cursor = i;
                    return candidate;
                }

                candidate = iv.End;
                i++;
            }

            cursor = list.Count;
            return candidate;
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

        public bool Release(string resourceId, double start, double end)
        {
            if (!_intervals.TryGetValue(resourceId, out var list))
            {
                return false;
            }

            TrimExpired(list);
            var index = FindReleaseIndex(list, start, end);
            if (index < 0)
            {
                return false;
            }

            var iv = list[index];
            list.RemoveAt(index);
            NotifyOccupancyChanged(resourceId, iv.Start, iv.End, released: true);
            return true;
        }

        /// <summary>已结束且不会再被改写的前缀删掉。仍覆盖当前时刻的区间保留。</summary>
        private void TrimExpired(List<TimeInterval> list)
        {
            if (list == null || list.Count < 64 || _historyFloor <= double.NegativeInfinity)
            {
                return;
            }

            var live = FindFirstIndexEndingAfter(list, _historyFloor);
            if (live < 32)
            {
                return;
            }

            list.RemoveRange(0, live);
        }

        private void EnsureScanBuffers(int count)
        {
            if (_scanCursors.Length >= count)
            {
                return;
            }

            var size = Math.Max(4, count);
            _scanLists = new List<TimeInterval>[size];
            _scanCursors = new int[size];
        }

        private static int FindReleaseIndex(List<TimeInterval> list, double start, double end)
        {
            var lo = 0;
            var hi = list.Count;
            while (lo < hi)
            {
                var mid = lo + (hi - lo) / 2;
                if (list[mid].Start < start - TimeEpsilon)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            for (var i = lo; i < list.Count && list[i].Start <= start + TimeEpsilon; i++)
            {
                var iv = list[i];
                if (Math.Abs(iv.Start - start) < TimeEpsilon && Math.Abs(iv.End - end) < TimeEpsilon)
                {
                    return i;
                }
            }

            return -1;
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
