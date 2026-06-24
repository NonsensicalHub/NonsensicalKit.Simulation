using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>从输送路段预约表生成 ZPA / 路口子任务记录。</summary>
    internal sealed class ConveyorSubTaskRecorder
    {
        internal delegate void RecordSubTaskDelegate(
            WarehouseJob job,
            SimSubTaskKind kind,
            double startSimTime,
            double endSimTime,
            int stackerId,
            GridIndex slot,
            int fromNodeIndex = -1,
            int toNodeIndex = -1,
            int segmentSlotIndex = -1,
            bool attachPathContext = false,
            GridIndex stackerFromSlot = default,
            GridIndex stackerToSlot = default,
            int stackerRailColumn = -1,
            bool hasStackerPose = false);

        private readonly ConveyorMapTopology _topology;
        private readonly IConveyorMapSource _mapSource;
        private readonly RecordSubTaskDelegate _record;

        public ConveyorSubTaskRecorder(
            ConveyorMapTopology topology,
            IConveyorMapSource mapSource,
            RecordSubTaskDelegate record)
        {
            _topology = topology;
            _mapSource = mapSource;
            _record = record;
        }

        /// <summary>单 zone 预约完成后写入 hop/停留子任务（逐步 DES）。</summary>
        public void RecordForZone(
            WarehouseJob job,
            in ConveyorPathZone zone,
            int zoneIndex,
            bool isFirstZoneOnPath)
        {
            // 路口 zone 的“驶入 / 等待 / 驶出路口”三段子任务由 RecordJunctionAfterNextZone 在下一段入口
            // zone 就绪后统一记录；此处不再额外记录通用的“hop 进入 + 路口停留”，
            // 否则会与路口子任务时间窗完全重叠（同一次穿越被记两遍 → 子任务时间轴重叠自检失败）。
            if (zone.Kind == ConveyorPathZoneKind.Junction)
            {
                return;
            }

            // 取货点/出库口 zone 的驶入 hop 已在来向边 flush（RecordEdgeSlotSubTasksFromChain）中记录。
            if (zone.Kind is ConveyorPathZoneKind.Pickup or ConveyorPathZoneKind.Outfeed)
            {
                return;
            }

            var chain = job.ConveyorPathZones;
            var hop = zone.HopSeconds;
            var attachPath = isFirstZoneOnPath || zoneIndex == (chain?.Count ?? 0) - 1;

            if (zone.Kind == ConveyorPathZoneKind.EdgeSlot)
            {
                if (isFirstZoneOnPath
                    && _topology.GetNode(zone.FromNodeIndex).Kind == SimConveyorNodeKind.InfeedPort)
                {
                    if (TryRecordInfeedQueueSubTask(job, zone, hop, attachPath))
                    {
                        attachPath = false;
                    }

                    TryRecordInfeedDepartMoveSubTask(
                        job,
                        ToSegmentStub(zone),
                        hop,
                        zone.ArriveSimTime,
                        zone.SlotIndex,
                        attachPath);
                }
                else if (isFirstZoneOnPath
                         && job.Direction == SimFlowDirection.Outbound
                         && _topology.GetNode(zone.FromNodeIndex).Kind == SimConveyorNodeKind.PickupPoint)
                {
                    if (TryRecordOutboundPickupQueueSubTask(job, zone, hop, attachPath))
                    {
                        attachPath = false;
                    }

                    TryRecordOutboundDepartMoveSubTask(
                        job,
                        ToSegmentStub(zone),
                        hop,
                        zone.ArriveSimTime,
                        zone.SlotIndex,
                        attachPath);
                }

                // 路段槽位 hop/停留由整条边 flush 后统一记录（见 RecordEdgeSlotSubTasksFromChain）。
                return;
            }

            var hopStart = zone.ArriveSimTime - hop;
            var followsJunction = chain != null
                && zoneIndex > 0
                && zoneIndex - 1 < chain.Count
                && chain[zoneIndex - 1].Kind == ConveyorPathZoneKind.Junction;

            if (!followsJunction && hop > 1e-9f)
            {
                var hopMoveStart = hopStart;
                var hopMoveEnd = zone.ArriveSimTime;
                if (hopMoveEnd > hopMoveStart + 1e-9)
                {
                    _record(
                        job,
                        SimSubTaskKind.SegmentHopMove,
                        hopMoveStart,
                        hopMoveEnd,
                        job.AssignedStackerId,
                        job.TargetSlot,
                        zone.FromNodeIndex,
                        zone.ToNodeIndex,
                        -1,
                        attachPath);
                }
            }
        }

        /// <summary>
        /// 在整条路径边预约完成后，按最终 zone 时刻统一记录 hop/停留。
        /// 逐 zone 记录时上游 Leave 尚未反映下游拥堵，会导致时间轴空白。
        /// </summary>
        public void RecordEdgeSlotSubTasksFromChain(
            WarehouseJob job,
            int pathEdgeIndex,
            bool attachPathContext = false)
        {
            var chain = job?.ConveyorPathZones;
            job.ConveyorEdgeSubTasksRecorded ??= new HashSet<int>();
            if (chain == null || chain.Count == 0 || !job.ConveyorEdgeSubTasksRecorded.Add(pathEdgeIndex))
            {
                return;
            }

            var slots = new List<(int ZoneIndex, ConveyorPathZone Zone)>();
            for (var i = 0; i < chain.Count; i++)
            {
                var zone = chain[i];
                if (zone.Kind == ConveyorPathZoneKind.EdgeSlot && zone.PathEdgeIndex == pathEdgeIndex)
                {
                    slots.Add((i, zone));
                }
            }

            if (slots.Count == 0)
            {
                return;
            }

            slots.Sort((a, b) => a.Zone.SlotIndex.CompareTo(b.Zone.SlotIndex));
            var hop = slots[0].Zone.HopSeconds;
            if (hop <= 1e-9f)
            {
                return;
            }

            ReconcileEdgeZoneLeaves(job, slots, hop);

            var fromInfeed = IsFirstEdgeFromInfeed(slots[^1].Zone);
            var fromOutboundPickup = IsFirstEdgeFromOutboundPickup(slots[^1].Zone);
            var pathAttached = false;
            for (var si = slots.Count - 1; si >= 0; si--)
            {
                var (zoneIndex, zone) = slots[si];
                var followsJunction = zoneIndex > 0 && chain[zoneIndex - 1].Kind == ConveyorPathZoneKind.Junction;
                var skipHop = followsJunction
                              || (fromInfeed && si == slots.Count - 1)
                              || (fromOutboundPickup && si == slots.Count - 1);

                if (!skipHop)
                {
                    var hopMoveStart = zone.ArriveSimTime - hop;
                    if (si + 1 < slots.Count)
                    {
                        hopMoveStart = slots[si + 1].Zone.LeaveSimTime;
                    }

                    if (zone.ArriveSimTime > hopMoveStart + 1e-9)
                    {
                        var attach = attachPathContext && !pathAttached;
                        _record(
                            job,
                            SimSubTaskKind.SegmentHopMove,
                            hopMoveStart,
                            zone.ArriveSimTime,
                            job.AssignedStackerId,
                            job.TargetSlot,
                            zone.FromNodeIndex,
                            zone.ToNodeIndex,
                            zone.SlotIndex,
                            attach);
                        pathAttached |= attach;
                    }
                }

                if (zone.LeaveSimTime > zone.ArriveSimTime + 1e-9)
                {
                    var attach = attachPathContext && !pathAttached;
                    _record(
                        job,
                        SimSubTaskKind.SegmentStopDwell,
                        zone.ArriveSimTime,
                        zone.LeaveSimTime,
                        job.AssignedStackerId,
                        job.TargetSlot,
                        zone.FromNodeIndex,
                        zone.ToNodeIndex,
                        zone.SlotIndex,
                        attach);
                    pathAttached |= attach;
                }
            }

            TryRecordTerminalExitHopFromChain(
                job,
                chain,
                pathEdgeIndex,
                slots,
                hop,
                attachPathContext,
                ref pathAttached);
        }

        /// <summary>来向边终点为取货点/出库口时，补记 slot-0 驶离 → 终点的 hop（原由 RecordForZone 在终端 zone 上重复记录）。</summary>
        private void TryRecordTerminalExitHopFromChain(
            WarehouseJob job,
            IReadOnlyList<ConveyorPathZone> chain,
            int pathEdgeIndex,
            List<(int ZoneIndex, ConveyorPathZone Zone)> slotsAsc,
            float hop,
            bool attachPathContext,
            ref bool pathAttached)
        {
            if (hop <= 1e-9f)
            {
                return;
            }

            ConveyorPathZone? terminal = null;
            for (var i = 0; i < chain.Count; i++)
            {
                var zone = chain[i];
                if (zone.PathEdgeIndex == pathEdgeIndex
                    && zone.Kind is ConveyorPathZoneKind.Pickup or ConveyorPathZoneKind.Outfeed)
                {
                    terminal = zone;
                    break;
                }
            }

            if (!terminal.HasValue)
            {
                return;
            }

            var terminalZone = terminal.Value;
            var s0Leave = 0d;
            for (var i = 0; i < slotsAsc.Count; i++)
            {
                if (slotsAsc[i].Zone.SlotIndex == 0)
                {
                    s0Leave = slotsAsc[i].Zone.LeaveSimTime;
                    break;
                }
            }

            var exitTime = terminalZone.ArriveSimTime;
            var hopStart = Math.Max(exitTime - hop, s0Leave);
            if (exitTime <= hopStart + 1e-9)
            {
                return;
            }

            var attach = attachPathContext && !pathAttached;
            _record(
                job,
                SimSubTaskKind.SegmentHopMove,
                hopStart,
                exitTime,
                job.AssignedStackerId,
                job.TargetSlot,
                terminalZone.FromNodeIndex,
                terminalZone.ToNodeIndex,
                -1,
                attach);
            pathAttached |= attach;
        }

        /// <summary>根据内侧槽位最终到达时刻，反推外侧槽位应有停留。</summary>
        private static void ReconcileEdgeZoneLeaves(
            WarehouseJob job,
            List<(int ZoneIndex, ConveyorPathZone Zone)> slotsAsc,
            float hop)
        {
            for (var i = 1; i < slotsAsc.Count; i++)
            {
                var inward = slotsAsc[i - 1].Zone;
                var upstreamIndex = slotsAsc[i].ZoneIndex;
                var impliedLeave = inward.ArriveSimTime - hop;
                if (impliedLeave <= slotsAsc[i].Zone.LeaveSimTime + 1e-9)
                {
                    continue;
                }

                var upstream = slotsAsc[i].Zone;
                upstream.LeaveSimTime = impliedLeave;
                slotsAsc[i] = (upstreamIndex, upstream);
                job.ConveyorPathZones[upstreamIndex] = upstream;
            }
        }

        private bool IsFirstEdgeFromInfeed(in ConveyorPathZone zone) =>
            zone.Kind == ConveyorPathZoneKind.EdgeSlot
            && zone.PathEdgeIndex == 0
            && _topology.GetNode(zone.FromNodeIndex).Kind == SimConveyorNodeKind.InfeedPort;

        private bool IsFirstEdgeFromOutboundPickup(in ConveyorPathZone zone) =>
            zone.Kind == ConveyorPathZoneKind.EdgeSlot
            && zone.PathEdgeIndex == 0
            && _topology.GetNode(zone.FromNodeIndex).Kind == SimConveyorNodeKind.PickupPoint;

        private static bool TryGetNextSameEdgeSlot(
            IReadOnlyList<ConveyorPathZone> chain,
            int zoneIndex,
            out ConveyorPathZone nextZone)
        {
            nextZone = default;
            if (chain == null
                || zoneIndex < 0
                || zoneIndex + 1 >= chain.Count
                || chain[zoneIndex].Kind != ConveyorPathZoneKind.EdgeSlot)
            {
                return false;
            }

            var edgeIndex = chain[zoneIndex].PathEdgeIndex;
            var candidate = chain[zoneIndex + 1];
            if (candidate.Kind != ConveyorPathZoneKind.EdgeSlot
                || candidate.PathEdgeIndex != edgeIndex)
            {
                return false;
            }

            nextZone = candidate;
            return true;
        }

        /// <summary>下一段入口 zone 已就绪后，补记路口驶入/等待/驶出子任务。</summary>
        public void RecordJunctionAfterNextZone(
            WarehouseJob job,
            int junctionZoneIndex,
            in ConveyorPathZone nextZone)
        {
            if (job?.ConveyorPathZones == null
                || junctionZoneIndex < 0
                || junctionZoneIndex >= job.ConveyorPathZones.Count
                || job.ConveyorSegmentSchedule == null
                || job.ConveyorSegmentSchedule.Count == 0)
            {
                return;
            }

            var junction = job.ConveyorPathZones[junctionZoneIndex];
            if (junction.Kind != ConveyorPathZoneKind.Junction)
            {
                return;
            }

            var seg = job.ConveyorSegmentSchedule[job.ConveyorSegmentSchedule.Count - 1];
            var nextStub = new ConveyorSegmentScheduleEntry
            {
                FromNodeIndex = nextZone.FromNodeIndex,
                ToNodeIndex = nextZone.ToNodeIndex,
                DesiredEntrySimTime = nextZone.DesiredArriveSimTime,
                EntrySimTime = nextZone.ArriveSimTime,
                ExitSimTime = nextZone.LeaveSimTime,
                StopArriveSimTimes = new[] { nextZone.ArriveSimTime },
            };
            RecordJunctionSubTasks(
                job,
                seg,
                nextStub,
                junction.HopSeconds,
                attachPathContext: false,
                downstreamEntryArrive: nextZone.ArriveSimTime);
        }

        /// <summary>
        /// 货物已在入库口完成放货，但下游首个槽位仍被占用时，在入库口排队等待驶入。
        /// 记录 [入库完成时刻, 驶离移动开始时刻] 的排队子任务，填补放货与驶离之间的时间轴空白。
        /// </summary>
        private bool TryRecordInfeedQueueSubTask(
            WarehouseJob job,
            in ConveyorPathZone zone,
            float hop,
            bool attachPathContext)
        {
            var infeedEnd = job.InfeedCompleteSimTime > 0
                ? job.InfeedCompleteSimTime
                : job.ScheduledCompleteTime;
            if (infeedEnd <= 0)
            {
                return false;
            }

            // 驶离移动最早开始时刻与 TryResolveInfeedDepartMoveWindow 保持一致。
            var moveStart = hop > 1e-9f ? zone.ArriveSimTime - hop : zone.ArriveSimTime;
            if (moveStart <= infeedEnd + 1e-9)
            {
                return false;
            }

            _record(
                job,
                SimSubTaskKind.SegmentQueue,
                infeedEnd,
                moveStart,
                job.AssignedStackerId,
                job.TargetSlot,
                zone.FromNodeIndex,
                zone.ToNodeIndex,
                zone.SlotIndex >= 0 ? zone.SlotIndex : 0,
                attachPathContext);
            return true;
        }

        /// <summary>
        /// 堆垛机已在交互点放货，但暂无可用出库口或首段入口停留点仍被占用时，在交互点等待。
        /// </summary>
        private bool TryRecordOutboundPickupQueueSubTask(
            WarehouseJob job,
            in ConveyorPathZone zone,
            float hop,
            bool attachPathContext)
        {
            var pickupEnd = job.PickupCompleteSimTime > 0
                ? job.PickupCompleteSimTime
                : job.ScheduledCompleteTime;
            if (pickupEnd <= 0)
            {
                return false;
            }

            var moveStart = hop > 1e-9f ? zone.ArriveSimTime - hop : zone.ArriveSimTime;
            if (moveStart <= pickupEnd + 1e-9)
            {
                return false;
            }

            _record(
                job,
                SimSubTaskKind.SegmentQueue,
                pickupEnd,
                moveStart,
                job.AssignedStackerId,
                job.TargetSlot,
                zone.FromNodeIndex,
                zone.ToNodeIndex,
                -1,
                attachPathContext);
            return true;
        }

        private void TryRecordOutboundDepartMoveSubTask(
            WarehouseJob job,
            ConveyorSegmentScheduleEntry seg,
            float hop,
            double entryArrive,
            int entrySlot,
            bool attachPathContext)
        {
            var pickupEnd = job.PickupCompleteSimTime > 0
                ? job.PickupCompleteSimTime
                : job.ScheduledCompleteTime;
            if (!TryResolveOutboundDepartMoveWindow(pickupEnd, entryArrive, hop, out var moveStart, out var moveEnd))
            {
                return;
            }

            _record(
                job,
                SimSubTaskKind.OutboundMove,
                moveStart,
                moveEnd,
                job.AssignedStackerId,
                job.TargetSlot,
                seg.FromNodeIndex,
                seg.ToNodeIndex,
                entrySlot,
                attachPathContext);
        }

        private static bool TryResolveOutboundDepartMoveWindow(
            double pickupEnd,
            double entryArrive,
            float hop,
            out double moveStart,
            out double moveEnd)
        {
            moveEnd = entryArrive;
            moveStart = hop > 1e-9f ? entryArrive - hop : entryArrive;
            if (moveStart < pickupEnd - 1e-9)
            {
                moveStart = pickupEnd;
            }

            if (moveEnd <= moveStart + 1e-9)
            {
                if (hop <= 1e-9f)
                {
                    moveStart = 0;
                    moveEnd = 0;
                    return false;
                }

                moveStart = pickupEnd;
                moveEnd = pickupEnd + hop;
            }

            return moveEnd > moveStart + 1e-9;
        }

        private static ConveyorSegmentScheduleEntry ToSegmentStub(in ConveyorPathZone zone) =>
            new()
            {
                FromNodeIndex = zone.FromNodeIndex,
                ToNodeIndex = zone.ToNodeIndex,
                SlotIndex = zone.SlotIndex,
                DesiredEntrySimTime = zone.DesiredArriveSimTime,
                EntrySimTime = zone.ArriveSimTime,
                ExitSimTime = zone.LeaveSimTime,
            };

        /// <summary>在单段预约完成后立即写入该段子任务（逐步 DES）。</summary>
        public void RecordForSegment(WarehouseJob job, int segmentIndex, double routeDesiredStart)
        {
            var schedule = job.ConveyorSegmentSchedule;
            if (schedule == null || segmentIndex < 0 || segmentIndex >= schedule.Count)
            {
                return;
            }

            RecordSegmentAt(job, schedule, segmentIndex, routeDesiredStart, deferJunction: true);

            if (segmentIndex > 0)
            {
                TryRecordDeferredJunction(job, schedule, segmentIndex - 1);
            }
        }

        private void RecordSegmentAt(
            WarehouseJob job,
            IReadOnlyList<ConveyorSegmentScheduleEntry> schedule,
            int i,
            double routeDesiredStart,
            bool deferJunction)
        {
            var seg = schedule[i];
            var infeedEnd = job.InfeedCompleteSimTime;
            var desired = i == 0 ? routeDesiredStart : seg.DesiredEntrySimTime;
            var attachPath = i == schedule.Count - 1 || (i == 0 && infeedEnd <= 0);

            var stops = seg.StopArriveSimTimes;
            var hop = 0f;
            if (_topology.TryGetEdge(seg.FromNodeIndex, seg.ToNodeIndex, out var edge))
            {
                hop = ConveyorMapMath.GetZoneHopSeconds(_mapSource?.ConveyorMap, edge);
            }

            var fromInfeed = _topology.GetNode(seg.FromNodeIndex).Kind == SimConveyorNodeKind.InfeedPort;
            if (i == 0 && infeedEnd > 0 && seg.DesiredEntrySimTime > infeedEnd + 1e-9)
            {
                _record(
                    job,
                    SimSubTaskKind.SegmentQueue,
                    infeedEnd,
                    seg.DesiredEntrySimTime,
                    job.AssignedStackerId,
                    job.TargetSlot,
                    seg.FromNodeIndex,
                    seg.ToNodeIndex,
                    seg.SlotIndex,
                    attachPathContext: true);
                attachPath = false;
            }

            if (ShouldRecordSegmentQueue(
                    job,
                    schedule,
                    i,
                    seg,
                    desired,
                    fromInfeed,
                    hop,
                    infeedEnd,
                    stops))
            {
                _record(
                    job,
                    SimSubTaskKind.SegmentQueue,
                    desired,
                    seg.EntrySimTime,
                    job.AssignedStackerId,
                    job.TargetSlot,
                    seg.FromNodeIndex,
                    seg.ToNodeIndex,
                    seg.SlotIndex,
                    attachPathContext: attachPath);
                attachPath = false;
            }

            if (stops == null || stops.Length == 0)
            {
                if (i == 0 && fromInfeed)
                {
                    var entrySlot = seg.SlotIndex >= 0 ? seg.SlotIndex : 0;
                    RecordInfeedDepartMoveSubTask(
                        job,
                        seg,
                        seg.DesiredEntrySimTime,
                        seg.EntrySimTime,
                        entrySlot,
                        attachPath);
                    attachPath = false;
                }

                _record(
                    job,
                    SimSubTaskKind.SegmentTransit,
                    seg.EntrySimTime,
                    seg.ExitSimTime,
                    job.AssignedStackerId,
                    job.TargetSlot,
                    seg.FromNodeIndex,
                    seg.ToNodeIndex,
                    seg.SlotIndex,
                    attachPathContext: attachPath);
                return;
            }

            if (i == 0 && fromInfeed)
            {
                RecordInfeedDepartMoveSubTask(job, seg, stops, hop, attachPath);
                attachPath = false;
            }

            ConveyorSegmentScheduleEntry? nextSeg = i + 1 < schedule.Count ? schedule[i + 1] : null;
            var destIsJunction = seg.ToNodeIndex >= 0
                                 && seg.ToNodeIndex < _topology.Map.Nodes.Length
                                 && _topology.GetNode(seg.ToNodeIndex).Kind == SimConveyorNodeKind.Junction;
            if (deferJunction && destIsJunction && nextSeg == null)
            {
                RecordZpaSegmentSubTasks(job, seg, stops, hop, attachPath, fromInfeed, nextSeg: null, skipJunction: true);
            }
            else
            {
                RecordZpaSegmentSubTasks(job, seg, stops, hop, attachPath, fromInfeed, nextSeg);
            }
        }

        private void TryRecordDeferredJunction(
            WarehouseJob job,
            IReadOnlyList<ConveyorSegmentScheduleEntry> schedule,
            int junctionSegmentIndex)
        {
            var seg = schedule[junctionSegmentIndex];
            if (seg.ToNodeIndex < 0
                || seg.ToNodeIndex >= _topology.Map.Nodes.Length
                || _topology.GetNode(seg.ToNodeIndex).Kind != SimConveyorNodeKind.Junction)
            {
                return;
            }

            if (junctionSegmentIndex + 1 >= schedule.Count)
            {
                return;
            }

            if (!_topology.TryGetEdge(seg.FromNodeIndex, seg.ToNodeIndex, out var edge))
            {
                return;
            }

            var hop = ConveyorMapMath.GetZoneHopSeconds(_mapSource?.ConveyorMap, edge);
            var nextSeg = schedule[junctionSegmentIndex + 1];
            RecordJunctionSubTasks(job, seg, nextSeg, hop, attachPathContext: false);
        }

        private void RecordInfeedDepartMoveSubTask(
            WarehouseJob job,
            ConveyorSegmentScheduleEntry seg,
            double[] stops,
            float hop,
            bool attachPathContext)
        {
            var entrySlot = stops.Length - 1;
            var entryArrive = stops[entrySlot];
            TryRecordInfeedDepartMoveSubTask(
                job,
                seg,
                hop,
                entryArrive,
                entrySlot,
                attachPathContext);
        }

        private void RecordInfeedDepartMoveSubTask(
            WarehouseJob job,
            ConveyorSegmentScheduleEntry seg,
            double moveStartIgnored,
            double entryArrive,
            int entrySlot,
            bool attachPathContext)
        {
            var hop = 0f;
            if (_topology.TryGetEdge(seg.FromNodeIndex, seg.ToNodeIndex, out var edge))
            {
                hop = ConveyorMapMath.GetZoneHopSeconds(_mapSource?.ConveyorMap, edge);
            }

            TryRecordInfeedDepartMoveSubTask(
                job,
                seg,
                hop,
                entryArrive,
                entrySlot,
                attachPathContext);
        }

        private void TryRecordInfeedDepartMoveSubTask(
            WarehouseJob job,
            ConveyorSegmentScheduleEntry seg,
            float hop,
            double entryArrive,
            int entrySlot,
            bool attachPathContext)
        {
            var infeedEnd = job.InfeedCompleteSimTime > 0
                ? job.InfeedCompleteSimTime
                : job.ScheduledCompleteTime;
            if (!TryResolveInfeedDepartMoveWindow(infeedEnd, entryArrive, hop, out var moveStart, out var moveEnd))
            {
                return;
            }

            _record(
                job,
                SimSubTaskKind.InfeedMove,
                moveStart,
                moveEnd,
                job.AssignedStackerId,
                job.TargetSlot,
                seg.FromNodeIndex,
                seg.ToNodeIndex,
                entrySlot,
                attachPathContext);
        }

        private static bool TryResolveInfeedDepartMoveWindow(
            double infeedEnd,
            double entryArrive,
            float hop,
            out double moveStart,
            out double moveEnd)
        {
            moveEnd = entryArrive;
            moveStart = hop > 1e-9f ? entryArrive - hop : entryArrive;
            if (moveStart < infeedEnd - 1e-9)
            {
                moveStart = infeedEnd;
            }

            if (moveEnd <= moveStart + 1e-9)
            {
                if (hop <= 1e-9f)
                {
                    moveStart = 0;
                    moveEnd = 0;
                    return false;
                }

                moveStart = infeedEnd;
                moveEnd = infeedEnd + hop;
            }

            return moveEnd > moveStart + 1e-9;
        }

        private bool ShouldSkipSegmentQueueAfterJunction(
            IReadOnlyList<ConveyorSegmentScheduleEntry> schedule,
            int segmentIndex)
        {
            if (segmentIndex <= 0)
            {
                return false;
            }

            var prev = schedule[segmentIndex - 1];
            return _topology.GetNode(prev.ToNodeIndex).Kind == SimConveyorNodeKind.Junction;
        }

        private bool ShouldRecordSegmentQueue(
            WarehouseJob job,
            IReadOnlyList<ConveyorSegmentScheduleEntry> schedule,
            int segmentIndex,
            ConveyorSegmentScheduleEntry seg,
            double desired,
            bool fromInfeed,
            float hop,
            double infeedEnd,
            double[] stops)
        {
            if (seg.EntrySimTime <= desired + 1e-9
                || ShouldSkipSegmentQueueAfterJunction(schedule, segmentIndex))
            {
                return false;
            }

            if (segmentIndex != 0 || !fromInfeed)
            {
                return true;
            }

            var entryArrive = stops != null && stops.Length > 0
                ? stops[stops.Length - 1]
                : seg.EntrySimTime;
            var infeedServiceEnd = infeedEnd > 0 ? infeedEnd : job.ScheduledCompleteTime;
            return !TryResolveInfeedDepartMoveWindow(
                infeedServiceEnd,
                entryArrive,
                hop,
                out var moveStart,
                out var moveEnd)
                || moveStart > desired + 1e-9
                || moveEnd < seg.EntrySimTime - 1e-9;
        }

        private static double ResolveHopInStart(
            int stopIndex,
            double[] stops,
            double segmentEntrySimTime,
            float hop)
        {
            var arrive = stops[stopIndex];
            if (stopIndex >= stops.Length - 1)
            {
                return Math.Max(segmentEntrySimTime, arrive - hop);
            }

            var upstreamLeave = stops[stopIndex + 1] - hop;
            return Math.Max(upstreamLeave, arrive - hop);
        }

        private void RecordZpaSegmentSubTasks(
            WarehouseJob job,
            ConveyorSegmentScheduleEntry seg,
            double[] stops,
            float hop,
            bool attachPathContext,
            bool fromInfeed = false,
            ConveyorSegmentScheduleEntry? nextSeg = null,
            bool skipJunction = false)
        {
            var capacity = stops.Length;
            var destIsJunction = seg.ToNodeIndex >= 0
                                 && seg.ToNodeIndex < _topology.Map.Nodes.Length
                                 && _topology.GetNode(seg.ToNodeIndex).Kind
                                     == SimConveyorNodeKind.Junction;
            var destIsPickup = seg.ToNodeIndex >= 0
                               && seg.ToNodeIndex < _topology.Map.Nodes.Length
                               && _topology.GetNode(seg.ToNodeIndex).Kind
                                   == SimConveyorNodeKind.PickupPoint;
            var pathAttached = false;
            void RecordPhase(SimSubTaskKind kind, double start, double end, int stopIndex)
            {
                if (end <= start + 1e-9)
                {
                    return;
                }

                var attach = attachPathContext && !pathAttached;
                _record(
                    job,
                    kind,
                    start,
                    end,
                    job.AssignedStackerId,
                    job.TargetSlot,
                    seg.FromNodeIndex,
                    seg.ToNodeIndex,
                    stopIndex,
                    attach);
                pathAttached |= attach;
            }

            for (var s = capacity - 1; s >= 0; s--)
            {
                var arrive = stops[s];
                if (s < capacity - 1)
                {
                    var hopInStart = ResolveHopInStart(s, stops, seg.EntrySimTime, hop);
                    RecordPhase(SimSubTaskKind.SegmentHopMove, hopInStart, arrive, s);
                }

                var nextArrive = s > 0 ? stops[s - 1] : seg.ExitSimTime;
                var moveOutStart = nextArrive - hop;
                if (s == 0 && destIsPickup)
                {
                    moveOutStart = Math.Min(moveOutStart, seg.ExitSimTime - hop);
                }

                if (s == 0 && destIsJunction)
                {
                    moveOutStart = Math.Min(moveOutStart, seg.ExitSimTime);
                }

                RecordPhase(SimSubTaskKind.SegmentStopDwell, arrive, moveOutStart, s);
            }

            RecordSegmentExitCoverage(job, seg, stops, hop, capacity, destIsPickup, destIsJunction);

            if (destIsJunction && !skipJunction)
            {
                RecordJunctionSubTasks(job, seg, nextSeg, hop, attachPathContext && !pathAttached);
            }
        }

        private void RecordSegmentExitCoverage(
            WarehouseJob job,
            ConveyorSegmentScheduleEntry seg,
            double[] stops,
            float hop,
            int capacity,
            bool destIsPickup,
            bool destIsJunction)
        {
            if (hop <= 1e-9f || destIsJunction)
            {
                return;
            }

            var exitHopStart = seg.ExitSimTime - hop;
            if (seg.ExitSimTime <= exitHopStart + 1e-9)
            {
                return;
            }

            var inboundEnd = capacity > 0 ? stops[0] : exitHopStart;
            if (seg.ExitSimTime <= inboundEnd + 1e-9)
            {
                return;
            }

            var hopStart = Math.Max(exitHopStart, inboundEnd);
            if (seg.ExitSimTime <= hopStart + 1e-9)
            {
                return;
            }

            _record(
                job,
                SimSubTaskKind.SegmentHopMove,
                hopStart,
                seg.ExitSimTime,
                job.AssignedStackerId,
                job.TargetSlot,
                seg.FromNodeIndex,
                seg.ToNodeIndex,
                destIsPickup ? -1 : 0);
        }

        private void RecordJunctionSubTasks(
            WarehouseJob job,
            ConveyorSegmentScheduleEntry seg,
            ConveyorSegmentScheduleEntry? nextSeg,
            float hop,
            bool attachPathContext,
            double downstreamEntryArrive = -1)
        {
            if (!JunctionSubTaskTiming.TryResolveWindows(
                    seg,
                    nextSeg,
                    _topology,
                    _mapSource?.ConveyorMap,
                    hop,
                    out var enterStart,
                    out var enterEnd,
                    out var waitStart,
                    out var waitEnd,
                    out var exitMoveStart,
                    out var exitEnd))
            {
                return;
            }

            if (downstreamEntryArrive > exitEnd + 1e-9)
            {
                exitEnd = downstreamEntryArrive;
            }

            var pathAttached = false;
            void RecordPhase(SimSubTaskKind kind, double start, double end)
            {
                if (end <= start + 1e-9)
                {
                    return;
                }

                var attach = attachPathContext && !pathAttached;
                _record(
                    job,
                    kind,
                    start,
                    end,
                    job.AssignedStackerId,
                    job.TargetSlot,
                    seg.FromNodeIndex,
                    seg.ToNodeIndex,
                    0,
                    attach);
                pathAttached |= attach;
            }

            RecordPhase(SimSubTaskKind.JunctionEnter, enterStart, enterEnd);
            var s0Arrive = seg.StopArriveSimTimes[0];
            if (JunctionSubTaskTiming.HasJunctionWait(s0Arrive, enterEnd, exitMoveStart, hop))
            {
                RecordPhase(SimSubTaskKind.JunctionWait, waitStart, waitEnd);
            }

            var exitSubStart = JunctionSubTaskTiming.GetExitSubTaskStart(s0Arrive, enterEnd, exitMoveStart, hop);
            RecordPhase(SimSubTaskKind.JunctionExit, exitSubStart, exitEnd);
        }

    }
}
