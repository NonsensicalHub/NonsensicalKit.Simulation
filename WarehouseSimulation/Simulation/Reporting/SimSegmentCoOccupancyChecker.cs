using System;
using System.Collections.Generic;
using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>
    /// 用各任务路段时刻表检查同一条物理路段上的在段时间。双向路段算同一条。
    /// 同向从开始驶入算到车头到达下游节点，人数可以达到路段容量，超过才算冲突。
    /// 对向只要车体还占着路段就算冲突：停在下游节点的时间要算上，还要等车头再开出一个车身长度，尾端才离开。
    /// 两条路在同一路口连成直线时，车身靠近路口的时间重叠也算对向冲突。
    /// </summary>
    public static class SimSegmentCoOccupancyChecker
    {
        private const double TimeEpsilon = 1e-6;

        public static List<SimSegmentCoOccupancyRecord> FindOverlaps(
            IReadOnlyList<SimSubTask> subTasks,
            WarehouseConveyorMap map,
            ConveyorMapTopology topology)
        {
            var overlaps = new List<SimSegmentCoOccupancyRecord>();
            if (subTasks == null || subTasks.Count == 0 || map == null || topology == null)
            {
                return overlaps;
            }

            var bestScheduleByJob = SimSubTaskQuery.BuildBestScheduleByJob(subTasks);
            if (bestScheduleByJob == null || bestScheduleByJob.Count == 0)
            {
                return overlaps;
            }

            var grouped = new Dictionary<string, List<Presence>>(bestScheduleByJob.Count * 4);
            foreach (var pair in bestScheduleByJob)
            {
                var schedule = pair.Value;
                if (schedule == null)
                {
                    continue;
                }

                for (var i = 0; i < schedule.Length; i++)
                {
                    if (!TryGetPresence(map, topology, schedule, i, pair.Key, out var presence))
                    {
                        continue;
                    }

                    if (!grouped.TryGetValue(presence.SegmentKey, out var list))
                    {
                        list = new List<Presence>();
                        grouped[presence.SegmentKey] = list;
                    }

                    list.Add(presence);
                }
            }

            foreach (var pair in grouped)
            {
                AppendOverlaps(pair.Value, overlaps);
            }

            AppendJunctionAxisOverlaps(bestScheduleByJob, map, topology, overlaps);
            overlaps.Sort(CompareOverlaps);
            return overlaps;
        }

        public static void CountByDirection(
            IReadOnlyList<SimSegmentCoOccupancyRecord> overlaps,
            out int opposite,
            out int sameDirection)
        {
            opposite = 0;
            sameDirection = 0;
            if (overlaps == null)
            {
                return;
            }

            for (var i = 0; i < overlaps.Count; i++)
            {
                if (overlaps[i].OppositeDirection)
                {
                    opposite++;
                }
                else
                {
                    sameDirection++;
                }
            }
        }

        public static string FormatCompactLine(in SimSegmentCoOccupancyRecord overlap) =>
            $"[{(overlap.OppositeDirection ? "对向" : "同向")}] {overlap.SegmentLabel} 容量{overlap.Capacity} " +
            $"| 任务{overlap.JobA} {overlap.DirectionA} [{overlap.JobAStart:F2},{overlap.JobAEnd:F2}] " +
            $"vs 任务{overlap.JobB} {overlap.DirectionB} [{overlap.JobBStart:F2},{overlap.JobBEnd:F2}] " +
            $"重叠 {overlap.OverlapStart:F2}~{overlap.OverlapEnd:F2}s";

        public static string FormatSummary(IReadOnlyList<SimSegmentCoOccupancyRecord> overlaps)
        {
            if (overlaps == null || overlaps.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder(overlaps.Count * 96);
            for (var i = 0; i < overlaps.Count; i++)
            {
                sb.Append("  [");
                sb.Append(i + 1);
                sb.Append("] ");
                sb.AppendLine(FormatCompactLine(overlaps[i]));
            }

            return sb.ToString().TrimEnd();
        }

        private static int CompareOverlaps(SimSegmentCoOccupancyRecord a, SimSegmentCoOccupancyRecord b)
        {
            var opposite = b.OppositeDirection.CompareTo(a.OppositeDirection);
            if (opposite != 0)
            {
                return opposite;
            }

            var start = a.OverlapStart.CompareTo(b.OverlapStart);
            if (start != 0)
            {
                return start;
            }

            return string.CompareOrdinal(a.SegmentLabel, b.SegmentLabel);
        }

        private static void AppendOverlaps(List<Presence> presences, List<SimSegmentCoOccupancyRecord> overlaps)
        {
            if (presences == null || presences.Count < 2)
            {
                return;
            }

            AppendSameDirectionOverflow(presences, overlaps);
            AppendOppositeBodyOverlaps(presences, overlaps);
        }

        private static void AppendSameDirectionOverflow(
            List<Presence> presences,
            List<SimSegmentCoOccupancyRecord> overlaps)
        {
            var events = BuildEdgeEvents(presences, useBody: false);
            var active = new List<Presence>(4);
            var seenPairs = new HashSet<(int Low, int High, long OverlapStartMs)>();
            foreach (var ev in events)
            {
                if (ev.Delta < 0)
                {
                    RemoveActive(active, ev.Presence);
                    continue;
                }

                var occupantCount = 1;
                for (var n = 0; n < active.Count; n++)
                {
                    if (active[n].JobId == ev.Presence.JobId || IsOpposite(active[n], ev.Presence))
                    {
                        continue;
                    }

                    occupantCount++;
                }

                for (var i = 0; i < active.Count; i++)
                {
                    var existing = active[i];
                    if (existing.JobId == ev.Presence.JobId || IsOpposite(existing, ev.Presence))
                    {
                        continue;
                    }

                    if (occupantCount <= Math.Max(1, ev.Presence.Capacity))
                    {
                        continue;
                    }

                    TryRecordOverlap(overlaps, seenPairs, existing, ev.Presence, useBody: false);
                }

                active.Add(ev.Presence);
            }
        }

        private static void AppendOppositeBodyOverlaps(
            List<Presence> presences,
            List<SimSegmentCoOccupancyRecord> overlaps)
        {
            var events = BuildEdgeEvents(presences, useBody: true);
            var active = new List<Presence>(4);
            var seenPairs = new HashSet<(int Low, int High, long OverlapStartMs)>();
            foreach (var ev in events)
            {
                if (ev.Delta < 0)
                {
                    RemoveActive(active, ev.Presence);
                    continue;
                }

                for (var i = 0; i < active.Count; i++)
                {
                    var existing = active[i];
                    if (existing.JobId == ev.Presence.JobId || !IsOpposite(existing, ev.Presence))
                    {
                        continue;
                    }

                    TryRecordOverlap(overlaps, seenPairs, existing, ev.Presence, useBody: true);
                }

                active.Add(ev.Presence);
            }
        }

        private static List<(double Time, int Delta, Presence Presence)> BuildEdgeEvents(
            List<Presence> presences,
            bool useBody)
        {
            var events = new List<(double Time, int Delta, Presence Presence)>(presences.Count * 2);
            for (var i = 0; i < presences.Count; i++)
            {
                var presence = presences[i];
                var end = useBody ? presence.BodyEnd : presence.End;
                events.Add((presence.Start, +1, presence));
                events.Add((end, -1, presence));
            }

            events.Sort((a, b) =>
            {
                var timeCompare = a.Time.CompareTo(b.Time);
                if (timeCompare != 0)
                {
                    return timeCompare;
                }

                return a.Delta.CompareTo(b.Delta);
            });
            return events;
        }

        private static void RemoveActive(List<Presence> active, in Presence presence)
        {
            for (var i = active.Count - 1; i >= 0; i--)
            {
                if (active[i].JobId == presence.JobId
                    && Math.Abs(active[i].Start - presence.Start) < TimeEpsilon)
                {
                    active.RemoveAt(i);
                    break;
                }
            }
        }

        private static void TryRecordOverlap(
            List<SimSegmentCoOccupancyRecord> overlaps,
            HashSet<(int Low, int High, long OverlapStartMs)> seen,
            in Presence a,
            in Presence b,
            bool useBody)
        {
            var aEnd = useBody ? a.BodyEnd : a.End;
            var bEnd = useBody ? b.BodyEnd : b.End;
            var overlapStart = Math.Max(a.Start, b.Start);
            var overlapEnd = Math.Min(aEnd, bEnd);
            if (overlapEnd - overlapStart <= TimeEpsilon)
            {
                return;
            }

            var lowJob = Math.Min(a.JobId, b.JobId);
            var highJob = Math.Max(a.JobId, b.JobId);
            var overlapKey = (lowJob, highJob, (long)Math.Round(overlapStart * 1000d));
            if (!seen.Add(overlapKey))
            {
                return;
            }

            var jobA = a;
            var jobB = b;
            if (jobB.JobId < jobA.JobId)
            {
                jobA = b;
                jobB = a;
            }

            var jobAEnd = useBody ? jobA.BodyEnd : jobA.End;
            var jobBEnd = useBody ? jobB.BodyEnd : jobB.End;
            overlaps.Add(new SimSegmentCoOccupancyRecord
            {
                SegmentKey = jobA.SegmentKey,
                SegmentLabel = jobA.SegmentLabel,
                Capacity = jobA.Capacity,
                OppositeDirection = useBody,
                JobA = jobA.JobId,
                JobB = jobB.JobId,
                DirectionA = jobA.Direction,
                DirectionB = jobB.Direction,
                JobAStart = jobA.Start,
                JobAEnd = jobAEnd,
                JobBStart = jobB.Start,
                JobBEnd = jobBEnd,
                OverlapStart = overlapStart,
                OverlapEnd = overlapEnd,
                OverlapSeconds = overlapEnd - overlapStart,
            });
        }

        private static void AppendJunctionAxisOverlaps(
            Dictionary<int, ConveyorSegmentScheduleEntry[]> bestScheduleByJob,
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            List<SimSegmentCoOccupancyRecord> overlaps)
        {
            if (bestScheduleByJob == null || map == null || topology == null)
            {
                return;
            }

            var grouped = new Dictionary<string, List<AxisNear>>();
            foreach (var pair in bestScheduleByJob)
            {
                var schedule = pair.Value;
                if (schedule == null)
                {
                    continue;
                }

                for (var i = 0; i < schedule.Length; i++)
                {
                    if (!TryGetPresence(map, topology, schedule, i, pair.Key, out var presence))
                    {
                        continue;
                    }

                    var seg = schedule[i];
                    if (!ConveyorJunctionAxis.TryBuildResourceId(
                            map,
                            seg.FromNodeIndex,
                            seg.ToNodeIndex,
                            out var axisId))
                    {
                        continue;
                    }

                    var nearStart = NearJunctionStart(map, topology, seg, presence.Start, presence.End);
                    if (presence.BodyEnd <= nearStart + TimeEpsilon)
                    {
                        continue;
                    }

                    if (!grouped.TryGetValue(axisId, out var list))
                    {
                        list = new List<AxisNear>();
                        grouped[axisId] = list;
                    }

                    list.Add(new AxisNear(
                        axisId,
                        presence.SegmentKey,
                        SimEntityNaming.FormatNode(map, seg.ToNodeIndex),
                        presence.JobId,
                        presence.Direction,
                        nearStart,
                        presence.BodyEnd));
                }
            }

            foreach (var pair in grouped)
            {
                AppendAxisNearOverlaps(pair.Value, overlaps);
            }
        }

        private static void AppendAxisNearOverlaps(
            List<AxisNear> nears,
            List<SimSegmentCoOccupancyRecord> overlaps)
        {
            if (nears == null || nears.Count < 2)
            {
                return;
            }

            nears.Sort((a, b) => a.Start.CompareTo(b.Start));
            var seen = new HashSet<(int Low, int High, long OverlapStartMs)>();
            for (var i = 0; i < nears.Count; i++)
            {
                var left = nears[i];
                for (var j = i + 1; j < nears.Count; j++)
                {
                    var right = nears[j];
                    if (right.Start >= left.End - TimeEpsilon)
                    {
                        break;
                    }

                    if (left.JobId == right.JobId
                        || string.Equals(left.SegmentKey, right.SegmentKey, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var overlapStart = Math.Max(left.Start, right.Start);
                    var overlapEnd = Math.Min(left.End, right.End);
                    if (overlapEnd - overlapStart <= TimeEpsilon)
                    {
                        continue;
                    }

                    var lowJob = Math.Min(left.JobId, right.JobId);
                    var highJob = Math.Max(left.JobId, right.JobId);
                    if (!seen.Add((lowJob, highJob, (long)Math.Round(overlapStart * 1000d))))
                    {
                        continue;
                    }

                    var jobA = left;
                    var jobB = right;
                    if (jobB.JobId < jobA.JobId)
                    {
                        jobA = right;
                        jobB = left;
                    }

                    overlaps.Add(new SimSegmentCoOccupancyRecord
                    {
                        SegmentKey = jobA.AxisId,
                        SegmentLabel = $"{jobA.NodeLabel} 直线",
                        Capacity = 1,
                        OppositeDirection = true,
                        JobA = jobA.JobId,
                        JobB = jobB.JobId,
                        DirectionA = jobA.Direction,
                        DirectionB = jobB.Direction,
                        JobAStart = jobA.Start,
                        JobAEnd = jobA.End,
                        JobBStart = jobB.Start,
                        JobBEnd = jobB.End,
                        OverlapStart = overlapStart,
                        OverlapEnd = overlapEnd,
                        OverlapSeconds = overlapEnd - overlapStart,
                    });
                }
            }
        }

        private static double NearJunctionStart(
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            in ConveyorSegmentScheduleEntry seg,
            double segmentStart,
            double frontAtNode)
        {
            if (topology == null
                || !topology.TryGetEdge(seg.FromNodeIndex, seg.ToNodeIndex, out var edge))
            {
                return Math.Max(segmentStart, frontAtNode);
            }

            var length = Math.Max(0f, edge.DistanceMeters);
            var cargo = ConveyorMapMath.GetCargoOccupancyLengthMeters(map);
            if (length <= cargo + 1e-3f)
            {
                return segmentStart;
            }

            var speed = Math.Max(0.01f, ConveyorMapMath.ResolveSpeedMetersPerSecond(map, edge));
            var near = frontAtNode - cargo / speed;
            var stops = seg.StopArriveSimTimes;
            if (stops != null)
            {
                var capacity = stops.Length;
                for (var s = 0; s < capacity; s++)
                {
                    var meters = ConveyorMapMath.GetZoneNormalizedPosition(map, edge, capacity, s) * length;
                    if (length - meters <= cargo + 0.05f && stops[s] < near)
                    {
                        near = stops[s];
                    }
                }
            }

            if (near < segmentStart)
            {
                near = segmentStart;
            }

            return near;
        }

        /// <summary>车尾离开这段路的时刻（对向与直线自检用的结束时刻）。</summary>
        public static bool TryGetBodyEnd(
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            IReadOnlyList<ConveyorSegmentScheduleEntry> schedule,
            int index,
            out double bodyEnd)
        {
            bodyEnd = 0d;
            if (map == null
                || topology == null
                || !TryGetPresence(map, topology, schedule, index, 0, out var presence))
            {
                return false;
            }

            bodyEnd = presence.BodyEnd;
            return true;
        }

        private static bool IsOpposite(in Presence a, in Presence b) =>
            a.FromNodeIndex == b.ToNodeIndex && a.ToNodeIndex == b.FromNodeIndex;

        private static bool TryGetPresence(
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            IReadOnlyList<ConveyorSegmentScheduleEntry> schedule,
            int index,
            int jobId,
            out Presence presence)
        {
            presence = default;
            if (schedule == null || index < 0 || index >= schedule.Count)
            {
                return false;
            }

            var seg = schedule[index];
            if (seg.FromNodeIndex < 0 || seg.ToNodeIndex < 0)
            {
                return false;
            }

            var start = seg.EntrySimTime;
            var end = seg.ExitSimTime;
            var capacity = 1;
            if (topology.TryGetEdge(seg.FromNodeIndex, seg.ToNodeIndex, out var edge))
            {
                capacity = Math.Max(1, ConveyorMapMath.GetSegmentCapacity(map, edge));
                var entryHop = ConveyorMapMath.GetEdgeEntrySlotHopSeconds(map, edge);
                var approachHop = ConveyorMapMath.GetNodeApproachHopSeconds(map, edge);
                start = seg.EntrySimTime - entryHop;
                if (start < 0)
                {
                    start = 0;
                }

                // 同向只算到车头到达下游节点。路口中心的等待不算还在这段路上。
                end = seg.ExitSimTime + approachHop;
            }

            if (end <= start + TimeEpsilon && seg.OccupancyEndSimTime > start + TimeEpsilon)
            {
                end = seg.OccupancyEndSimTime;
            }

            if (end <= start + TimeEpsilon)
            {
                return false;
            }

            var bodyEnd = ResolveBodyEnd(map, topology, schedule, index, seg, end);
            if (bodyEnd < end)
            {
                bodyEnd = end;
            }

            var label = SimEntityNaming.FormatSegment(map, seg.FromNodeIndex, seg.ToNodeIndex);
            presence = new Presence(
                label,
                label,
                capacity,
                jobId,
                seg.FromNodeIndex,
                seg.ToNodeIndex,
                $"{SimEntityNaming.FormatNode(map, seg.FromNodeIndex)}→{SimEntityNaming.FormatNode(map, seg.ToNodeIndex)}",
                start,
                end,
                bodyEnd);
            return true;
        }

        /// <summary>
        /// 对向占用收到车尾离开这段路。车头到下游节点后若还停着，停的时间也算；
        /// 开走之后还要再开出一个车身长度，尾端才从这段路让开。
        /// </summary>
        private static double ResolveBodyEnd(
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            IReadOnlyList<ConveyorSegmentScheduleEntry> schedule,
            int index,
            in ConveyorSegmentScheduleEntry seg,
            double frontAtNode)
        {
            var tailMeters = (double)ConveyorMapMath.GetCargoOccupancyLengthMeters(map);
            var tailSeconds = TailSeconds(map, topology, seg);
            var occupancy = seg.OccupancyEndSimTime;
            var hasDownstream = ConveyorSegmentScheduleNavigation.TryGetDownstreamIndex(
                schedule,
                index,
                out var downstream);
            if (!hasDownstream)
            {
                if (occupancy > frontAtNode + TimeEpsilon)
                {
                    return occupancy;
                }

                return frontAtNode + tailSeconds;
            }

            var remaining = tailMeters;
            var time = frontAtNode;
            var cursor = downstream;
            for (var guard = 0; guard < 8 && remaining > 1e-3d; guard++)
            {
                if (!TryConsumeScheduleEdge(map, topology, schedule[cursor], ref time, ref remaining))
                {
                    time += tailSeconds * (tailMeters > 1e-3d ? remaining / tailMeters : 1d);
                    break;
                }

                if (remaining <= 1e-3d)
                {
                    break;
                }

                if (!ConveyorSegmentScheduleNavigation.TryGetDownstreamIndex(schedule, cursor, out cursor))
                {
                    time += tailSeconds * (tailMeters > 1e-3d ? remaining / tailMeters : 1d);
                    break;
                }
            }

            if (occupancy > frontAtNode + TimeEpsilon && time < occupancy)
            {
                time = occupancy + tailSeconds;
            }

            return time;
        }

        private static bool TryConsumeScheduleEdge(
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            in ConveyorSegmentScheduleEntry seg,
            ref double time,
            ref double remaining)
        {
            if (map == null
                || topology == null
                || !topology.TryGetEdge(seg.FromNodeIndex, seg.ToNodeIndex, out var edge))
            {
                return false;
            }

            var length = (double)Math.Max(0f, edge.DistanceMeters);
            if (length <= 1e-4d)
            {
                return false;
            }

            var stops = seg.StopArriveSimTimes;
            if (stops == null || stops.Length == 0)
            {
                return false;
            }

            var speed = (double)ConveyorMapMath.ResolveSpeedMetersPerSecond(map, edge);
            var capacity = stops.Length;
            var cursor = 0d;
            var cursorTime = time;
            for (var s = capacity - 1; s >= 0; s--)
            {
                var meters = (double)ConveyorMapMath.GetZoneNormalizedPosition(map, edge, capacity, s) * length;
                if (ConveyorBodyClearance.TryAdvance(
                        ref cursorTime,
                        ref cursor,
                        ref remaining,
                        stops[s],
                        meters,
                        speed,
                        out var clear))
                {
                    time = clear;
                    return true;
                }
            }

            var approach = (double)ConveyorMapMath.GetNodeApproachHopSeconds(map, edge);
            var leaveSlot0 = seg.ExitSimTime;
            if (stops[0] > leaveSlot0)
            {
                leaveSlot0 = stops[0];
            }

            if (ConveyorBodyClearance.TryAdvance(
                    ref cursorTime,
                    ref cursor,
                    ref remaining,
                    leaveSlot0 + approach,
                    length,
                    speed,
                    out var nodeClear))
            {
                time = nodeClear;
                return true;
            }

            if (seg.OccupancyEndSimTime > cursorTime)
            {
                cursorTime = seg.OccupancyEndSimTime;
            }

            time = cursorTime;
            return true;
        }

        private static double TailSeconds(
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            in ConveyorSegmentScheduleEntry seg)
        {
            if (topology != null
                && topology.TryGetEdge(seg.FromNodeIndex, seg.ToNodeIndex, out var edge))
            {
                return ConveyorMapMath.GetCargoTailClearanceSeconds(map, edge);
            }

            var length = ConveyorMapMath.GetCargoOccupancyLengthMeters(map);
            var speed = map != null && map.DefaultSpeedMetersPerSecond > 0f
                ? map.DefaultSpeedMetersPerSecond
                : 0.6f;
            return length / Math.Max(0.01f, speed);
        }

        private readonly struct Presence
        {
            public readonly string SegmentKey;
            public readonly string SegmentLabel;
            public readonly int Capacity;
            public readonly int JobId;
            public readonly int FromNodeIndex;
            public readonly int ToNodeIndex;
            public readonly string Direction;
            public readonly double Start;
            public readonly double End;
            public readonly double BodyEnd;

            public Presence(
                string segmentKey,
                string segmentLabel,
                int capacity,
                int jobId,
                int fromNodeIndex,
                int toNodeIndex,
                string direction,
                double start,
                double end,
                double bodyEnd)
            {
                SegmentKey = segmentKey;
                SegmentLabel = segmentLabel;
                Capacity = capacity;
                JobId = jobId;
                FromNodeIndex = fromNodeIndex;
                ToNodeIndex = toNodeIndex;
                Direction = direction;
                Start = start;
                End = end;
                BodyEnd = bodyEnd;
            }
        }

        private readonly struct AxisNear
        {
            public readonly string AxisId;
            public readonly string SegmentKey;
            public readonly string NodeLabel;
            public readonly int JobId;
            public readonly string Direction;
            public readonly double Start;
            public readonly double End;

            public AxisNear(
                string axisId,
                string segmentKey,
                string nodeLabel,
                int jobId,
                string direction,
                double start,
                double end)
            {
                AxisId = axisId;
                SegmentKey = segmentKey;
                NodeLabel = nodeLabel;
                JobId = jobId;
                Direction = direction;
                Start = start;
                End = end;
            }
        }
    }
}
