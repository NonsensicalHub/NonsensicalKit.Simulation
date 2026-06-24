using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback.Tasks;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Playback
{
    /// <summary>
    /// 货位回放快照索引：预处理后按仿真时刻 O(变更数) 求值，避免每帧全表扫描子任务。
    /// </summary>
    public sealed class WarehouseSlotPlaybackSnapshotIndex
    {
        private const double TimeEpsilon = 1e-9;

        private readonly struct OccupancyChange
        {
            public readonly double Time;
            public readonly GridIndex Slot;
            public readonly bool Remove;

            public OccupancyChange(double time, GridIndex slot, bool remove)
            {
                Time = time;
                Slot = slot;
                Remove = remove;
            }
        }

        private readonly struct HighlightCandidate
        {
            public readonly double StartSimTime;
            public readonly double EndSimTime;
            public readonly GridIndex Slot;
            public readonly bool RequiresContainsTime;
            public readonly SimSubTaskKind Kind;
            public readonly int JobId;

            public HighlightCandidate(
                double startSimTime,
                double endSimTime,
                GridIndex slot,
                bool requiresContainsTime,
                SimSubTaskKind kind,
                int jobId)
            {
                StartSimTime = startSimTime;
                EndSimTime = endSimTime;
                Slot = slot;
                RequiresContainsTime = requiresContainsTime;
                Kind = kind;
                JobId = jobId;
            }

            public bool Matches(double simTime)
            {
                if (simTime < StartSimTime - TimeEpsilon)
                {
                    return false;
                }

                if (!RequiresContainsTime)
                {
                    return true;
                }

                if (EndSimTime - StartSimTime <= TimeEpsilon)
                {
                    return Math.Abs(simTime - StartSimTime) <= TimeEpsilon;
                }

                return simTime >= StartSimTime - TimeEpsilon && simTime < EndSimTime - TimeEpsilon;
            }
        }

        private IReadOnlyList<SimSubTask> _source = Array.Empty<SimSubTask>();
        private List<OccupancyChange> _occupancyChanges = new();
        private List<HighlightCandidate> _highlightCandidates = new();
        private HashSet<GridIndex> _initialOccupied = new();
        private HashSet<int> _outboundJobIds = new();

        private readonly HashSet<GridIndex> _scratchOccupied = new();
        private int _occupancyCursor;
        private double _lastSimTime = double.NaN;

        public bool IsCurrentSource(IReadOnlyList<SimSubTask> subTasks) => ReferenceEquals(_source, subTasks);

        public void Build(
            IReadOnlyList<SimSubTask> subTasks,
            bool highlightOnStackerMove,
            IReadOnlyCollection<GridIndex> initialOccupied = null)
        {
            _source = subTasks ?? Array.Empty<SimSubTask>();
            _occupancyChanges.Clear();
            _highlightCandidates.Clear();
            _initialOccupied.Clear();
            _outboundJobIds.Clear();
            _occupancyCursor = 0;
            _lastSimTime = double.NaN;

            if (initialOccupied != null)
            {
                foreach (var slot in initialOccupied)
                {
                    _initialOccupied.Add(slot);
                }
            }

            if (_source.Count == 0)
            {
                return;
            }

            _outboundJobIds = SimSubTaskQuery.BuildOutboundJobIds(_source);

            for (var i = 0; i < _source.Count; i++)
            {
                var task = _source[i];

                if (task.Kind == SimSubTaskKind.InfeedPlace)
                {
                    _highlightCandidates.Add(new HighlightCandidate(
                        task.StartSimTime,
                        task.EndSimTime,
                        task.Slot,
                        requiresContainsTime: false,
                        task.Kind,
                        task.JobId));
                }

                if (_outboundJobIds.Contains(task.JobId) && task.Kind == SimSubTaskKind.StackerPick)
                {
                    _highlightCandidates.Add(new HighlightCandidate(
                        task.StartSimTime,
                        task.EndSimTime,
                        task.Slot,
                        requiresContainsTime: true,
                        task.Kind,
                        task.JobId));

                    // 取货完成前货位仍应有货，与堆垛机 AttachCargo 时刻（Pick 结束）对齐。
                    _occupancyChanges.Add(new OccupancyChange(task.EndSimTime, task.Slot, remove: true));
                }

                if (highlightOnStackerMove && task.Kind == SimSubTaskKind.StackerMove)
                {
                    _highlightCandidates.Add(new HighlightCandidate(
                        task.StartSimTime,
                        task.EndSimTime,
                        task.Slot,
                        requiresContainsTime: true,
                        task.Kind,
                        task.JobId));
                }

                if (task.Kind is SimSubTaskKind.StackerPlace or SimSubTaskKind.Completed)
                {
                    var remove = _outboundJobIds.Contains(task.JobId);
                    _occupancyChanges.Add(new OccupancyChange(task.EndSimTime, task.Slot, remove));
                }
            }

            _occupancyChanges.Sort((a, b) =>
            {
                var time = a.Time.CompareTo(b.Time);
                return time != 0 ? time : a.Remove.CompareTo(b.Remove);
            });

            _highlightCandidates.Sort((a, b) =>
            {
                var start = a.StartSimTime.CompareTo(b.StartSimTime);
                if (start != 0)
                {
                    return start;
                }

                var kind = a.Kind.CompareTo(b.Kind);
                return kind != 0 ? kind : a.JobId.CompareTo(b.JobId);
            });
        }

        public WarehouseSlotPlaybackSnapshot BuildAt(double simTime)
        {
            var occupied = BuildOccupiedAt(simTime);
            var highlight = ResolveHighlightAt(simTime);
            return new WarehouseSlotPlaybackSnapshot
            {
                OccupiedSlots = occupied,
                HighlightSlot = highlight,
            };
        }

        private HashSet<GridIndex> BuildOccupiedAt(double simTime)
        {
            var isForward = !double.IsNaN(_lastSimTime) && simTime >= _lastSimTime - TimeEpsilon;

            if (!isForward)
            {
                _scratchOccupied.Clear();
                foreach (var slot in _initialOccupied)
                {
                    _scratchOccupied.Add(slot);
                }

                _occupancyCursor = UpperBoundOccupancy(simTime);
                for (var i = 0; i < _occupancyCursor; i++)
                {
                    ApplyOccupancyChange(_occupancyChanges[i]);
                }
            }
            else
            {
                if (double.IsNaN(_lastSimTime))
                {
                    _scratchOccupied.Clear();
                    foreach (var slot in _initialOccupied)
                    {
                        _scratchOccupied.Add(slot);
                    }

                    _occupancyCursor = 0;
                }

                AdvanceOccupancyCursor(simTime);
            }

            _lastSimTime = simTime;
            return new HashSet<GridIndex>(_scratchOccupied);
        }

        private void AdvanceOccupancyCursor(double simTime)
        {
            while (_occupancyCursor < _occupancyChanges.Count
                   && _occupancyChanges[_occupancyCursor].Time <= simTime + TimeEpsilon)
            {
                ApplyOccupancyChange(_occupancyChanges[_occupancyCursor]);
                _occupancyCursor++;
            }
        }

        private int UpperBoundOccupancy(double simTime)
        {
            var lo = 0;
            var hi = _occupancyChanges.Count;
            while (lo < hi)
            {
                var mid = lo + (hi - lo) / 2;
                if (_occupancyChanges[mid].Time <= simTime + TimeEpsilon)
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

        private void ApplyOccupancyChange(in OccupancyChange change)
        {
            if (change.Remove)
            {
                _scratchOccupied.Remove(change.Slot);
            }
            else
            {
                _scratchOccupied.Add(change.Slot);
            }
        }

        private GridIndex? ResolveHighlightAt(double simTime)
        {
            if (_highlightCandidates.Count == 0)
            {
                return null;
            }

            var lastStartIndex = UpperBoundHighlight(simTime) - 1;
            if (lastStartIndex < 0)
            {
                return null;
            }

            GridIndex? highlight = null;
            for (var i = 0; i <= lastStartIndex; i++)
            {
                var candidate = _highlightCandidates[i];
                if (candidate.Matches(simTime))
                {
                    highlight = candidate.Slot;
                }
            }

            return highlight;
        }

        private int UpperBoundHighlight(double simTime)
        {
            var lo = 0;
            var hi = _highlightCandidates.Count;
            while (lo < hi)
            {
                var mid = lo + (hi - lo) / 2;
                if (_highlightCandidates[mid].StartSimTime <= simTime + TimeEpsilon)
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

        public void ResetPlaybackCursor()
        {
            _occupancyCursor = 0;
            _lastSimTime = double.NaN;
            _scratchOccupied.Clear();
        }
    }
}
