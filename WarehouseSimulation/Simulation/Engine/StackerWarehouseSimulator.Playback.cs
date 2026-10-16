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
            if (job.Direction == SimFlowDirection.Inbound)
            {
                _inboundCompletedCount++;
            }

            ReportLiveProgress();
            if (job.Direction != SimFlowDirection.Outbound)
            {
                DecrementStackerActiveJobCount(job.AssignedStackerId);
                if (_outboundWaitingCount > 0)
                {
                    // 入库占着的堆垛机空出来了：唤醒出库排队，否则出入库混合时出库请求可能永远不再被派单。
                    ScheduleOutfeedQueueFill(_clock.Now);
                }
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
                job.TargetSlot,
                attachPathContext: true);
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

            // 路口校正和重排会改已有路段的时刻，段数不一定增加。每次附带上下文都拷当前表。
            // 补记会把中间段放到表尾，先按路径排好，回放和自检才不会把下一条当成反向的下一段。
            ConveyorSegmentScheduleNavigation.SortByPath(
                job.ConveyorSegmentSchedule,
                job.ConveyorPathNodeIndices);
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

            NormalizeConveyorSubTaskTimeline();
            RunOccupancySelfCheck(result);
            RunSegmentCoOccupancySelfCheck(result);
            RunCargoSpacingSelfCheck(result);
            RunSubTaskTimelineSelfCheck(result);
        }

        /// <summary>
        /// 同一箱货的输送子任务收成一条时间轴。
        /// 后记下的窗口若盖住先记下的，裁掉重叠的那段；裁完后相邻子任务之间的空白补到前一段上，避免箱子在时间轴上消失。
        /// </summary>
        private void NormalizeConveyorSubTaskTimeline()
        {
            if (_subTasks.Count == 0)
            {
                return;
            }

            TrimOverlappingConveyorSubTasks();
            CloseConveyorTimelineGaps();
        }

        private void TrimOverlappingConveyorSubTasks()
        {
            var indicesByJob = GroupConveyorSubTaskIndices();
            var removeIds = new HashSet<int>();
            foreach (var indices in indicesByJob.Values)
            {
                indices.Sort(CompareConveyorSubTaskIndices);
                for (var i = 0; i < indices.Count; i++)
                {
                    if (removeIds.Contains(_subTasks[indices[i]].SubTaskId))
                    {
                        continue;
                    }

                    var anchor = _subTasks[indices[i]];
                    for (var j = i + 1; j < indices.Count; j++)
                    {
                        if (removeIds.Contains(_subTasks[indices[j]].SubTaskId))
                        {
                            continue;
                        }

                        var other = _subTasks[indices[j]];
                        if (other.StartSimTime >= anchor.EndSimTime - 1e-6)
                        {
                            break;
                        }

                        if (other.EndSimTime - Math.Max(anchor.StartSimTime, other.StartSimTime) <= 1e-6)
                        {
                            continue;
                        }

                        if (other.SubTaskId > anchor.SubTaskId)
                        {
                            if (other.StartSimTime < anchor.StartSimTime)
                            {
                                other.EndSimTime = anchor.StartSimTime;
                            }
                            else
                            {
                                other.StartSimTime = anchor.EndSimTime;
                            }

                            if (other.EndSimTime <= other.StartSimTime + 1e-6)
                            {
                                removeIds.Add(other.SubTaskId);
                            }
                            else
                            {
                                _subTasks[indices[j]] = other;
                            }
                        }
                        else
                        {
                            if (anchor.StartSimTime < other.StartSimTime)
                            {
                                anchor.EndSimTime = other.StartSimTime;
                            }
                            else
                            {
                                anchor.StartSimTime = other.EndSimTime;
                            }

                            if (anchor.EndSimTime <= anchor.StartSimTime + 1e-6)
                            {
                                removeIds.Add(anchor.SubTaskId);
                                break;
                            }

                            _subTasks[indices[i]] = anchor;
                        }
                    }
                }
            }

            if (removeIds.Count > 0)
            {
                _subTasks.RemoveAll(task => removeIds.Contains(task.SubTaskId));
            }
        }

        private void CloseConveyorTimelineGaps()
        {
            var indicesByJob = GroupConveyorSubTaskIndices();
            var inserts = new List<SimSubTask>();
            foreach (var indices in indicesByJob.Values)
            {
                indices.Sort(CompareConveyorSubTaskIndices);
                for (var i = 0; i < indices.Count - 1; i++)
                {
                    var prev = _subTasks[indices[i]];
                    var next = _subTasks[indices[i + 1]];
                    if (next.StartSimTime <= prev.EndSimTime + 1e-6
                        || GapContainsOtherSubTask(prev.JobId, prev.EndSimTime, next.StartSimTime, prev.SubTaskId, next.SubTaskId))
                    {
                        continue;
                    }

                    if (next.Kind == SimSubTaskKind.JunctionEnter
                        && !(prev.Kind == SimSubTaskKind.SegmentStopDwell
                             && prev.FromNodeIndex == next.FromNodeIndex
                             && prev.ToNodeIndex == next.ToNodeIndex))
                    {
                        var dwell = next;
                        dwell.SubTaskId = _nextSubTaskId++;
                        dwell.Kind = SimSubTaskKind.SegmentStopDwell;
                        dwell.StartSimTime = prev.EndSimTime;
                        dwell.EndSimTime = next.StartSimTime;
                        dwell.SegmentSlotIndex = 0;
                        inserts.Add(dwell);
                        continue;
                    }

                    prev.EndSimTime = next.StartSimTime;
                    _subTasks[indices[i]] = prev;
                }
            }

            if (inserts.Count > 0)
            {
                _subTasks.AddRange(inserts);
            }
        }

        private bool GapContainsOtherSubTask(
            int jobId,
            double gapStart,
            double gapEnd,
            int subTaskIdA,
            int subTaskIdB)
        {
            for (var i = 0; i < _subTasks.Count; i++)
            {
                var task = _subTasks[i];
                if (task.JobId != jobId
                    || task.SubTaskId == subTaskIdA
                    || task.SubTaskId == subTaskIdB
                    || task.Kind == SimSubTaskKind.Completed)
                {
                    continue;
                }

                if (task.EndSimTime > gapStart + 1e-6 && task.StartSimTime < gapEnd - 1e-6)
                {
                    return true;
                }
            }

            return false;
        }

        private Dictionary<int, List<int>> GroupConveyorSubTaskIndices()
        {
            var indicesByJob = new Dictionary<int, List<int>>();
            for (var i = 0; i < _subTasks.Count; i++)
            {
                if (!SimSubTaskKinds.ExtendsConveyorTimeline(_subTasks[i].Kind))
                {
                    continue;
                }

                var jobId = _subTasks[i].JobId;
                if (!indicesByJob.TryGetValue(jobId, out var indices))
                {
                    indices = new List<int>();
                    indicesByJob[jobId] = indices;
                }

                indices.Add(i);
            }

            return indicesByJob;
        }

        private int CompareConveyorSubTaskIndices(int left, int right)
        {
            var startCompare = _subTasks[left].StartSimTime.CompareTo(_subTasks[right].StartSimTime);
            if (startCompare != 0)
            {
                return startCompare;
            }

            return _subTasks[left].SubTaskId.CompareTo(_subTasks[right].SubTaskId);
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

        private void RunSegmentCoOccupancySelfCheck(SimRunResult result)
        {
            if (result == null || _conveyorTopology == null || _bindings?.ConveyorMap == null)
            {
                return;
            }

            var overlaps = SimSegmentCoOccupancyChecker.FindOverlaps(
                _subTasks,
                _bindings.ConveyorMap,
                _conveyorTopology);
            result.SegmentCoOccupancies = overlaps;
            if (overlaps.Count == 0)
            {
                return;
            }

            SimSegmentCoOccupancyChecker.CountByDirection(overlaps, out var opposite, out var sameDirection);
            WarehouseSimLog.Error(
                $"仿真后自检发现 {overlaps.Count} 处路段同时占用（对向 {opposite}，同向 {sameDirection}；导出调试 Markdown 报告查看明细）。");
            MarkSelfCheckFailed(
                result,
                $"自检：路段同时占用 {overlaps.Count} 处（对向 {opposite}，同向 {sameDirection}）。");
        }

        private void RunCargoSpacingSelfCheck(SimRunResult result)
        {
            if (result == null || _conveyorTopology == null || _bindings?.ConveyorMap == null)
            {
                return;
            }

            var violations = SimCargoSpacingChecker.FindViolations(
                _jobs.Values,
                _bindings.ConveyorMap,
                _conveyorTopology);
            result.CargoSpacingViolations = violations;
            if (violations.Count == 0)
            {
                return;
            }

            WarehouseSimLog.Error(
                $"仿真后自检发现 {violations.Count} 段货物间距不足（中心距 < 单箱长度，回放会重叠；导出调试 Markdown 报告查看明细）。");
            MarkSelfCheckFailed(result, $"自检：货物间距不足 {violations.Count} 段。");
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

        private List<SimSubTask> MutableJobSubTasks(int jobId)
        {
            if (!_subTasksByJob.TryGetValue(jobId, out var list))
            {
                list = new List<SimSubTask>(24);
                _subTasksByJob[jobId] = list;
            }

            return list;
        }

        private bool TryGetJobSubTasks(int jobId, out List<SimSubTask> list) =>
            _subTasksByJob.TryGetValue(jobId, out list) && list != null && list.Count > 0;

        private void FlattenSubTasks()
        {
            _subTasks.Clear();
            foreach (var pair in _subTasksByJob)
            {
                if (pair.Value != null && pair.Value.Count > 0)
                {
                    _subTasks.AddRange(pair.Value);
                }
            }
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
            bool hasStackerPose = false,
            bool stackerToUsesForkDepth = false,
            float stackerToForkDepthMeters = 0f)
        {
            if (!_recordPlayback)
            {
                return;
            }

            using (_wallClockProfiler.Begin(SimWallClockProfilePhases.RecordSubTaskPhase(kind)))
            {
                CopyConveyorContext(job, attachPathContext, out var pathCopy, out var scheduleCopy);

                MutableJobSubTasks(job.JobId).Add(new SimSubTask
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
                    StackerToUsesForkDepth = stackerToUsesForkDepth,
                    StackerToForkDepthMeters = stackerToForkDepthMeters,
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
