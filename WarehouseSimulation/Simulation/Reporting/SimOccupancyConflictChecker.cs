using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>
    /// 根据输送路段预约表与非输送子任务，检测资源占用冲突。
    /// 路口/停留点以预约语义为准，不使用 <see cref="SimSubTaskKind.JunctionExit"/> 等回放子任务的完整时间窗。
    /// 取货点/入库口允许多任务并行（见 <see cref="ISimResourcePolicy.MaxPickupReservationsPerPoint"/> 等），
    /// 仅当并发数超过配置上限时才记为冲突。
    /// 加工站与垂直提升机从 <see cref="SimSubTaskKind.ProcessStationService"/> /
    /// <see cref="SimSubTaskKind.VerticalTransferMove"/> 重建 zone 与设备互斥占用。
    /// </summary>
    public static class SimOccupancyConflictChecker
    {
        private const double TimeEpsilon = 1e-6;

        public readonly struct IntervalSnapshot
        {
            public readonly string ResourceKey;
            public readonly string ResourceLabel;
            public readonly SimOccupancyResourceCategory Category;
            public readonly int JobId;
            public readonly SimSubTaskKind Kind;
            public readonly double Start;
            public readonly double End;

            public IntervalSnapshot(
                string resourceKey,
                string resourceLabel,
                SimOccupancyResourceCategory category,
                int jobId,
                SimSubTaskKind kind,
                double start,
                double end)
            {
                ResourceKey = resourceKey;
                ResourceLabel = resourceLabel;
                Category = category;
                JobId = jobId;
                Kind = kind;
                Start = start;
                End = end;
            }
        }

        public sealed class CheckResult
        {
            public List<SimOccupancyConflictRecord> Conflicts = new();
            public List<IntervalSnapshot> Intervals = new();
            public string FullReportText;
        }

        public static CheckResult Run(
            IReadOnlyList<SimSubTask> subTasks,
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            double simEndTime = 0,
            IWarehouseSimulationBindings bindings = null)
        {
            var result = new CheckResult();
            if (subTasks == null || subTasks.Count == 0 || map == null || topology == null)
            {
                return result;
            }

            var bestScheduleByJob = SimSubTaskQuery.BuildBestScheduleByJob(subTasks);
            var intervals = new List<OccupancyInterval>(subTasks.Count);
            CollectScheduleIntervals(bestScheduleByJob, map, topology, intervals);
            CollectPickupHoldIntervals(subTasks, bestScheduleByJob, map, topology, intervals);
            CollectNonConveyorIntervals(subTasks, map, topology, intervals);
            CollectStationServiceIntervals(subTasks, map, topology, bindings, intervals);

            for (var i = 0; i < intervals.Count; i++)
            {
                var iv = intervals[i];
                result.Intervals.Add(new IntervalSnapshot(
                    iv.ResourceKey,
                    iv.ResourceLabel,
                    iv.Category,
                    iv.JobId,
                    iv.Kind,
                    iv.Start,
                    iv.End));
            }

            intervals.Sort((a, b) =>
            {
                var key = string.CompareOrdinal(a.ResourceKey, b.ResourceKey);
                if (key != 0)
                {
                    return key;
                }

                var start = a.Start.CompareTo(b.Start);
                return start != 0 ? start : a.JobId.CompareTo(b.JobId);
            });

            for (var i = 0; i < intervals.Count;)
            {
                var groupStart = i;
                var resourceKey = intervals[i].ResourceKey;
                i++;
                while (i < intervals.Count
                       && string.Equals(intervals[i].ResourceKey, resourceKey, StringComparison.Ordinal))
                {
                    i++;
                }

                var capacity = ResolveCapacity(intervals[groupStart].Category, bindings);
                AppendConflictsForResource(
                    intervals,
                    groupStart,
                    i - groupStart,
                    capacity,
                    result.Conflicts);
            }

            result.FullReportText = result.Conflicts.Count > 0
                ? SimOccupancyConflictReportBuilder.BuildFullReport(
                    result.Conflicts,
                    result.Intervals,
                    simEndTime)
                : null;
            return result;
        }

        public static List<SimOccupancyConflictRecord> FindConflicts(
            IReadOnlyList<SimSubTask> subTasks,
            WarehouseConveyorMap map,
            ConveyorMapTopology topology) =>
            Run(subTasks, map, topology).Conflicts;

        public static string FormatSummary(IReadOnlyList<SimOccupancyConflictRecord> conflicts)
        {
            if (conflicts == null || conflicts.Count == 0)
            {
                return string.Empty;
            }

            var sb = new System.Text.StringBuilder(conflicts.Count * 128);
            for (var i = 0; i < conflicts.Count; i++)
            {
                SimOccupancyConflictFormatting.AppendSummaryLine(sb, i + 1, conflicts[i]);
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }

        private static int ResolveCapacity(
            SimOccupancyResourceCategory category,
            IWarehouseSimulationBindings bindings)
        {
            if (bindings == null)
            {
                return 1;
            }

            return category switch
            {
                SimOccupancyResourceCategory.Pickup => bindings.MaxPickupReservationsPerPoint > 0
                    ? bindings.MaxPickupReservationsPerPoint
                    : 1,
                SimOccupancyResourceCategory.Infeed => bindings.MaxInfeedReservationsPerPort > 0
                    ? bindings.MaxInfeedReservationsPerPort
                    : 1,
                SimOccupancyResourceCategory.Outfeed => bindings.MaxOutfeedQueuePerPort > 0
                    ? bindings.MaxOutfeedQueuePerPort
                    : 3,
                _ => 1,
            };
        }

        /// <summary>
        /// 扫描线检测：仅当同一资源在某一时刻的并发占用超过 <paramref name="capacity"/> 时记录冲突。
        /// </summary>
        private static void AppendConflictsForResource(
            List<OccupancyInterval> intervals,
            int groupStart,
            int groupCount,
            int capacity,
            List<SimOccupancyConflictRecord> conflicts)
        {
            if (groupCount <= capacity || capacity < 1)
            {
                return;
            }

            var events = new List<(double Time, int Delta, OccupancyInterval Interval)>(groupCount * 2);
            for (var i = 0; i < groupCount; i++)
            {
                var iv = intervals[groupStart + i];
                events.Add((iv.Start, +1, iv));
                events.Add((iv.End, -1, iv));
            }

            events.Sort((a, b) =>
            {
                var timeCompare = a.Time.CompareTo(b.Time);
                if (timeCompare != 0)
                {
                    return timeCompare;
                }

                // 半开区间 [start, end)：同一时刻先结束再开始。
                return a.Delta.CompareTo(b.Delta);
            });

            var active = new List<OccupancyInterval>(capacity + 1);
            var seenPairs = new HashSet<long>();

            foreach (var ev in events)
            {
                if (ev.Delta < 0)
                {
                    for (var i = active.Count - 1; i >= 0; i--)
                    {
                        if (active[i].JobId == ev.Interval.JobId
                            && Math.Abs(active[i].Start - ev.Interval.Start) < TimeEpsilon)
                        {
                            active.RemoveAt(i);
                            break;
                        }
                    }

                    continue;
                }

                if (active.Count >= capacity)
                {
                    for (var i = 0; i < active.Count; i++)
                    {
                        var existing = active[i];
                        if (existing.JobId == ev.Interval.JobId)
                        {
                            continue;
                        }

                        var pairKey = PackJobPair(existing.JobId, ev.Interval.JobId);
                        if (!seenPairs.Add(pairKey))
                        {
                            continue;
                        }

                        var overlapStart = Math.Max(existing.Start, ev.Interval.Start);
                        var overlapEnd = Math.Min(existing.End, ev.Interval.End);
                        var overlapSeconds = overlapEnd - overlapStart;
                        if (overlapSeconds <= TimeEpsilon)
                        {
                            continue;
                        }

                        conflicts.Add(new SimOccupancyConflictRecord
                        {
                            ResourceKey = ev.Interval.ResourceKey,
                            ResourceLabel = ev.Interval.ResourceLabel,
                            Category = ev.Interval.Category,
                            JobA = existing.JobId,
                            JobB = ev.Interval.JobId,
                            JobAStart = existing.Start,
                            JobAEnd = existing.End,
                            JobBStart = ev.Interval.Start,
                            JobBEnd = ev.Interval.End,
                            OverlapStart = overlapStart,
                            OverlapEnd = overlapEnd,
                            OverlapSeconds = overlapSeconds,
                            KindA = existing.Kind,
                            KindB = ev.Interval.Kind,
                        });
                    }
                }

                active.Add(ev.Interval);
            }
        }

        private static long PackJobPair(int jobA, int jobB)
        {
            var low = Math.Min(jobA, jobB);
            var high = Math.Max(jobA, jobB);
            return ((long)low << 32) | (uint)high;
        }

        private readonly struct OccupancyInterval
        {
            public readonly string ResourceKey;
            public readonly string ResourceLabel;
            public readonly SimOccupancyResourceCategory Category;
            public readonly int JobId;
            public readonly SimSubTaskKind Kind;
            public readonly double Start;
            public readonly double End;

            public OccupancyInterval(
                string resourceKey,
                string resourceLabel,
                SimOccupancyResourceCategory category,
                int jobId,
                SimSubTaskKind kind,
                double start,
                double end)
            {
                ResourceKey = resourceKey;
                ResourceLabel = resourceLabel;
                Category = category;
                JobId = jobId;
                Kind = kind;
                Start = start;
                End = end;
            }
        }

        private static void CollectScheduleIntervals(
            Dictionary<int, ConveyorSegmentScheduleEntry[]> bestScheduleByJob,
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            List<OccupancyInterval> intervals)
        {
            if (bestScheduleByJob == null || bestScheduleByJob.Count == 0)
            {
                return;
            }

            foreach (var pair in bestScheduleByJob)
            {
                AppendScheduleIntervals(pair.Key, pair.Value, map, topology, intervals);
            }
        }

        private static void AppendScheduleIntervals(
            int jobId,
            ConveyorSegmentScheduleEntry[] schedule,
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            List<OccupancyInterval> intervals)
        {
            for (var i = 0; i < schedule.Length; i++)
            {
                var seg = schedule[i];
                var stops = seg.StopArriveSimTimes;
                if (stops == null || stops.Length == 0)
                {
                    continue;
                }

                if (!topology.TryGetEdge(seg.FromNodeIndex, seg.ToNodeIndex, out var edge))
                {
                    continue;
                }

                var hop = ConveyorMapMath.GetZoneHopSeconds(map, edge);
                if (hop <= 1e-9f)
                {
                    continue;
                }

                var destIsJunction = seg.ToNodeIndex >= 0
                                     && seg.ToNodeIndex < topology.Map.Nodes.Length
                                     && topology.GetNode(seg.ToNodeIndex).Kind == SimConveyorNodeKind.Junction;
                var capacity = stops.Length;
                var slotIds = ConveyorMapMath.BuildSegmentSlotIds(
                    map,
                    seg.FromNodeIndex,
                    seg.ToNodeIndex,
                    capacity);
                var segmentLabel = SimEntityNaming.FormatSegment(map, seg.FromNodeIndex, seg.ToNodeIndex);

                for (var s = capacity - 1; s >= 0; s--)
                {
                    if (s >= slotIds.Length)
                    {
                        continue;
                    }

                    var arrive = stops[s];
                    var nextArrive = s > 0 ? stops[s - 1] : seg.ExitSimTime;
                    var moveOutStart = nextArrive - hop;
                    if (moveOutStart <= arrive + TimeEpsilon)
                    {
                        continue;
                    }

                    var slotKey = slotIds[s];
                    intervals.Add(new OccupancyInterval(
                        slotKey,
                        $"{segmentLabel} S{s}",
                        SimOccupancyResourceCategory.SegmentSlot,
                        jobId,
                        SimSubTaskKind.SegmentStopDwell,
                        arrive,
                        moveOutStart));
                }

                if (!destIsJunction)
                {
                    continue;
                }

                ConveyorSegmentScheduleEntry? nextSeg = i + 1 < schedule.Length ? schedule[i + 1] : null;
                if (!JunctionSubTaskTiming.TryGetJunctionHoldWindow(
                        seg,
                        nextSeg,
                        topology,
                        map,
                        hop,
                        out var holdStart,
                        out var holdEnd))
                {
                    continue;
                }

                var junctionKey = topology.GetJunctionZoneResourceId(seg.ToNodeIndex);
                intervals.Add(new OccupancyInterval(
                    junctionKey,
                    SimEntityNaming.FormatNode(map, seg.ToNodeIndex),
                    SimOccupancyResourceCategory.Junction,
                    jobId,
                    SimSubTaskKind.JunctionWait,
                    holdStart,
                    holdEnd));
            }
        }

        /// <summary>
        /// 取货点：每任务一条占用。
        /// 入库：抵达取货区 → 堆垛机取货完成（与输送 HoldPickupZone 一致）。
        /// 出库：堆垛机在取货点放货 → 输送接走（不用货架侧 StackerPick/StackerMove 区间，避免误报）。
        /// </summary>
        private static void CollectPickupHoldIntervals(
            IReadOnlyList<SimSubTask> subTasks,
            Dictionary<int, ConveyorSegmentScheduleEntry[]> bestScheduleByJob,
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            List<OccupancyInterval> intervals)
        {
            var outboundJobIds = SimSubTaskQuery.BuildOutboundJobIds(subTasks);

            var holdStartByJob = new Dictionary<int, double>();
            var holdEndByJob = new Dictionary<int, double>();
            var holdKindByJob = new Dictionary<int, SimSubTaskKind>();
            var pickupIndexByJob = new Dictionary<int, int>();
            for (var i = 0; i < subTasks.Count; i++)
            {
                var task = subTasks[i];
                if (task.PickupPointIndex < 0)
                {
                    continue;
                }

                pickupIndexByJob[task.JobId] = task.PickupPointIndex;
                var isOutbound = outboundJobIds.Contains(task.JobId);

                if (!isOutbound && task.Kind == SimSubTaskKind.StackerWait)
                {
                    if (!holdStartByJob.TryGetValue(task.JobId, out var start) || task.StartSimTime < start)
                    {
                        holdStartByJob[task.JobId] = task.StartSimTime;
                    }
                }

                if (!isOutbound && task.Kind == SimSubTaskKind.StackerPick)
                {
                    if (!holdStartByJob.TryGetValue(task.JobId, out var start)
                        || task.StartSimTime < start)
                    {
                        holdStartByJob[task.JobId] = task.StartSimTime;
                    }

                    holdEndByJob[task.JobId] = task.EndSimTime;
                    holdKindByJob[task.JobId] = SimSubTaskKind.StackerPick;
                }

                if (isOutbound && task.Kind == SimSubTaskKind.StackerPlace)
                {
                    // 出库取货点占用始于放货，勿与更早的输送子任务取 min（子任务按时刻排序时输送可能先被扫到）。
                    holdStartByJob[task.JobId] = task.StartSimTime;

                    if (!holdEndByJob.TryGetValue(task.JobId, out var end) || task.EndSimTime > end)
                    {
                        holdEndByJob[task.JobId] = task.EndSimTime;
                    }

                    holdKindByJob[task.JobId] = SimSubTaskKind.StackerPlace;
                }

                if (isOutbound
                    && holdStartByJob.ContainsKey(task.JobId)
                    && task.FromNodeIndex == task.PickupPointIndex
                    && task.Kind is SimSubTaskKind.SegmentTransit
                        or SimSubTaskKind.SegmentHopMove
                        or SimSubTaskKind.SegmentStopDwell
                        or SimSubTaskKind.SegmentQueue)
                {
                    if (!holdEndByJob.TryGetValue(task.JobId, out var end) || task.EndSimTime > end)
                    {
                        holdEndByJob[task.JobId] = task.EndSimTime;
                    }
                }

            }

            if (bestScheduleByJob != null)
            {
                foreach (var jobId in outboundJobIds)
                {
                    if (!holdStartByJob.ContainsKey(jobId)
                        || !pickupIndexByJob.TryGetValue(jobId, out var pickupIndex)
                        || !bestScheduleByJob.TryGetValue(jobId, out var schedule))
                    {
                        continue;
                    }

                    TryExtendOutboundPickupHoldFromSchedule(
                        jobId,
                        pickupIndex,
                        schedule,
                        holdStartByJob,
                        holdEndByJob);
                }
            }

            foreach (var pair in holdStartByJob)
            {
                var jobId = pair.Key;
                if (!holdEndByJob.TryGetValue(jobId, out var holdEnd)
                    || holdEnd <= pair.Value + TimeEpsilon
                    || !pickupIndexByJob.TryGetValue(jobId, out var pickupIndex)
                    || pickupIndex < 0
                    || pickupIndex >= map.Nodes.Length)
                {
                    continue;
                }

                if (!holdKindByJob.TryGetValue(jobId, out var kind))
                {
                    kind = outboundJobIds.Contains(jobId)
                        ? SimSubTaskKind.StackerPlace
                        : SimSubTaskKind.StackerPick;
                }

                ref var pickupNode = ref map.Nodes[pickupIndex];
                intervals.Add(new OccupancyInterval(
                    SimEntityNaming.PickupResourceId(pickupNode, pickupIndex),
                    SimEntityNaming.FormatPickupPoint(map, pickupIndex),
                    SimOccupancyResourceCategory.Pickup,
                    jobId,
                    kind,
                    pair.Value,
                    holdEnd));
            }
        }

        /// <summary>用首个离开取货点的路段预约表，把占用结束时刻延长到货箱实际驶离。</summary>
        private static void TryExtendOutboundPickupHoldFromSchedule(
            int jobId,
            int pickupIndex,
            ConveyorSegmentScheduleEntry[] schedule,
            Dictionary<int, double> holdStartByJob,
            Dictionary<int, double> holdEndByJob)
        {
            if (!holdStartByJob.ContainsKey(jobId))
            {
                return;
            }

            for (var i = 0; i < schedule.Length; i++)
            {
                var seg = schedule[i];
                if (seg.FromNodeIndex != pickupIndex)
                {
                    continue;
                }

                var end = seg.OccupancyEndSimTime > 0 ? seg.OccupancyEndSimTime : seg.ExitSimTime;
                if (end <= holdStartByJob[jobId] + TimeEpsilon)
                {
                    break;
                }

                if (!holdEndByJob.TryGetValue(jobId, out var cur) || end > cur)
                {
                    holdEndByJob[jobId] = end;
                }

                break;
            }
        }

        private static void CollectNonConveyorIntervals(
            IReadOnlyList<SimSubTask> subTasks,
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            List<OccupancyInterval> intervals)
        {
            for (var i = 0; i < subTasks.Count; i++)
            {
                if (TryExtractNonConveyorInterval(subTasks[i], map, topology, out var interval))
                {
                    intervals.Add(interval);
                }
            }
        }

        private static bool TryExtractNonConveyorInterval(
            in SimSubTask task,
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            out OccupancyInterval interval)
        {
            interval = default;
            if (task.EndSimTime <= task.StartSimTime + TimeEpsilon)
            {
                return false;
            }

            string resourceKey;
            string resourceLabel;
            SimOccupancyResourceCategory category;
            switch (task.Kind)
            {
                case SimSubTaskKind.InfeedPlace:
                    if (task.InfeedPortIndex < 0
                        || topology.InfeedNodeIndices == null
                        || task.InfeedPortIndex >= topology.InfeedNodeIndices.Count)
                    {
                        return false;
                    }

                    var infeedNode = topology.InfeedNodeIndices[task.InfeedPortIndex];
                    resourceKey = $"infeed:{infeedNode}";
                    resourceLabel = SimEntityNaming.FormatInfeedPort(map, topology, task.InfeedPortIndex);
                    category = SimOccupancyResourceCategory.Infeed;
                    break;

                case SimSubTaskKind.OutfeedService:
                    if (task.OutfeedPortIndex < 0
                        || topology.OutfeedNodeIndices == null
                        || task.OutfeedPortIndex >= topology.OutfeedNodeIndices.Count)
                    {
                        return false;
                    }

                    var outfeedNode = topology.OutfeedNodeIndices[task.OutfeedPortIndex];
                    resourceKey = $"outfeed:{outfeedNode}";
                    resourceLabel = SimEntityNaming.FormatOutfeedPort(map, topology, task.OutfeedPortIndex);
                    category = SimOccupancyResourceCategory.Outfeed;
                    break;

                default:
                    return false;
            }

            interval = new OccupancyInterval(
                resourceKey,
                resourceLabel,
                category,
                task.JobId,
                task.Kind,
                task.StartSimTime,
                task.EndSimTime);
            return true;
        }

        /// <summary>
        /// 加工站 / 垂直提升机：zone 占用含驶入 hop；设备互斥区间取子任务结束时刻反推服务时长。
        /// </summary>
        private static void CollectStationServiceIntervals(
            IReadOnlyList<SimSubTask> subTasks,
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            List<OccupancyInterval> intervals)
        {
            for (var i = 0; i < subTasks.Count; i++)
            {
                if (!TryExtractStationServiceIntervals(
                        subTasks[i],
                        map,
                        topology,
                        bindings,
                        out var zoneInterval,
                        out var serviceInterval))
                {
                    continue;
                }

                intervals.Add(zoneInterval);
                intervals.Add(serviceInterval);
            }
        }

        private static bool TryExtractStationServiceIntervals(
            in SimSubTask task,
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            out OccupancyInterval zoneInterval,
            out OccupancyInterval serviceInterval)
        {
            zoneInterval = default;
            serviceInterval = default;
            if (task.EndSimTime <= task.StartSimTime + TimeEpsilon)
            {
                return false;
            }

            var nodeIndex = task.ToNodeIndex;
            if (nodeIndex < 0 || nodeIndex >= map.Nodes.Length)
            {
                return false;
            }

            ref var node = ref map.Nodes[nodeIndex];
            string zoneKey;
            string serviceKey;
            string nodeLabel;
            SimOccupancyResourceCategory category;
            float serviceSeconds;

            switch (task.Kind)
            {
                case SimSubTaskKind.ProcessStationService:
                    zoneKey = SimEntityNaming.ProcessStationZoneResourceId(node, nodeIndex);
                    serviceKey = SimEntityNaming.ProcessStationServiceResourceId(node, nodeIndex);
                    nodeLabel = SimEntityNaming.FormatNode(map, nodeIndex);
                    category = SimOccupancyResourceCategory.ProcessStation;
                    serviceSeconds = node.ProcessServiceSeconds > 0f
                        ? node.ProcessServiceSeconds
                        : bindings?.ProcessStationServiceSeconds ?? 30f;
                    break;

                case SimSubTaskKind.VerticalTransferMove:
                    zoneKey = SimEntityNaming.VerticalTransferZoneResourceId(node, nodeIndex);
                    serviceKey = SimEntityNaming.VerticalTransferServiceResourceId(node, nodeIndex);
                    nodeLabel = SimEntityNaming.FormatNode(map, nodeIndex);
                    category = SimOccupancyResourceCategory.VerticalTransfer;
                    serviceSeconds = ConveyorVerticalTransferUtility.ResolveTransferSeconds(node, bindings);
                    break;

                default:
                    return false;
            }

            if (serviceSeconds <= 1e-9f)
            {
                return false;
            }

            var hop = 0f;
            if (task.FromNodeIndex >= 0
                && topology.TryGetEdge(task.FromNodeIndex, nodeIndex, out var edge))
            {
                hop = ConveyorMapMath.GetZoneHopSeconds(map, edge);
            }

            var zoneStart = task.StartSimTime - hop;
            if (zoneStart < 0d)
            {
                zoneStart = 0d;
            }

            var serviceEnd = task.EndSimTime;
            var serviceStart = serviceEnd - serviceSeconds;
            if (serviceStart < task.StartSimTime - TimeEpsilon)
            {
                serviceStart = task.StartSimTime;
            }

            zoneInterval = new OccupancyInterval(
                zoneKey,
                nodeLabel,
                category,
                task.JobId,
                task.Kind,
                zoneStart,
                serviceEnd);
            serviceInterval = new OccupancyInterval(
                serviceKey,
                $"{nodeLabel} 设备",
                category,
                task.JobId,
                task.Kind,
                serviceStart,
                serviceEnd);
            return true;
        }
    }
}
