using System;
using System.Collections.Generic;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>
    /// 子任务时间轴按 JobId / StackerId 的索引，供回放每帧求值时避免全表扫描。
    /// </summary>
    public sealed class SimSubTaskPlaybackIndex
    {
        private const double TimeEpsilon = 1e-6;

        private IReadOnlyList<SimSubTask> _source = Array.Empty<SimSubTask>();
        private readonly Dictionary<int, List<SimSubTask>> _byJob = new();
        private readonly Dictionary<int, List<SimSubTask>> _byStacker = new();
        private readonly Dictionary<int, double> _jobFirstStart = new();
        private readonly Dictionary<int, double> _jobLastEnd = new();
        private readonly List<int> _jobIds = new();

        public IReadOnlyList<int> JobIds => _jobIds;

        public bool IsCurrentSource(IReadOnlyList<SimSubTask> subTasks) => ReferenceEquals(_source, subTasks);

        public void Build(IReadOnlyList<SimSubTask> subTasks)
        {
            _source = subTasks ?? Array.Empty<SimSubTask>();
            _byJob.Clear();
            _byStacker.Clear();
            _jobFirstStart.Clear();
            _jobLastEnd.Clear();
            _jobIds.Clear();

            for (var i = 0; i < _source.Count; i++)
            {
                var task = _source[i];
                AddToJobList(task);
                if (task.StackerId >= 0 && IsStackerMotionKind(task.Kind))
                {
                    AddToStackerList(task);
                }
            }
        }

        /// <summary>任务在仿真时间轴上是否仍可能需要输送线料箱可视更新。</summary>
        public bool IsJobVisibleAt(int jobId, double simTime)
        {
            if (!_jobFirstStart.TryGetValue(jobId, out var first))
            {
                return false;
            }

            if (simTime < first - TimeEpsilon)
            {
                return false;
            }

            return !_jobLastEnd.TryGetValue(jobId, out var last) || simTime <= last + TimeEpsilon;
        }

        public bool TryGetJobTasks(int jobId, out IReadOnlyList<SimSubTask> tasks)
        {
            if (_byJob.TryGetValue(jobId, out var list))
            {
                tasks = list;
                return true;
            }

            tasks = Array.Empty<SimSubTask>();
            return false;
        }

        public bool TryGetStackerTasks(int stackerId, out IReadOnlyList<SimSubTask> tasks)
        {
            if (_byStacker.TryGetValue(stackerId, out var list))
            {
                tasks = list;
                return true;
            }

            tasks = Array.Empty<SimSubTask>();
            return false;
        }

        public bool TryGetActive(int jobId, double simTime, out SimSubTask active)
        {
            active = default;
            if (!_byJob.TryGetValue(jobId, out var list))
            {
                return false;
            }

            var found = false;
            for (var i = 0; i < list.Count; i++)
            {
                var task = list[i];
                if (!task.ContainsTime(simTime))
                {
                    continue;
                }

                if (!found || task.StartSimTime >= active.StartSimTime)
                {
                    active = task;
                    found = true;
                }
            }

            return found;
        }

        public bool HasStarted(int jobId, double simTime)
        {
            if (!_jobFirstStart.TryGetValue(jobId, out var first))
            {
                return false;
            }

            return simTime >= first - TimeEpsilon;
        }

        public bool TryGetActiveForStacker(int stackerId, double simTime, out SimSubTask active)
        {
            active = default;
            if (!_byStacker.TryGetValue(stackerId, out var list))
            {
                return false;
            }

            var found = false;
            for (var i = 0; i < list.Count; i++)
            {
                var task = list[i];
                if (!task.ContainsTime(simTime))
                {
                    continue;
                }

                if (!found || task.StartSimTime >= active.StartSimTime)
                {
                    active = task;
                    found = true;
                }
            }

            return found;
        }

        public bool TryGetJobContext(
            int jobId,
            out int[] path,
            out ConveyorSegmentScheduleEntry[] schedule)
        {
            path = null;
            schedule = null;
            if (!_byJob.TryGetValue(jobId, out var list))
            {
                return false;
            }

            for (var i = 0; i < list.Count; i++)
            {
                var task = list[i];
                if (task.PathNodeIndices != null && task.PathNodeIndices.Length > 0)
                {
                    path = task.PathNodeIndices;
                }

                if (task.SegmentSchedule != null && task.SegmentSchedule.Length > 0)
                {
                    schedule = task.SegmentSchedule;
                }

                if (path != null && schedule != null)
                {
                    return true;
                }
            }

            return path != null || schedule != null;
        }

        public void CopyJobIds(List<int> buffer)
        {
            buffer.Clear();
            buffer.AddRange(_jobIds);
        }

        /// <summary>复制在 <paramref name="simTime"/> 仍可能处于输送可视窗口内的 JobId。</summary>
        public void CopyVisibleJobIds(double simTime, List<int> buffer)
        {
            buffer.Clear();
            for (var i = 0; i < _jobIds.Count; i++)
            {
                var jobId = _jobIds[i];
                if (IsJobVisibleAt(jobId, simTime))
                {
                    buffer.Add(jobId);
                }
            }
        }

        private void AddToJobList(in SimSubTask task)
        {
            var jobId = task.JobId;
            var taskEnd = ResolveTaskTimelineEnd(task);
            if (!_byJob.TryGetValue(jobId, out var list))
            {
                list = new List<SimSubTask>();
                _byJob[jobId] = list;
                _jobIds.Add(jobId);
                _jobFirstStart[jobId] = task.StartSimTime;
                _jobLastEnd[jobId] = taskEnd;
            }
            else
            {
                if (task.StartSimTime < _jobFirstStart[jobId])
                {
                    _jobFirstStart[jobId] = task.StartSimTime;
                }

                if (taskEnd > _jobLastEnd[jobId])
                {
                    _jobLastEnd[jobId] = taskEnd;
                }
            }

            list.Add(task);
        }

        private static double ResolveTaskTimelineEnd(in SimSubTask task)
        {
            var end = task.EndSimTime;
            var schedule = task.SegmentSchedule;
            if (schedule == null || schedule.Length == 0)
            {
                return end;
            }

            var occEnd = schedule[^1].OccupancyEndSimTime;
            return occEnd > end ? occEnd : end;
        }

        private void AddToStackerList(in SimSubTask task)
        {
            var stackerId = task.StackerId;
            if (!_byStacker.TryGetValue(stackerId, out var list))
            {
                list = new List<SimSubTask>();
                _byStacker[stackerId] = list;
            }

            list.Add(task);
        }

        private static bool IsStackerMotionKind(SimSubTaskKind kind) =>
            kind is SimSubTaskKind.StackerWait
                or SimSubTaskKind.StackerApproach
                or SimSubTaskKind.StackerPick
                or SimSubTaskKind.StackerMove
                or SimSubTaskKind.StackerPlace;
    }
}
