using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>
    /// 路段时刻表的前后段。
    /// 中间段被摘掉再补记后，表尾不一定是行驶方向的下一段，不能用下标相邻判断。
    /// </summary>
    public static class ConveyorSegmentScheduleNavigation
    {
        private const double TimeEpsilon = 1e-6;

        /// <summary>
        /// 从本段终点继续出发、且进入时刻不早于本段离开时刻的最早一段。
        /// </summary>
        public static bool TryGetDownstreamIndex(
            IReadOnlyList<ConveyorSegmentScheduleEntry> schedule,
            int index,
            out int downstreamIndex)
        {
            downstreamIndex = -1;
            if (schedule == null || index < 0 || index >= schedule.Count)
            {
                return false;
            }

            var current = schedule[index];
            // 加工路径会再次经过同一个路口。只认路径上下一条边，不能把下一次折返当成这一次的下游。
            if (current.PathEdgeIndex >= 0)
            {
                var nextEdge = current.PathEdgeIndex + 1;
                for (var j = 0; j < schedule.Count; j++)
                {
                    if (j == index || schedule[j].PathEdgeIndex != nextEdge)
                    {
                        continue;
                    }

                    if (schedule[j].FromNodeIndex != current.ToNodeIndex)
                    {
                        continue;
                    }

                    downstreamIndex = j;
                    return true;
                }

                return false;
            }

            var bestEntry = double.MaxValue;
            var found = false;
            for (var j = 0; j < schedule.Count; j++)
            {
                if (j == index || schedule[j].FromNodeIndex != current.ToNodeIndex)
                {
                    continue;
                }

                var entry = schedule[j].EntrySimTime;
                if (entry + TimeEpsilon < current.ExitSimTime)
                {
                    continue;
                }

                if (found && entry >= bestEntry)
                {
                    continue;
                }

                downstreamIndex = j;
                bestEntry = entry;
                found = true;
            }

            return found;
        }

        /// <summary>驶入本段起点、且进入时刻不晚于本段进入时刻的最近一段。</summary>
        public static bool TryGetUpstreamIndex(
            IReadOnlyList<ConveyorSegmentScheduleEntry> schedule,
            int index,
            out int upstreamIndex)
        {
            upstreamIndex = -1;
            if (schedule == null || index < 0 || index >= schedule.Count)
            {
                return false;
            }

            var current = schedule[index];
            if (current.PathEdgeIndex > 0)
            {
                var prevEdge = current.PathEdgeIndex - 1;
                for (var j = 0; j < schedule.Count; j++)
                {
                    if (j == index || schedule[j].PathEdgeIndex != prevEdge)
                    {
                        continue;
                    }

                    if (schedule[j].ToNodeIndex != current.FromNodeIndex)
                    {
                        continue;
                    }

                    upstreamIndex = j;
                    return true;
                }

                return false;
            }

            var bestEntry = double.MinValue;
            var found = false;
            for (var j = 0; j < schedule.Count; j++)
            {
                if (j == index || schedule[j].ToNodeIndex != current.FromNodeIndex)
                {
                    continue;
                }

                var entry = schedule[j].EntrySimTime;
                if (entry > current.EntrySimTime + TimeEpsilon)
                {
                    continue;
                }

                if (found && entry <= bestEntry)
                {
                    continue;
                }

                upstreamIndex = j;
                bestEntry = entry;
                found = true;
            }

            return found;
        }

        /// <summary>同一起终点的最后一条（补记后的那条）。</summary>
        public static int FindEndpointIndex(
            IReadOnlyList<ConveyorSegmentScheduleEntry> schedule,
            int fromNodeIndex,
            int toNodeIndex)
        {
            if (schedule == null)
            {
                return -1;
            }

            for (var i = schedule.Count - 1; i >= 0; i--)
            {
                if (schedule[i].FromNodeIndex == fromNodeIndex && schedule[i].ToNodeIndex == toNodeIndex)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>路径上某一次经过（<see cref="ConveyorSegmentScheduleEntry.PathEdgeIndex"/>）的时刻记录。</summary>
        public static int FindPathEdgeIndex(
            IReadOnlyList<ConveyorSegmentScheduleEntry> schedule,
            int pathEdgeIndex)
        {
            if (schedule == null || pathEdgeIndex < 0)
            {
                return -1;
            }

            for (var i = schedule.Count - 1; i >= 0; i--)
            {
                if (schedule[i].PathEdgeIndex == pathEdgeIndex)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>按路径节点顺序重排。不在路径上的段保持原相对顺序并排到后面。</summary>
        public static void SortByPath(
            List<ConveyorSegmentScheduleEntry> schedule,
            IReadOnlyList<int> pathNodeIndices)
        {
            if (schedule == null || schedule.Count < 2 || pathNodeIndices == null || pathNodeIndices.Count < 2)
            {
                return;
            }

            var ordinals = new int[schedule.Count];
            for (var i = 0; i < schedule.Count; i++)
            {
                ordinals[i] = schedule[i].PathEdgeIndex >= 0
                    ? schedule[i].PathEdgeIndex
                    : PathOrdinal(pathNodeIndices, schedule[i].FromNodeIndex, schedule[i].ToNodeIndex);
            }

            var indexed = new int[schedule.Count];
            for (var i = 0; i < indexed.Length; i++)
            {
                indexed[i] = i;
            }

            Array.Sort(indexed, (a, b) =>
            {
                var oa = ordinals[a] < 0 ? int.MaxValue : ordinals[a];
                var ob = ordinals[b] < 0 ? int.MaxValue : ordinals[b];
                var compare = oa.CompareTo(ob);
                return compare != 0 ? compare : a.CompareTo(b);
            });

            var sorted = new ConveyorSegmentScheduleEntry[schedule.Count];
            for (var i = 0; i < indexed.Length; i++)
            {
                sorted[i] = schedule[indexed[i]];
            }

            for (var i = 0; i < sorted.Length; i++)
            {
                schedule[i] = sorted[i];
            }
        }

        private static int PathOrdinal(IReadOnlyList<int> path, int from, int to)
        {
            for (var i = 0; i < path.Count - 1; i++)
            {
                if (path[i] == from && path[i + 1] == to)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
