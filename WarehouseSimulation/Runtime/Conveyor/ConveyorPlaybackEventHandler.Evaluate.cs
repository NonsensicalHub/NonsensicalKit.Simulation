using System;
using System.Collections.Generic;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback;
namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    public sealed partial class ConveyorPlaybackEventHandler
    {
        private void EvaluateJob(int jobId, double simTime, IReadOnlyList<SimSubTask> subTasks)
        {
            if (!_subTaskIndex.TryGetJobTasks(jobId, out var jobTasks))
            {
                m_cargoRegistry?.Release(jobId);
                return;
            }

            if (!_subTaskIndex.HasStarted(jobId, simTime))
            {
                m_cargoRegistry?.Release(jobId);
                return;
            }

            if (_subTaskIndex.TryGetActive(jobId, simTime, out var active))
            {
                var isOutbound = SimSubTaskQuery.IsOutboundJob(jobTasks);
                switch (active.Kind)
                {
                    case SimSubTaskKind.InfeedPlace:
                        ShowAtInfeed(jobId, active.InfeedPortIndex);
                        return;
                    case SimSubTaskKind.StackerWait:
                        if (isOutbound)
                        {
                            DetachCargo(jobId);
                        }
                        else
                        {
                            SnapCargoToPickup(jobId, active.PickupPointIndex);
                        }
                        return;
                    case SimSubTaskKind.StackerApproach:
                        if (isOutbound)
                        {
                            DetachCargo(jobId);
                        }
                        else
                        {
                            SnapCargoToPickup(jobId, active.PickupPointIndex);
                        }
                        return;
                    case SimSubTaskKind.StackerPick:
                        if (SimSubTaskKinds.IsStackerForkCargoWindow(active, simTime))
                        {
                            return;
                        }

                        if (isOutbound)
                        {
                            DetachCargo(jobId);
                        }
                        else
                        {
                            SnapCargoToPickup(jobId, active.PickupPointIndex);
                        }
                        return;
                    case SimSubTaskKind.StackerMove:
                        if (SimSubTaskKinds.IsStackerForkCargoWindow(active, simTime))
                        {
                            return;
                        }

                        DetachCargo(jobId);
                        return;
                    case SimSubTaskKind.StackerPlace:
                        if (SimSubTaskKinds.IsStackerForkCargoWindow(active, simTime))
                        {
                            return;
                        }

                        if (isOutbound)
                        {
                            SnapCargoToPickup(jobId, active.PickupPointIndex);
                        }
                        else
                        {
                            m_cargoRegistry?.Release(jobId);
                        }
                        return;
                    case SimSubTaskKind.OutfeedService:
                        SnapCargoToOutfeed(jobId, active.OutfeedPortIndex);
                        return;
                    case SimSubTaskKind.ProcessStationService:
                        SnapCargoToProcessStation(jobId, active.ToNodeIndex);
                        return;
                    case SimSubTaskKind.VerticalTransferMove:
                        if (TryPlaceCargoFromSubTaskMotion(jobId, simTime, active, subTasks, out var transferPos))
                        {
                            PlaceCargo(jobId, transferPos);
                            return;
                        }

                        SnapCargoToNode(jobId, LogicalIdAt(active.ToNodeIndex));
                        return;
                    case SimSubTaskKind.Completed:
                        m_cargoRegistry?.Release(jobId);
                        return;
                    default:
                        if (TryPlaceCargoFromSubTaskMotion(jobId, simTime, active, subTasks, out var subTaskPos))
                        {
                            PlaceCargo(jobId, subTaskPos);
                            return;
                        }

                        if (SimSubTaskKinds.UsesConveyorPlacement(active.Kind))
                        {
                            PlaceConveyorCargo(jobId, simTime, active, subTasks);
                        }
                        return;
                }
            }
            if (TryResolveJobSchedule(subTasks, jobId, out var schedule)
                && ConveyorPlaybackJobTimeline.IsOnConveyor(schedule, jobTasks, simTime)
                && TryPlaceCargoOnConveyor(jobId, simTime, subTasks, out var conveyorPos))
            {
                PlaceCargo(jobId, conveyorPos);
                return;
            }

            if (ConveyorPlaybackJobTimeline.IsWaitingAtPickup(jobTasks, simTime, out var pickupIndex))
            {
                SnapCargoToPickup(jobId, pickupIndex);
                return;
            }

            if (TryResolveJobSchedule(subTasks, jobId, out var outboundSchedule)
                && ConveyorPlaybackJobTimeline.IsWaitingAtOutboundPickup(
                    jobTasks,
                    outboundSchedule,
                    simTime,
                    out pickupIndex))
            {
                SnapCargoToPickup(jobId, pickupIndex);
                return;
            }

            if (TryResolveJobSchedule(subTasks, jobId, out outboundSchedule)
                && ConveyorPlaybackJobTimeline.IsWaitingAtOutfeed(
                    outboundSchedule,
                    jobTasks,
                    simTime,
                    out var outfeedNodeIndex)
                && TryGetAnchor(outfeedNodeIndex, out var outfeedTf))
            {
                PlaceCargo(jobId, outfeedTf.position);
                return;
            }

            if (ConveyorPlaybackJobTimeline.HasCompletedPlacement(jobTasks, simTime))
            {
                m_cargoRegistry?.Release(jobId);
            }
        }

        private bool TryEvaluateConveyorPosition(
            int jobId,
            double simTime,
            IReadOnlyList<SimSubTask> subTasks,
            out Vector3 position,
            in SimSubTask? contextTask = null)
        {
            position = Vector3.zero;
            if (!TryResolveJobSchedule(subTasks, jobId, out var schedule))
            {
                return false;
            }
            var tail = schedule[^1];
            if (simTime >= tail.OccupancyEndSimTime - 1e-9)
            {
                if (!TryGetAnchor(tail.ToNodeIndex, out var pastEndTf))
                {
                    return false;
                }
                position = pastEndTf.position;
                return true;
            }
            if (contextTask.HasValue)
            {
                var task = contextTask.Value;
                var segIndex = FindSegmentIndexForSubTask(schedule, task);
                if (task.Kind == SimSubTaskKind.SegmentQueue && segIndex > 0)
                {
                    return TryEvaluateScheduleSegment(
                        schedule,
                        segIndex - 1,
                        simTime,
                        subTasks,
                        jobId,
                        task,
                        out position);
                }
                return TryEvaluateScheduleSegment(
                    schedule,
                    segIndex,
                    simTime,
                    subTasks,
                    jobId,
                    task,
                    out position);
            }
            var timeSegIndex = FindSegmentIndexForSimTime(schedule, simTime);
            return TryEvaluateScheduleSegment(
                schedule,
                timeSegIndex,
                simTime,
                subTasks,
                jobId,
                null,
                out position);
        }
        private bool TryEvaluateScheduleSegment(
            ConveyorSegmentScheduleEntry[] schedule,
            int segIndex,
            double simTime,
            IReadOnlyList<SimSubTask> subTasks,
            int jobId,
            in SimSubTask? contextTask,
            out Vector3 position)
        {
            position = Vector3.zero;
            if (segIndex < 0 || segIndex >= schedule.Length)
            {
                return false;
            }

            if (!_subTaskIndex.TryGetJobTasks(jobId, out var jobTasks))
            {
                jobTasks = System.Array.Empty<SimSubTask>();
            }

            var map = Bindings.ConveyorMap;
            var seg = schedule[segIndex];
            if (!TryGetAnchor(seg.FromNodeIndex, out var fromTf)
                || !TryGetAnchor(seg.ToNodeIndex, out var toTf))
            {
                return false;
            }
            var fromWorld = fromTf.position;
            var toWorld = toTf.position;
            Vector3? infeedAnchor = null;
            if (contextTask.HasValue && contextTask.Value.InfeedPortIndex >= 0
                && TryGetInfeedWorld(contextTask.Value.InfeedPortIndex, out var infeedWorld)
                && (contextTask.Value.Kind == SimSubTaskKind.InfeedMove
                    || (contextTask.Value.Kind == SimSubTaskKind.SegmentQueue
                        && segIndex == 0
                        && simTime < seg.EntrySimTime - 1e-9)))
            {
                fromWorld = infeedWorld;
                infeedAnchor = infeedWorld;
            }
            else if (segIndex == 0
                     && TryResolveInfeedPortIndex(jobTasks, contextTask, out var portIndex)
                     && TryGetInfeedWorld(portIndex, out var portWorld))
            {
                infeedAnchor = portWorld;
            }
            ConveyorSegmentScheduleEntry? prevSeg = null;
            Vector3? prevFrom = null;
            Vector3? prevTo = null;
            if (segIndex > 0)
            {
                prevSeg = schedule[segIndex - 1];
                if (TryGetAnchor(prevSeg.Value.FromNodeIndex, out var prevFromTf)
                    && TryGetAnchor(prevSeg.Value.ToNodeIndex, out var prevToTf))
                {
                    prevFrom = prevFromTf.position;
                    prevTo = prevToTf.position;
                }
            }
            ConveyorSegmentScheduleEntry? nextSeg = null;
            Vector3? nextFrom = null;
            Vector3? nextTo = null;
            if (segIndex + 1 < schedule.Length)
            {
                nextSeg = schedule[segIndex + 1];
                if (TryGetAnchor(nextSeg.Value.FromNodeIndex, out var nextFromTf)
                    && TryGetAnchor(nextSeg.Value.ToNodeIndex, out var nextToTf))
                {
                    nextFrom = nextFromTf.position;
                    nextTo = nextToTf.position;
                }
            }
            return ConveyorPlaybackPosition.TryEvaluate(
                map,
                _topology,
                fromWorld,
                toWorld,
                seg,
                simTime,
                prevSeg,
                prevFrom,
                prevTo,
                nextSeg,
                nextFrom,
                nextTo,
                out position,
                BuildInfeedDepartHints(jobTasks, infeedAnchor));
        }
        private static bool TryResolveInfeedPortIndex(
            IReadOnlyList<SimSubTask> jobTasks,
            in SimSubTask? contextTask,
            out int portIndex)
        {
            portIndex = contextTask.HasValue ? contextTask.Value.InfeedPortIndex : -1;
            if (portIndex >= 0)
            {
                return true;
            }

            for (var i = 0; i < jobTasks.Count; i++)
            {
                if (jobTasks[i].InfeedPortIndex >= 0)
                {
                    portIndex = jobTasks[i].InfeedPortIndex;
                    return true;
                }
            }

            return false;
        }

        private static InfeedDepartPlaybackHints? BuildInfeedDepartHints(
            IReadOnlyList<SimSubTask> jobTasks,
            Vector3? anchorWorld)
        {
            for (var i = 0; i < jobTasks.Count; i++)
            {
                var task = jobTasks[i];
                if (task.Kind != SimSubTaskKind.InfeedMove)
                {
                    continue;
                }

                return new InfeedDepartPlaybackHints(
                    task.StartSimTime,
                    task.EndSimTime,
                    anchorWorld);
            }

            return null;
        }
        private bool TryPlaceCargoOnConveyor(
            int jobId,
            double simTime,
            IReadOnlyList<SimSubTask> subTasks,
            out Vector3 position)
        {
            return TryEvaluateConveyorPosition(jobId, simTime, subTasks, out position);
        }
        private void PlaceConveyorCargo(
            int jobId,
            double simTime,
            in SimSubTask active,
            IReadOnlyList<SimSubTask> subTasks)
        {
            if (TryPlaceCargoFromSubTaskMotion(jobId, simTime, active, subTasks, out var pos)
                || TryEvaluateConveyorPosition(jobId, simTime, subTasks, out pos, active))
            {
                PlaceCargo(jobId, pos);
                return;
            }

            HoldLastConveyorOrInfeed(jobId, simTime, active, subTasks);
        }
        private void HoldLastConveyorOrInfeed(
            int jobId,
            double simTime,
            in SimSubTask active,
            IReadOnlyList<SimSubTask> subTasks)
        {
            if (TryEvaluateConveyorPosition(jobId, simTime, subTasks, out var pos, active)
                || TryPlaceCargoFromSubTaskMotion(jobId, simTime, active, subTasks, out pos))
            {
                PlaceCargo(jobId, pos);
                return;
            }
            if (active.InfeedPortIndex >= 0 && SimSubTaskKinds.CanHoldAtInfeed(active.Kind))
            {
                ShowAtInfeed(jobId, active.InfeedPortIndex);
            }
        }
        private void RebuildScheduleCache(IReadOnlyList<SimSubTask> subTasks)
        {
            _scheduleByJob.Clear();
            var events = m_eventSource != null ? m_eventSource.Events : null;
            if (events == null && m_playback != null)
            {
                events = m_playback.EventSource?.Events;
            }
            if (events != null)
            {
                for (var i = 0; i < events.Count; i++)
                {
                    var evt = events[i];
                    if (evt.Phase != SimPlaybackPhase.ConveyorRouted
                        || evt.SegmentSchedule == null
                        || evt.SegmentSchedule.Length == 0)
                    {
                        continue;
                    }
                    StoreScheduleSnapshot(evt.JobId, evt.SegmentSchedule);
                }
            }
            if (subTasks == null)
            {
                return;
            }
            for (var i = 0; i < subTasks.Count; i++)
            {
                var schedule = subTasks[i].SegmentSchedule;
                if (schedule == null || schedule.Length == 0)
                {
                    continue;
                }
                StoreScheduleSnapshot(subTasks[i].JobId, schedule);
            }
        }

        private void StoreScheduleSnapshot(int jobId, ConveyorSegmentScheduleEntry[] schedule)
        {
            if (schedule == null || schedule.Length == 0)
            {
                return;
            }

            if (!_scheduleByJob.TryGetValue(jobId, out var existing)
                || existing == null
                || existing.Length == 0
                || schedule.Length > existing.Length
                || schedule[^1].OccupancyEndSimTime > existing[^1].OccupancyEndSimTime + 1e-9)
            {
                _scheduleByJob[jobId] = schedule;
            }
        }
        private bool TryResolveJobSchedule(
            IReadOnlyList<SimSubTask> subTasks,
            int jobId,
            out ConveyorSegmentScheduleEntry[] schedule)
        {
            schedule = null;
            if (_scheduleByJob.TryGetValue(jobId, out schedule)
                && schedule != null
                && schedule.Length > 0)
            {
                return true;
            }
            return _subTaskIndex.TryGetJobContext(jobId, out _, out schedule)
                   && schedule != null
                   && schedule.Length > 0;
        }
        private static int FindSegmentIndexForSimTime(
            ConveyorSegmentScheduleEntry[] schedule,
            double simTime)
        {
            for (var i = schedule.Length - 1; i >= 0; i--)
            {
                if (simTime + 1e-9 >= schedule[i].DesiredEntrySimTime
                    && simTime < schedule[i].OccupancyEndSimTime + 1e-9)
                {
                    return i;
                }
            }
            var fallback = 0;
            for (var i = 0; i < schedule.Length; i++)
            {
                if (simTime + 1e-9 >= schedule[i].DesiredEntrySimTime)
                {
                    fallback = i;
                }
            }
            return fallback;
        }
        private static int FindSegmentIndexForSubTask(
            ConveyorSegmentScheduleEntry[] schedule,
            in SimSubTask task)
        {
            if (task.FromNodeIndex >= 0 && task.ToNodeIndex >= 0)
            {
                for (var i = 0; i < schedule.Length; i++)
                {
                    if (schedule[i].FromNodeIndex == task.FromNodeIndex
                        && schedule[i].ToNodeIndex == task.ToNodeIndex)
                    {
                        return i;
                    }
                }
            }
            return FindSegmentIndexForSimTime(schedule, task.StartSimTime);
        }
        private bool TryGetInfeedWorld(int infeedPortIndex, out Vector3 world)
        {
            world = Vector3.zero;
            var nodeId = ResolveInfeedNodeId(infeedPortIndex);
            if (string.IsNullOrEmpty(nodeId) || !_anchorIndex.TryGet(nodeId, out var tf))
            {
                return false;
            }
            world = tf.position;
            return true;
        }
        /// <summary>
        /// 按子任务在路段停留点之间的运动插值（与任务报呁S0/S1 一致，不用节点锚点直连）。
        /// </summary>
        private bool TryPlaceCargoFromSubTaskMotion(
            int jobId,
            double simTime,
            in SimSubTask task,
            IReadOnlyList<SimSubTask> subTasks,
            out Vector3 position)
        {
            position = Vector3.zero;
            if (!TryResolveJobSchedule(subTasks, jobId, out var schedule))
            {
                return false;
            }
            var segIndex = FindSegmentIndexForSubTask(schedule, task);
            if (task.Kind == SimSubTaskKind.SegmentQueue && segIndex > 0)
            {
                segIndex -= 1;
            }
            var seg = schedule[segIndex];
            if (!TryGetAnchor(seg.FromNodeIndex, out var fromTf)
                || !TryGetAnchor(seg.ToNodeIndex, out var toTf))
            {
                return false;
            }
            var fromWorld = fromTf.position;
            var toWorld = toTf.position;
            if (!_subTaskIndex.TryGetJobTasks(jobId, out var jobTasks))
            {
                jobTasks = System.Array.Empty<SimSubTask>();
            }

            if (task.InfeedPortIndex >= 0
                && TryGetInfeedWorld(task.InfeedPortIndex, out var infeedWorld)
                && (task.Kind == SimSubTaskKind.InfeedMove
                    || (task.Kind == SimSubTaskKind.SegmentQueue && segIndex == 0)))
            {
                fromWorld = infeedWorld;
            }
            else if (task.PickupPointIndex >= 0
                     && SimSubTaskQuery.IsOutboundJob(jobTasks)
                     && TryGetAnchor(task.PickupPointIndex, out var pickupTf)
                     && (task.Kind == SimSubTaskKind.OutboundMove
                         || (task.Kind == SimSubTaskKind.SegmentQueue
                             && segIndex == 0
                             && task.SegmentSlotIndex < 0)))
            {
                fromWorld = pickupTf.position;
            }
            var stops = seg.StopArriveSimTimes;
            var capacity = stops?.Length ?? 0;
            var progress = task.NormalizedProgress(simTime);

            switch (task.Kind)
            {
                case SimSubTaskKind.InfeedMove:
                {
                    var slot = task.SegmentSlotIndex >= 0 ? task.SegmentSlotIndex : Math.Max(0, capacity - 1);
                    if (capacity <= 0)
                    {
                        position = Vector3.Lerp(fromWorld, toWorld, progress);
                        return true;
                    }
                    var toT = ConveyorMapMath.GetZoneNormalizedPosition(capacity, slot);
                    var stopWorld = Vector3.Lerp(fromTf.position, toTf.position, toT);
                    position = Vector3.Lerp(fromWorld, stopWorld, progress);
                    return true;
                }
                case SimSubTaskKind.OutboundMove:
                {
                    var slot = task.SegmentSlotIndex >= 0 ? task.SegmentSlotIndex : Math.Max(0, capacity - 1);
                    if (capacity <= 0)
                    {
                        position = Vector3.Lerp(fromWorld, toWorld, progress);
                        return true;
                    }
                    var toT = ConveyorMapMath.GetZoneNormalizedPosition(capacity, slot);
                    var stopWorld = Vector3.Lerp(fromTf.position, toTf.position, toT);
                    position = Vector3.Lerp(fromWorld, stopWorld, progress);
                    return true;
                }
                case SimSubTaskKind.SegmentHopMove:
                {
                    if (capacity <= 0)
                    {
                        position = Vector3.Lerp(fromWorld, toWorld, progress);
                        return true;
                    }
                    if (SimSubTaskKinds.IsPickupArrivalHop(task))
                    {
                        var fromTT = ConveyorMapMath.GetZoneNormalizedPosition(capacity, 0);
                        var fromPos = Vector3.Lerp(fromWorld, toWorld, fromTT);
                        position = Vector3.Lerp(fromPos, toWorld, progress);
                        return true;
                    }
                    var toSlot = task.SegmentSlotIndex;
                    if (toSlot < 0 || toSlot >= capacity)
                    {
                        return false;
                    }
                    var fromSlot = toSlot + 1;
                    var fromT = fromSlot < capacity
                        ? ConveyorMapMath.GetZoneNormalizedPosition(capacity, fromSlot)
                        : 0f;
                    var toT = ConveyorMapMath.GetZoneNormalizedPosition(capacity, toSlot);
                    position = Vector3.Lerp(fromWorld, toWorld, Mathf.Lerp(fromT, toT, progress));
                    return true;
                }
                case SimSubTaskKind.SegmentStopDwell:
                {
                    if (capacity <= 0 || task.SegmentSlotIndex < 0)
                    {
                        return false;
                    }
                    var t = ConveyorMapMath.GetZoneNormalizedPosition(capacity, task.SegmentSlotIndex);
                    position = Vector3.Lerp(fromWorld, toWorld, t);
                    return true;
                }
                case SimSubTaskKind.SegmentTransit:
                    position = Vector3.Lerp(fromWorld, toWorld, progress);
                    return true;
                case SimSubTaskKind.VerticalTransferMove:
                {
                    if (!TryGetAnchor(task.ToNodeIndex, out var transferTf))
                    {
                        return false;
                    }

                    var entryPos = transferTf.position;
                    if (!TryResolveVerticalTransferTargetWorld(task, out var targetPos))
                    {
                        targetPos = entryPos;
                    }

                    ref var transferNode = ref _topology.GetNode(task.ToNodeIndex);
                    position = ConveyorVerticalTransferPlayback.Sample(
                        in transferNode,
                        entryPos,
                        targetPos,
                        progress);
                    return true;
                }
                case SimSubTaskKind.SegmentQueue:
                    if (SimSubTaskKinds.IsOutboundPickupQueue(task))
                    {
                        if (TryGetAnchor(task.PickupPointIndex, out var pickupTf))
                        {
                            position = pickupTf.position;
                            return true;
                        }
                    }

                    if (segIndex == 0)
                    {
                        if (TryResolveInfeedPortIndex(jobTasks, task, out var portIndex)
                            && TryGetInfeedWorld(portIndex, out var infeedPos))
                        {
                            position = infeedPos;
                            return true;
                        }
                    }

                    if (capacity > 0)
                    {
                        var waitSlot = task.SegmentSlotIndex >= 0 ? task.SegmentSlotIndex : capacity - 1;
                        if (waitSlot >= capacity)
                        {
                            waitSlot = capacity - 1;
                        }

                        var t = ConveyorMapMath.GetZoneNormalizedPosition(capacity, waitSlot);
                        position = Vector3.Lerp(fromWorld, toWorld, t);
                        return true;
                    }
                    position = fromWorld;
                    return true;
                default:
                    if (!SimSubTaskKinds.IsJunction(task.Kind))
                    {
                        return false;
                    }
                    return TryEvaluateScheduleSegment(
                        schedule,
                        segIndex,
                        simTime,
                        subTasks,
                        jobId,
                        task,
                        out position);
            }
            return false;
        }

        private bool TryResolveVerticalTransferTargetWorld(in SimSubTask task, out Vector3 targetWorld)
        {
            targetWorld = Vector3.zero;
            ref var node = ref _topology.GetNode(task.ToNodeIndex);
            if (ConveyorVerticalTransferUtility.TryResolveTargetNodeIndex(
                    _topology,
                    in node,
                    task.ToNodeIndex,
                    out var targetNodeIndex)
                && TryGetAnchor(targetNodeIndex, out var targetTf))
            {
                targetWorld = targetTf.position;
                return true;
            }

            var path = task.PathNodeIndices;
            if (path != null)
            {
                for (var i = 0; i < path.Length - 1; i++)
                {
                    if (path[i] != task.ToNodeIndex)
                    {
                        continue;
                    }

                    var nextNode = path[i + 1];
                    if (TryGetAnchor(nextNode, out var nextTf))
                    {
                        targetWorld = nextTf.position;
                        return true;
                    }

                    break;
                }
            }

            return false;
        }
    }
}
