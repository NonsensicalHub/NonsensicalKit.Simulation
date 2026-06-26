using System;
using System.Collections.Generic;
using System.Linq;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <content>
    /// 任务收尾、回放/子任务记录、按资源汇总等待时间，以及仿真后的占用与时间轴自检。
    /// </content>
    public sealed partial class StackerWarehouseSimulator
    {
        #region 任务完成

        internal void RecordFailure(SimJobFailureReason reason, int count = 1)
        {
            if (count <= 0)
            {
                return;
            }

            if (!_failureCounts.ContainsKey(reason))
            {
                _failureCounts[reason] = 0;
            }

            _failureCounts[reason] += count;
        }

        private void FinalizeFailureCounts(int queuePending)
        {
            var accounted = _completedCount + _failedNoSlot + _failedNoCargo;
            var incomplete = Math.Max(0, _targetCount - accounted);
            if (incomplete <= 0)
            {
                return;
            }

            if (_eventCount >= _bindings?.MaxSimEvents)
            {
                return;
            }

            RecordFailure(SimJobFailureReason.SimIncompleteRemaining, incomplete);
        }

        private int CountIncompleteJobs() =>
            Math.Max(0, _targetCount - _completedCount - _failedNoSlot - _failedNoCargo);

        /// <summary>写入完成记录并发出 Completed 回放事件。</summary>
        private void OnJobCompleted(WarehouseJob job)
        {
            _completedCount++;
            if (job.Direction != SimFlowDirection.Outbound)
            {
                DecrementStackerActiveJobCount(job.AssignedStackerId);
            }
            else
            {
                ClearOutboundPickupHold(job);
            }

            job.State = WarehouseJobState.Completed;
            var endTime = _clock.Now;
            var duration = endTime - job.ArrivalTime;
            var record = new JobCompletionRecord
            {
                JobId = job.JobId,
                Direction = job.Direction,
                CompletedAt = endTime,
                Duration = duration,
                ServiceTime = job.ServiceTimeAccum,
                WaitTime = job.WaitTimeAccum,
                BottleneckResource = job.LastWaitResource,
                Slot = job.TargetSlot,
                StackerId = job.AssignedStackerId,
                InfeedPortIndex = job.InfeedPortIndex,
                OutfeedPortIndex = job.OutfeedPortIndex,
                PickupPointIndex = job.PickupPointIndex,
            };
            _completions.Add(record);
            RecordPlayback(job, SimPlaybackPhase.Completed, job.AssignedStackerId, job.TargetSlot);
            RecordSubTask(
                job,
                SimSubTaskKind.Completed,
                endTime,
                endTime,
                job.AssignedStackerId,
                job.TargetSlot);
            WarehouseSimLog.Info(() =>
                $"任务完成 job={job.JobId} 耗时={duration:F1}s 服务={job.ServiceTimeAccum:F1}s " +
                $"等待={job.WaitTimeAccum:F1}s slot={job.TargetSlot}");
        }

        #endregion

        #region 回放与子任务记录

        private static void CopyConveyorContext(
            WarehouseJob job,
            bool attachPathContext,
            out int[] pathCopy,
            out ConveyorSegmentScheduleEntry[] scheduleCopy)
        {
            pathCopy = null;
            scheduleCopy = null;
            if (!attachPathContext || job == null)
            {
                return;
            }

            EnsurePlaybackSnapshot(job);
            pathCopy = job.PlaybackPathSnapshot;
            scheduleCopy = job.PlaybackScheduleSnapshot;
        }

        private static void EnsurePlaybackSnapshot(WarehouseJob job)
        {
            if (job.ConveyorPathNodeIndices != null
                && job.ConveyorPathNodeIndices.Count > 0
                && (job.PlaybackPathSnapshot == null
                    || job.PlaybackPathSnapshot.Length < job.ConveyorPathNodeIndices.Count))
            {
                job.PlaybackPathSnapshot = job.ConveyorPathNodeIndices.ToArray();
            }

            if (job.ConveyorSegmentSchedule == null || job.ConveyorSegmentSchedule.Count == 0)
            {
                return;
            }

            var scheduleCount = job.ConveyorSegmentSchedule.Count;
            if (job.PlaybackScheduleSnapshot != null
                && job.PlaybackScheduleSnapshot.Length >= scheduleCount)
            {
                return;
            }

            job.PlaybackScheduleSnapshot = job.ConveyorSegmentSchedule.ToArray();
        }

        /// <summary>追加一条回放事件（含当前输送路径副本）。</summary>
        private void RecordPlayback(
            WarehouseJob job,
            SimPlaybackPhase phase,
            int stackerId,
            GridIndex slot) =>
            RecordPlaybackAt(_clock.Now, job, phase, stackerId, slot);

        private void RecordPlaybackAt(
            double simTime,
            WarehouseJob job,
            SimPlaybackPhase phase,
            int stackerId,
            GridIndex slot,
            bool attachPathContext = false)
        {
            if (!_recordPlayback)
            {
                return;
            }

            CopyConveyorContext(job, attachPathContext, out var pathCopy, out var scheduleCopy);

            _playback.Add(new SimPlaybackEvent
            {
                SimTime = simTime,
                JobId = job.JobId,
                Phase = phase,
                StackerId = stackerId,
                Slot = slot,
                InfeedPortIndex = job.InfeedPortIndex,
                PickupPointIndex = job.PickupPointIndex,
                PathNodeIndices = pathCopy,
                SegmentSchedule = scheduleCopy,
            });
        }

        private void SortPlaybackEvents()
        {
            _playback.Sort((a, b) =>
            {
                var timeCompare = a.SimTime.CompareTo(b.SimTime);
                if (timeCompare != 0)
                {
                    return timeCompare;
                }

                var phaseCompare = a.Phase.CompareTo(b.Phase);
                return phaseCompare != 0 ? phaseCompare : a.JobId.CompareTo(b.JobId);
            });
        }

        private void SortSubTasks()
        {
            _subTasks.Sort((a, b) =>
            {
                var startCompare = a.StartSimTime.CompareTo(b.StartSimTime);
                if (startCompare != 0)
                {
                    return startCompare;
                }

                var kindCompare = a.Kind.CompareTo(b.Kind);
                return kindCompare != 0 ? kindCompare : a.JobId.CompareTo(b.JobId);
            });
        }

        #endregion

        #region 统计与诊断

        /// <summary>超过此子任务数时跳过仿真后自检，避免大批量入库时墙钟耗时过长。</summary>
        private const int PostSimulationSelfCheckSubTaskLimit = 15000;

        private void RunPostSimulationSelfChecks(SimRunResult result)
        {
            if (_subTasks.Count > PostSimulationSelfCheckSubTaskLimit)
            {
                WarehouseSimLog.Warn(
                    $"子任务 {_subTasks.Count} 条，已超过自检上限 {PostSimulationSelfCheckSubTaskLimit}，跳过仿真后自检。");
                return;
            }

            RunOccupancySelfCheck(result);
            RunSubTaskTimelineSelfCheck(result);
        }

        private void RunOccupancySelfCheck(SimRunResult result)
        {
            if (result == null || _conveyorTopology == null || _bindings?.ConveyorMap == null)
            {
                return;
            }

            SimOccupancyConflictChecker.CheckResult check;
            using (_wallClockProfiler.Begin(SimWallClockProfilePhases.PostOccupancyCheck))
            {
                check = SimOccupancyConflictChecker.Run(
                    _subTasks,
                    _bindings.ConveyorMap,
                    _conveyorTopology,
                    result.TotalSimSeconds,
                    _bindings);
            }
            result.OccupancyConflicts = check.Conflicts;
            result.OccupancyConflictReportText = check.Conflicts.Count > 0 ? check.FullReportText : null;
            if (result.OccupancyConflicts == null || result.OccupancyConflicts.Count == 0)
            {
                return;
            }

            WarehouseSimLog.Error(
                $"仿真后自检发现 {result.OccupancyConflicts.Count} 处节点占用冲突（导出调试 Markdown 报告查看明细）。");
            MarkSelfCheckFailed(
                result,
                $"自检：节点占用冲突 {result.OccupancyConflicts.Count} 处。");
        }

        private void RunSubTaskTimelineSelfCheck(SimRunResult result)
        {
            if (result == null)
            {
                return;
            }

            SimSubTaskTimelineChecker.CheckResult check;
            using (_wallClockProfiler.Begin(SimWallClockProfilePhases.PostTimelineCheck))
            {
                check = SimSubTaskTimelineChecker.Run(_subTasks);
            }
            result.SubTaskTimelineIssues = check.Issues;
            if (result.SubTaskTimelineIssues == null || result.SubTaskTimelineIssues.Count == 0)
            {
                return;
            }

            SimSubTaskTimelineChecker.CountIssuesByKind(
                result.SubTaskTimelineIssues,
                out var overlap,
                out var gap);

            WarehouseSimLog.Error(
                $"仿真后自检发现 {result.SubTaskTimelineIssues.Count} 处子任务时间轴问题（重叠 {overlap}，空白 {gap}；导出调试 Markdown 报告查看明细）。");
            MarkSelfCheckFailed(
                result,
                $"自检：子任务时间轴问题 {result.SubTaskTimelineIssues.Count} 处（重叠 {overlap}，空白 {gap}）。");
        }

        private static void MarkSelfCheckFailed(SimRunResult result, string fragment)
        {
            result.Success = false;
            result.Message = string.IsNullOrEmpty(result.Message)
                ? fragment
                : $"{result.Message} {fragment}";
        }

        private void RecordSubTask(
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
            bool hasStackerPose = false)
        {
            if (!_recordPlayback)
            {
                return;
            }

            using (_wallClockProfiler.Begin(SimWallClockProfilePhases.RecordSubTaskPhase(kind)))
            {
                CopyConveyorContext(job, attachPathContext, out var pathCopy, out var scheduleCopy);

                _subTasks.Add(new SimSubTask
                {
                    SubTaskId = _nextSubTaskId++,
                    JobId = job.JobId,
                    Kind = kind,
                    StartSimTime = startSimTime,
                    EndSimTime = endSimTime,
                    StackerId = stackerId,
                    Slot = slot,
                    InfeedPortIndex = job.InfeedPortIndex,
                    OutfeedPortIndex = job.OutfeedPortIndex,
                    PickupPointIndex = job.PickupPointIndex,
                    FromNodeIndex = fromNodeIndex,
                    ToNodeIndex = toNodeIndex,
                    SegmentSlotIndex = segmentSlotIndex,
                    StackerFromSlot = stackerFromSlot,
                    StackerToSlot = stackerToSlot,
                    StackerRailColumn = stackerRailColumn,
                    HasStackerPose = hasStackerPose,
                    PathNodeIndices = pathCopy,
                    SegmentSchedule = scheduleCopy,
                });
            }
        }

        private Dictionary<string, double> BuildWaitTotals()
        {
            var dict = new Dictionary<string, double>();
            foreach (var c in _completions)
            {
                if (string.IsNullOrEmpty(c.BottleneckResource))
                {
                    continue;
                }

                if (!dict.ContainsKey(c.BottleneckResource))
                {
                    dict[c.BottleneckResource] = 0;
                }

                dict[c.BottleneckResource] += c.WaitTime;
            }

            return dict;
        }

        /// <summary>事件上限或队列耗尽时输出诊断：类型分布、任务状态、资源占用。</summary>
        internal SimEventDiagnostics BuildEventDiagnosticsSnapshot(int queuePending)
        {
            var diag = new SimEventDiagnostics
            {
                DispatchedEventCount = _eventCount,
                QueuePendingCount = queuePending,
                MaxSimEvents = _bindings?.MaxSimEvents ?? 0,
                SimTimeSeconds = _clock.Now,
                CompletedJobCount = _completedCount,
                FailedJobCount = _failedNoSlot + _failedNoCargo,
                TargetJobCount = _targetCount,
            };

            foreach (var entry in _eventTypeCounts
                         .Select((count, index) => (Type: (SimEventType)index, Count: count))
                         .Where(x => x.Count > 0)
                         .OrderByDescending(x => x.Count))
            {
                var pct = _eventCount > 0 ? entry.Count * 100.0 / _eventCount : 0;
                diag.EventTypeStats.Add(new SimEventTypeStat
                {
                    Type = entry.Type,
                    Count = entry.Count,
                    Percent = pct,
                });
            }

            var stateCounts = new Dictionary<WarehouseJobState, int>();
            foreach (var job in _jobs.Values)
            {
                if (job == null)
                {
                    continue;
                }

                if (!stateCounts.ContainsKey(job.State))
                {
                    stateCounts[job.State] = 0;
                }

                stateCounts[job.State]++;
            }

            foreach (var kv in stateCounts.OrderByDescending(x => x.Value))
            {
                diag.JobStateStats.Add(new SimJobStateStat
                {
                    State = kv.Key,
                    Count = kv.Value,
                });
            }

            if (_infeedReservationCounts != null)
            {
                for (var i = 0; i < _infeedReservationCounts.Length; i++)
                {
                    diag.InfeedReservations.Add(new SimNamedCount
                    {
                        Name = $"port{i}",
                        Count = _infeedReservationCounts[i],
                    });
                }
            }

            if (_outfeedReservationCounts != null)
            {
                for (var i = 0; i < _outfeedReservationCounts.Length; i++)
                {
                    diag.OutfeedReservations.Add(new SimNamedCount
                    {
                        Name = $"port{i}",
                        Count = _outfeedReservationCounts[i],
                    });
                }
            }

            if (_pickupReservationCounts != null
                && _conveyorTopology?.PickupNodeIndices != null)
            {
                foreach (var pickupIndex in _conveyorTopology.PickupNodeIndices)
                {
                    if (pickupIndex < 0 || pickupIndex >= _pickupReservationCounts.Length)
                    {
                        continue;
                    }

                    var count = _pickupReservationCounts[pickupIndex];
                    if (count <= 0)
                    {
                        continue;
                    }

                    ref var node = ref _conveyorTopology.GetNode(pickupIndex);
                    var label = SimEntityNaming.FormatLogicalId(in node, pickupIndex);
                    if (label == "—")
                    {
                        label = $"pickup-{pickupIndex}";
                    }

                    diag.PickupReservations.Add(new SimNamedCount
                    {
                        Name = label,
                        Count = count,
                    });
                }
            }

            if (_slotAllocator.Occupied != null)
            {
                diag.TotalSlotCount = _slotAllocator.StorageSlotCount;
                diag.OccupiedSlotCount = _slotAllocator.CountOccupiedStorageSlots(_slotAllocator.Occupied);
                diag.AllocatableStorageSlotCount = _slotAllocator.CountAllocatableStorageSlots(
                    _bindings, _conveyorTopology);
            }

            foreach (var kv in _failureCounts.OrderByDescending(x => x.Value))
            {
                if (kv.Value <= 0)
                {
                    continue;
                }

                diag.FailureReasonCounts.Add(new SimNamedCount
                {
                    Name = SimJobFailureReasonLabels.GetLabel(kv.Key),
                    Count = kv.Value,
                });
            }

            return diag;
        }

        private void ReleaseAllocatedSlot(WarehouseJob job)
        {
            if (!job.HasSlot)
            {
                return;
            }

            _slotAllocator.Release(job.TargetSlot);
            job.HasSlot = false;
            DecrementStackerActiveJobCount(job.AssignedStackerId);
        }

        #endregion
    }
}
