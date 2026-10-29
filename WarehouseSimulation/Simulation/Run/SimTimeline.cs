using System.Collections.Generic;
using System.Linq;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>一次运行的回放事件与子任务。进程只通过这里追加，循环结束后才排序。</summary>
    internal sealed class SimTimeline
    {
        private readonly SimRunContext _ctx;
        private readonly List<SimPlaybackEvent> _playback = new();
        private readonly List<SimSubTask> _subTasks = new();
        private readonly Dictionary<int, List<SimSubTask>> _subTasksByJob = new();
        private int _nextSubTaskId;

        public SimTimeline(SimRunContext ctx) => _ctx = ctx;

        public IReadOnlyList<SimPlaybackEvent> PlaybackEvents => _playback;

        public IReadOnlyList<SimSubTask> SubTasks => _subTasks;

        public void Clear()
        {
            _playback.Clear();
            _subTasks.Clear();
            _subTasksByJob.Clear();
            _nextSubTaskId = 0;
        }

        public void EnsurePlaybackSnapshot(WarehouseJob job)
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

        public void RecordPlayback(
            WarehouseJob job,
            SimPlaybackPhase phase,
            int stackerId,
            GridIndex slot) =>
            RecordPlaybackAt(_ctx.Clock.Now, job, phase, stackerId, slot);

        public void RecordPlaybackAt(
            double simTime,
            WarehouseJob job,
            SimPlaybackPhase phase,
            int stackerId,
            GridIndex slot,
            bool attachPathContext = false)
        {
            if (!_ctx.ShouldRecordPlayback)
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

        public void RecordSubTask(
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
            if (!_ctx.ShouldRecordPlayback)
            {
                return;
            }

            using (_ctx.WallClockProfiler.Begin(SimWallClockProfilePhases.RecordSubTaskPhase(kind)))
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
                    HasCargoHandover = kind is SimSubTaskKind.StackerPick or SimSubTaskKind.StackerPlace,
                    CargoHandoverSimTime = kind is SimSubTaskKind.StackerPick or SimSubTaskKind.StackerPlace
                        ? StackerCargoHandover.FromPhase(startSimTime, endSimTime)
                        : 0d,
                    PathNodeIndices = pathCopy,
                    SegmentSchedule = scheduleCopy,
                });
            }
        }

        public void Flatten()
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

        public void SortPlaybackEvents()
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

        public void SortSubTasks()
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

        /// <summary>
        /// 同一箱货的输送子任务收成一条时间轴。
        /// 后记下的窗口若盖住先记下的，裁掉重叠的那段；裁完后相邻子任务之间的空白补到前一段上。
        /// </summary>
        public void NormalizeConveyorSubTaskTimeline()
        {
            if (_subTasks.Count == 0)
            {
                return;
            }

            TrimOverlappingConveyorSubTasks();
            CloseConveyorTimelineGaps();
        }

        private void CopyConveyorContext(
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

        private List<SimSubTask> MutableJobSubTasks(int jobId)
        {
            if (!_subTasksByJob.TryGetValue(jobId, out var list))
            {
                list = new List<SimSubTask>(24);
                _subTasksByJob[jobId] = list;
            }

            return list;
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

                        if (other.EndSimTime - System.Math.Max(anchor.StartSimTime, other.StartSimTime) <= 1e-6)
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
    }
}
