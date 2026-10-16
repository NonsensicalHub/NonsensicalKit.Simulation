using System;
using System.Collections.Generic;
using System.Globalization;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Core.Calendar;
using NonsensicalKit.Simulation.WarehouseSimulation.Core.Graph;
using NonsensicalKit.Simulation.WarehouseSimulation.Simulation.Routing;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation.Kernel
{
    /// <summary>
    /// 最小纯 C# 输送 DES 内核（阶段3）。
    /// <list type="bullet">
    /// <item>时钟复用 <see cref="SimClock"/>；事件队列按 (Time, 显式优先级, 稳定序号) 全序。</item>
    /// <item>每次只为“下一条未提交 RouteStep”原子预约资源（<see cref="ResourceCalendar"/>），失败只重试该步。</item>
    /// <item>同刻竞争：EffectivePriority(基础优先级+等待年龄) → FirstEligibleAt → DispatchSequence；不使用 JobId。</item>
    /// <item>节点占位用“保持预约”表达，离开时按尾清时刻截短；路段方向锁/区段持有到尾部清空。</item>
    /// <item>无未来事件仍有未完成任务 → 等待图诊断终态（Deadlock / NoFutureEvent）。</item>
    /// </list>
    /// 不依赖 Unity；不接主仿真；不读写旧 ReservationTable。
    /// </summary>
    public sealed class ConveyorDesKernel
    {
        private const double Eps = 1e-9;
        private const double MinDuration = 1e-6;

        private sealed class PendingRequest
        {
            public int StepIndex;
            public double FirstEligibleAt;
            public double ReadyAt;
            public long DispatchSequence = -1;
            public bool LoggedWait;
            public string LastBlocker;
        }

        private sealed class JobRuntime
        {
            public KernelJobSpec Spec;
            public IReadOnlyList<RouteStep> Steps;
            public IReadOnlyList<ProcessRequirement> Requirements;
            public KernelJobStatus Status = KernelJobStatus.Created;
            public string Location;
            public long HoldId = -1;
            public int CompletedStages;
            public PendingRequest Pending;
            public readonly HashSet<int> Committed = new HashSet<int>();
            public readonly KernelJobResult Result = new KernelJobResult();
        }

        private readonly ConveyorGraph _graph;
        private readonly KernelOptions _opt;

        private ResourceCalendar _cal;
        private KernelEventQueue _queue;
        private SimClock _clock;
        private KernelEventLog _log;
        private List<JobRuntime> _jobs;
        private Dictionary<int, JobRuntime> _byId;
        private HashSet<double> _dispatchTimes;
        private Dictionary<KernelEventKind, int> _histogram;
        private long _dispatchSeq;
        private string _violation;

        public ConveyorDesKernel(ConveyorGraph graph, KernelOptions options = null)
        {
            _graph = graph;
            _opt = options ?? new KernelOptions();
        }

        /// <summary>最近一次运行使用的资源日历（只读检查用）。</summary>
        public ResourceCalendar Calendar => _cal;

        public KernelRunResult Run(IReadOnlyList<KernelJobSpec> specs)
        {
            _cal = new ResourceCalendar();
            _queue = new KernelEventQueue();
            _clock = new SimClock();
            _clock.Reset();
            _log = new KernelEventLog();
            _jobs = new List<JobRuntime>();
            _byId = new Dictionary<int, JobRuntime>();
            _dispatchTimes = new HashSet<double>();
            _histogram = new Dictionary<KernelEventKind, int>();
            _dispatchSeq = 0;
            _violation = null;

            var configError = ValidateConfiguration(specs);
            if (configError != null)
            {
                return BuildResult(KernelOutcome.InvalidConfiguration, 0, new KernelDiagnostics { Message = configError });
            }

            RegisterResources();

            var ordered = new List<KernelJobSpec>(specs);
            var inputIndex = new Dictionary<KernelJobSpec, int>();
            for (var i = 0; i < ordered.Count; i++) inputIndex[ordered[i]] = i;
            ordered.Sort((a, b) =>
            {
                var c = a.ArrivalTime.CompareTo(b.ArrivalTime);
                if (c != 0) return c;
                c = a.ArrivalSequence.CompareTo(b.ArrivalSequence);
                return c != 0 ? c : inputIndex[a].CompareTo(inputIndex[b]);
            });

            foreach (var spec in ordered)
            {
                var job = new JobRuntime
                {
                    Spec = spec,
                    Steps = spec.Plan.Steps ?? Array.Empty<RouteStep>(),
                    Requirements = spec.Plan.Requirements ?? Array.Empty<ProcessRequirement>(),
                };
                job.Result.JobId = spec.JobId;
                job.Result.ArrivalTime = spec.ArrivalTime;
                _jobs.Add(job);
                _byId[spec.JobId] = job;
                _queue.Schedule(spec.ArrivalTime, KernelEventKind.JobArrived, spec.JobId);
            }

            var processed = 0;
            while (_queue.TryDequeue(out var ev))
            {
                processed++;
                if (processed > _opt.MaxEvents)
                {
                    return FinishWithUnfinished(KernelOutcome.EventLimitReached, processed,
                        $"事件数超过保险丝 MaxEvents={_opt.MaxEvents}。");
                }

                if (ev.Time < _clock.Now - Eps)
                {
                    _violation = $"时间回退：事件 {ev.Kind}@{ev.Time} 早于当前 {_clock.Now}。";
                    break;
                }

                _clock.AdvanceTo(ev.Time);
                _histogram.TryGetValue(ev.Kind, out var n);
                _histogram[ev.Kind] = n + 1;

                Handle(ev);

                if (_violation == null && _opt.CheckInvariantsEveryEvent)
                {
                    _violation = CheckInvariants();
                }

                if (_violation != null)
                {
                    break;
                }
            }

            if (_violation != null)
            {
                _log.Append(_clock.Now, KernelLogKind.Diagnostic, -1, -1, detail: "InvariantViolation: " + _violation);
                return FinishWithUnfinished(KernelOutcome.InvariantViolation, processed, _violation);
            }

            return FinishWithUnfinished(null, processed, null);
        }

        #region 初始化

        private string ValidateConfiguration(IReadOnlyList<KernelJobSpec> specs)
        {
            if (_graph == null) return "ConveyorGraph 为空。";
            if (specs == null) return "任务列表为空。";
            if (!(_opt.AgingIntervalSeconds > 0)) return "AgingIntervalSeconds 必须为严格正数。";
            if (!(_opt.CargoLengthMeters > 0)) return "CargoLengthMeters 必须为正。";
            if (!(_opt.HoldHorizonSeconds > 0)) return "HoldHorizonSeconds 必须为正。";

            var ids = new HashSet<int>();
            for (var i = 0; i < specs.Count; i++)
            {
                var s = specs[i];
                if (s?.Plan == null) return $"任务[{i}] 缺少 RoutePlan。";
                if (!ids.Add(s.JobId)) return $"重复 JobId：{s.JobId}";
                if (double.IsNaN(s.ArrivalTime) || s.ArrivalTime < 0) return $"任务 {s.JobId} 到达时刻非法。";
                if (!_graph.TryGetNode(s.Plan.StartNodeId, out _)) return $"任务 {s.JobId} 起点不在图中。";
                var steps = s.Plan.Steps;
                if (steps != null)
                {
                    for (var k = 0; k < steps.Count; k++)
                    {
                        if (steps[k].OccurrenceIndex != k)
                        {
                            return $"任务 {s.JobId} 步骤 OccurrenceIndex 不连续（期望 {k}）。";
                        }
                    }
                }
            }

            return null;
        }

        private void RegisterResources()
        {
            ResourceCalendarBootstrap.RegisterFromGraph(_cal, _graph, new ResourceCalendarBootstrap.Options
            {
                DefaultZoneCapacity = Math.Max(1, _opt.SegmentZoneCapacity),
                DefaultDirectionLockSameWayCapacity = Math.Max(1, _opt.DirectionLockSameWayCapacity),
                DefaultProcessStationCapacity = Math.Max(1, _opt.ProcessStationCapacity),
                ZonesPerSegment = 1,
            });

            var nodeIds = new List<string>(_graph.Nodes.Keys);
            nodeIds.Sort(StringComparer.Ordinal);
            foreach (var id in nodeIds)
            {
                var cap = _opt.NodeHoldCapacity != null && _opt.NodeHoldCapacity.TryGetValue(id, out var c)
                    ? c
                    : _opt.DefaultNodeHoldCapacity;
                _cal.EnsureRegistered(new ResourceDefinition
                {
                    ResourceId = KernelResourceIds.NodeHold(id),
                    Kind = ResourceKind.BufferSlot,
                    Capacity = Math.Max(1, cap),
                });
            }
        }

        #endregion

        #region 事件处理

        private void Handle(KernelEvent ev)
        {
            var now = ev.Time;
            _byId.TryGetValue(ev.JobId, out var job);
            var step = job != null && ev.StepIndex >= 0 && ev.StepIndex < job.Steps.Count ? job.Steps[ev.StepIndex] : null;

            switch (ev.Kind)
            {
                case KernelEventKind.JobArrived:
                    job.Location = "arrival:" + job.Spec.Plan.StartNodeId;
                    _log.Append(now, KernelLogKind.JobArrived, job.Spec.JobId, -1, to: job.Spec.Plan.StartNodeId);
                    AddPending(job, -1, now);
                    break;

                case KernelEventKind.DispatchRound:
                    _dispatchTimes.Remove(now);
                    Arbitrate(now);
                    break;

                case KernelEventKind.MoveStarted:
                    if (step == null || string.IsNullOrEmpty(step.ArcId) || !_graph.TryGetArc(step.ArcId, out _))
                    {
                        _violation = $"路由非法：任务 {ev.JobId} 步骤 {ev.StepIndex} 的弧不存在。";
                        return;
                    }

                    job.Status = KernelJobStatus.Moving;
                    job.Location = "arc:" + step.ArcId;
                    _log.Append(now, KernelLogKind.MoveStarted, job.Spec.JobId, ev.StepIndex, step.Kind,
                        step.FromNodeId, step.ToNodeId, new[] { step.ArcId });
                    break;

                case KernelEventKind.MoveCompleted:
                    job.Status = KernelJobStatus.AtNode;
                    job.Location = step.ToNodeId;
                    _log.Append(now, KernelLogKind.MoveCompleted, job.Spec.JobId, ev.StepIndex, step.Kind,
                        step.FromNodeId, step.ToNodeId, new[] { step.ArcId });
                    Advance(job, ev.StepIndex, now);
                    break;

                case KernelEventKind.TailCleared:
                    _log.Append(now, KernelLogKind.TailCleared, ev.JobId, ev.StepIndex, step?.Kind,
                        step?.FromNodeId, step?.ToNodeId, step?.PhysicalSegmentId != null ? new[] { step.PhysicalSegmentId } : null);
                    ScheduleDispatch(now);
                    break;

                case KernelEventKind.HoldReleased:
                    _log.Append(now, KernelLogKind.HoldReleased, ev.JobId, ev.StepIndex, step?.Kind, step?.FromNodeId);
                    ScheduleDispatch(now);
                    break;

                case KernelEventKind.ProcessStarted:
                    job.Status = KernelJobStatus.Processing;
                    _log.Append(now, KernelLogKind.ProcessStarted, job.Spec.JobId, ev.StepIndex, step.Kind,
                        step.FromNodeId, step.ToNodeId, detail: step.ProcessRequirementId);
                    break;

                case KernelEventKind.ProcessCompleted:
                    CompleteProcessStage(job, step, now);
                    if (_violation != null) return;
                    _log.Append(now, KernelLogKind.ProcessCompleted, job.Spec.JobId, ev.StepIndex, step.Kind,
                        step.FromNodeId, step.ToNodeId, detail: step.ProcessRequirementId);
                    ScheduleDispatch(now);
                    Advance(job, ev.StepIndex, now);
                    break;

                case KernelEventKind.ActionStarted:
                    job.Status = KernelJobStatus.InAction;
                    _log.Append(now, KernelLogKind.ActionStarted, job.Spec.JobId, ev.StepIndex, step.Kind,
                        step.FromNodeId, step.ToNodeId);
                    break;

                case KernelEventKind.ActionCompleted:
                    job.Status = KernelJobStatus.AtNode;
                    _log.Append(now, KernelLogKind.ActionCompleted, job.Spec.JobId, ev.StepIndex, step.Kind,
                        step.FromNodeId, step.ToNodeId);
                    ScheduleDispatch(now);
                    Advance(job, ev.StepIndex, now);
                    break;
            }
        }

        private void CompleteProcessStage(JobRuntime job, RouteStep step, double now)
        {
            if (job.CompletedStages >= job.Requirements.Count)
            {
                _violation = $"工序顺序：任务 {job.Spec.JobId} 在全部工序完成后又执行加工。";
                return;
            }

            var expected = job.Requirements[job.CompletedStages].RequirementId;
            if (!string.Equals(expected, step.ProcessRequirementId, StringComparison.Ordinal))
            {
                _violation = $"工序顺序：任务 {job.Spec.JobId} 期望 {expected}，实际 {step.ProcessRequirementId}。";
                return;
            }

            job.CompletedStages++;
        }

        private void Advance(JobRuntime job, int stepIndex, double now)
        {
            job.Result.LastCompletedOccurrence = stepIndex;
            var next = stepIndex + 1;
            if (next >= job.Steps.Count)
            {
                CompleteJob(job, now);
            }
            else
            {
                AddPending(job, next, now);
            }
        }

        private void CompleteJob(JobRuntime job, double now)
        {
            var goal = job.Spec.Plan.GoalNodeId;
            if (job.Steps.Count > 0 && !string.Equals(job.Location, goal, StringComparison.Ordinal))
            {
                _violation = $"任务 {job.Spec.JobId} 完成时不在终点 {goal}（位置 {job.Location}）。";
                return;
            }

            if (job.CompletedStages != job.Requirements.Count)
            {
                _violation = $"任务 {job.Spec.JobId} 完成时工序 {job.CompletedStages}/{job.Requirements.Count}。";
                return;
            }

            if (job.HoldId >= 0)
            {
                _cal.TryTruncate(job.HoldId, now);
                job.HoldId = -1;
                ScheduleDispatch(now);
            }

            job.Status = KernelJobStatus.Completed;
            job.Location = goal;
            job.Result.CompletionTime = now;
            _log.Append(now, KernelLogKind.JobCompleted, job.Spec.JobId, job.Steps.Count - 1, to: goal);
        }

        #endregion

        #region 派发与提交

        private void AddPending(JobRuntime job, int stepIndex, double readyAt)
        {
            job.Pending = new PendingRequest
            {
                StepIndex = stepIndex,
                FirstEligibleAt = readyAt,
                ReadyAt = readyAt,
            };
            job.Status = KernelJobStatus.WaitingForResource;
            ScheduleDispatch(readyAt);
        }

        private void ScheduleDispatch(double time)
        {
            if (_dispatchTimes.Add(time))
            {
                _queue.Schedule(time, KernelEventKind.DispatchRound);
            }
        }

        private long EffectivePriority(JobRuntime j, double now) =>
            j.Spec.BasePriority + (long)Math.Floor(Math.Max(0, now - j.Pending.FirstEligibleAt) / _opt.AgingIntervalSeconds);

        private void Arbitrate(double now)
        {
            var batch = new List<JobRuntime>();
            for (var i = 0; i < _jobs.Count; i++)
            {
                var j = _jobs[i];
                if (j.Pending != null && j.Pending.ReadyAt <= now + Eps)
                {
                    batch.Add(j);
                }
            }

            if (batch.Count == 0) return;

            // 首次成为可派发请求者：按 (FirstEligibleAt, BasePriority↓, ArrivalSequence, StepIndex) 获得不可变 DispatchSequence。
            var fresh = batch.FindAll(j => j.Pending.DispatchSequence < 0);
            fresh.Sort((a, b) =>
            {
                var c = a.Pending.FirstEligibleAt.CompareTo(b.Pending.FirstEligibleAt);
                if (c != 0) return c;
                c = b.Spec.BasePriority.CompareTo(a.Spec.BasePriority);
                if (c != 0) return c;
                c = a.Spec.ArrivalSequence.CompareTo(b.Spec.ArrivalSequence);
                return c != 0 ? c : a.Pending.StepIndex.CompareTo(b.Pending.StepIndex);
            });
            foreach (var f in fresh) f.Pending.DispatchSequence = ++_dispatchSeq;

            batch.Sort((a, b) =>
            {
                var c = EffectivePriority(b, now).CompareTo(EffectivePriority(a, now));
                if (c != 0) return c;
                c = a.Pending.FirstEligibleAt.CompareTo(b.Pending.FirstEligibleAt);
                return c != 0 ? c : a.Pending.DispatchSequence.CompareTo(b.Pending.DispatchSequence);
            });

            foreach (var job in batch)
            {
                var pending = job.Pending;
                if (pending == null) continue;
                if (TryCommit(job, now, out var blocker))
                {
                    continue;
                }

                if (_violation != null) return;
                pending.LastBlocker = blocker;
                if (!pending.LoggedWait)
                {
                    pending.LoggedWait = true;
                    var occ = pending.StepIndex;
                    var st = occ >= 0 ? job.Steps[occ] : null;
                    _log.Append(now, KernelLogKind.WaitingForResource, job.Spec.JobId, occ, st?.Kind,
                        st?.FromNodeId, st?.ToNodeId ?? job.Spec.Plan.StartNodeId, new[] { blocker },
                        detail: "seq=" + pending.DispatchSequence.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        /// <summary>为当前一步构建资源需求并原子提交；失败零副作用，仅返回阻塞资源。</summary>
        private bool TryCommit(JobRuntime job, double now, out string blocker)
        {
            blocker = null;
            var pending = job.Pending;
            var t0 = Math.Max(now, pending.ReadyAt);
            var demands = new List<ResourceWindowRequest>();
            RouteStep step = null;
            string holdResource = null;
            double duration = 0, tail = 0;

            if (pending.StepIndex < 0)
            {
                holdResource = KernelResourceIds.NodeHold(job.Spec.Plan.StartNodeId);
                demands.Add(ResourceWindowRequest.Create(holdResource, _opt.HoldHorizonSeconds));
            }
            else
            {
                step = job.Steps[pending.StepIndex];
                switch (step.Kind)
                {
                    case RouteStepKind.Move:
                    case RouteStepKind.PassThrough:
                        if (!TryResolveArc(step, out var arc))
                        {
                            _violation = $"路由非法：任务 {job.Spec.JobId} 步骤 {step.OccurrenceIndex} 无弧 {step.ArcId}。";
                            return false;
                        }

                        duration = Math.Max(MinDuration, step.NominalCostSeconds > 0 ? step.NominalCostSeconds : arc.TransitSeconds);
                        var speed = arc.SpeedMetersPerSecond > 0.01f ? arc.SpeedMetersPerSecond : 0.01f;
                        tail = Math.Max(MinDuration, _opt.CargoLengthMeters / speed);
                        var dir = ResourceIds.DirectionForArc(arc);
                        demands.Add(ResourceWindowRequest.Create(ResourceIds.DirectionLock(arc.PhysicalSegmentId), duration + tail, 0, 1, dir));
                        demands.Add(ResourceWindowRequest.Create(ResourceIds.ConveyorZone(arc.PhysicalSegmentId, 0), duration + tail));
                        var isFinal = pending.StepIndex == job.Steps.Count - 1
                                      && string.Equals(step.ToNodeId, job.Spec.Plan.GoalNodeId, StringComparison.Ordinal);
                        if (!isFinal)
                        {
                            holdResource = KernelResourceIds.NodeHold(step.ToNodeId);
                            demands.Add(ResourceWindowRequest.Create(holdResource, _opt.HoldHorizonSeconds, duration));
                        }

                        if (step.Kind == RouteStepKind.PassThrough && _graph.TryGetNode(step.ToNodeId, out var st))
                        {
                            var traversal = st.PassThroughPolicy == StationPassThroughPolicy.TraversalOnly
                                ? ResourceIds.StationTraversal(st.NodeId)
                                : st.PassThroughPolicy == StationPassThroughPolicy.ExclusiveWithService
                                    ? ResourceIds.StationExclusiveWithService(st.NodeId)
                                    : null;
                            if (traversal != null && _cal.TryGetResource(traversal, out _))
                            {
                                demands.Add(ResourceWindowRequest.Create(traversal, duration + tail));
                            }
                        }

                        break;

                    case RouteStepKind.Process:
                        duration = Math.Max(0, step.NominalCostSeconds);
                        if (duration > Eps)
                        {
                            demands.Add(ResourceWindowRequest.Create(ResourceIds.ProcessStation(step.FromNodeId), duration));
                            if (_graph.TryGetNode(step.FromNodeId, out var station)
                                && station.PassThroughPolicy == StationPassThroughPolicy.ExclusiveWithService
                                && _cal.TryGetResource(ResourceIds.StationExclusiveWithService(station.NodeId), out _))
                            {
                                demands.Add(ResourceWindowRequest.Create(ResourceIds.StationExclusiveWithService(station.NodeId), duration));
                            }
                        }

                        break;

                    case RouteStepKind.Turnaround:
                        duration = Math.Max(0, step.NominalCostSeconds);
                        if (duration > Eps && !string.IsNullOrEmpty(step.PhysicalSegmentId))
                        {
                            var tdir = TravelDirection.None;
                            if (!string.IsNullOrEmpty(step.ArcId) && _graph.TryGetArc(step.ArcId, out var rev))
                            {
                                tdir = ResourceIds.DirectionForArc(rev);
                            }

                            demands.Add(ResourceWindowRequest.Create(ResourceIds.DirectionLock(step.PhysicalSegmentId), duration, 0, 1, tdir));
                        }

                        break;

                    case RouteStepKind.Transfer:
                        duration = Math.Max(0, step.NominalCostSeconds);
                        var xfer = ResourceIds.TransferDevice(step.ResourceId ?? step.FromNodeId);
                        if (duration > Eps && _cal.TryGetResource(xfer, out _))
                        {
                            demands.Add(ResourceWindowRequest.Create(xfer, duration));
                        }

                        break;
                }
            }

            var anchor = t0;
            var handles = new List<ReservationHandle>();
            if (demands.Count > 0)
            {
                var found = _cal.FindEarliestWindow(demands, t0);
                if (!found.Success || found.AnchorStart - t0 > _opt.HoldHorizonSeconds * 0.5)
                {
                    blocker = IdentifyBlocker(demands, t0, found);
                    return false;
                }

                anchor = found.AnchorStart;
                var specs = new List<ExactReservationSpec>(demands.Count);
                for (var i = 0; i < demands.Count; i++)
                {
                    specs.Add(new ExactReservationSpec
                    {
                        ResourceId = demands[i].ResourceId,
                        Start = found.Windows[i].Start,
                        End = found.Windows[i].End,
                        Units = demands[i].Units,
                        Direction = demands[i].Direction,
                        JobId = job.Spec.JobId,
                        RouteOccurrenceIndex = pending.StepIndex,
                    });
                }

                var commit = _cal.TryCommitExact(specs);
                if (!commit.Success)
                {
                    blocker = commit.Conflict?.ResourceId ?? "unknown";
                    return false;
                }

                handles = commit.Handles;
            }

            if (pending.StepIndex >= 0 && !job.Committed.Add(pending.StepIndex))
            {
                _violation = $"任务 {job.Spec.JobId} 步骤 {pending.StepIndex} 被重复提交。";
                return false;
            }

            job.Pending = null;
            job.Status = KernelJobStatus.Committed;

            var resIds = new List<string>();
            var resvIds = new List<long>();
            var windowEnd = anchor;
            long newHoldId = -1;
            foreach (var h in handles)
            {
                resIds.Add(h.ResourceId);
                resvIds.Add(h.ReservationId);
                if (h.End < _opt.HoldHorizonSeconds * 0.5 + anchor && h.End > windowEnd) windowEnd = h.End;
                if (holdResource != null && h.ResourceId == holdResource) newHoldId = h.ReservationId;
            }

            _log.Append(now, KernelLogKind.ReservationCommitted, job.Spec.JobId, pending.StepIndex, step?.Kind,
                step?.FromNodeId, step?.ToNodeId ?? job.Spec.Plan.StartNodeId, resIds, resvIds, anchor, windowEnd);

            var jobId = job.Spec.JobId;
            var idx = pending.StepIndex;
            if (idx < 0)
            {
                job.HoldId = newHoldId;
                job.Location = job.Spec.Plan.StartNodeId;
                job.Status = KernelJobStatus.AtNode;
                _log.Append(anchor, KernelLogKind.JobEntered, jobId, -1, to: job.Spec.Plan.StartNodeId);
                if (job.Steps.Count == 0) CompleteJob(job, anchor);
                else AddPending(job, 0, anchor);
                return true;
            }

            switch (step.Kind)
            {
                case RouteStepKind.Move:
                case RouteStepKind.PassThrough:
                    if (job.HoldId >= 0)
                    {
                        // 源节点占位保持到货物尾部离开该节点。
                        var release = anchor + tail;
                        _cal.TryTruncate(job.HoldId, release);
                        _queue.Schedule(release, KernelEventKind.HoldReleased, jobId, idx);
                    }

                    job.HoldId = newHoldId;
                    job.Result.MoveSeconds += duration;
                    _queue.Schedule(anchor, KernelEventKind.MoveStarted, jobId, idx);
                    _queue.Schedule(anchor + duration, KernelEventKind.MoveCompleted, jobId, idx);
                    _queue.Schedule(anchor + duration + tail, KernelEventKind.TailCleared, jobId, idx);
                    break;

                case RouteStepKind.Process:
                    if (duration > Eps)
                    {
                        job.Result.ProcessSeconds += duration;
                        _queue.Schedule(anchor, KernelEventKind.ProcessStarted, jobId, idx);
                        _queue.Schedule(anchor + duration, KernelEventKind.ProcessCompleted, jobId, idx);
                    }
                    else
                    {
                        CompleteProcessStage(job, step, anchor);
                        if (_violation != null) return false;
                        _log.Append(anchor, KernelLogKind.ProcessCompleted, jobId, idx, step.Kind, step.FromNodeId, step.ToNodeId,
                            detail: step.ProcessRequirementId + " (零时长)");
                        Advance(job, idx, anchor);
                    }

                    break;

                default:
                    if (duration > Eps)
                    {
                        job.Result.ActionSeconds += duration;
                        _queue.Schedule(anchor, KernelEventKind.ActionStarted, jobId, idx);
                        _queue.Schedule(anchor + duration, KernelEventKind.ActionCompleted, jobId, idx);
                    }
                    else
                    {
                        _log.Append(anchor, KernelLogKind.ActionCompleted, jobId, idx, step.Kind, step.FromNodeId, step.ToNodeId,
                            detail: "零时长");
                        Advance(job, idx, anchor);
                    }

                    break;
            }

            return true;
        }

        private bool TryResolveArc(RouteStep step, out ConveyorArc arc)
        {
            arc = null;
            if (!string.IsNullOrEmpty(step.ArcId) && _graph.TryGetArc(step.ArcId, out arc)) return true;
            return _graph.TryGetArc(step.FromNodeId, step.ToNodeId, out arc);
        }

        private string IdentifyBlocker(List<ResourceWindowRequest> demands, double t0, FindWindowResult combined)
        {
            for (var i = 0; i < demands.Count; i++)
            {
                var d = demands[i];
                var at = t0 + d.OffsetFromAnchor;
                var single = _cal.FindEarliestWindow(
                    new[] { ResourceWindowRequest.Create(d.ResourceId, d.Duration, 0, d.Units, d.Direction) }, at);
                if (!single.Success || single.AnchorStart > at + Eps)
                {
                    return d.ResourceId;
                }
            }

            return combined?.Conflict?.ResourceId ?? "(共同时间窗不可行)";
        }

        #endregion

        #region 不变量与诊断

        /// <summary>§12.1 不变量（每次事件迁移后）。返回 null 表示通过。</summary>
        private string CheckInvariants()
        {
            if (!_cal.ValidateCapacityInvariant(out var capErr))
            {
                return "容量安全：" + capErr;
            }

            for (var i = 0; i < _jobs.Count; i++)
            {
                var j = _jobs[i];
                if (j.Status == KernelJobStatus.Created) continue;
                if (string.IsNullOrEmpty(j.Location))
                {
                    return $"位置唯一：任务 {j.Spec.JobId} 无位置状态。";
                }

                if (j.CompletedStages < 0 || j.CompletedStages > j.Requirements.Count)
                {
                    return $"工序顺序：任务 {j.Spec.JobId} 阶段索引越界。";
                }
            }

            return null;
        }

        private KernelRunResult FinishWithUnfinished(KernelOutcome? forced, int processed, string message)
        {
            var now = _clock.Now;
            var unfinished = new List<JobRuntime>();
            foreach (var j in _jobs)
            {
                if (j.Status != KernelJobStatus.Completed && j.Status != KernelJobStatus.Failed) unfinished.Add(j);
            }

            var diag = new KernelDiagnostics
            {
                Message = message,
                EventKindHistogram = new Dictionary<KernelEventKind, int>(_histogram),
            };

            if (unfinished.Count == 0 && forced == null)
            {
                return BuildResult(KernelOutcome.AllJobsFinished, processed, diag);
            }

            var edges = new List<KernelWaitEdge>();
            var graph = new Dictionary<int, List<int>>();
            foreach (var j in unfinished)
            {
                var id = j.Spec.JobId;
                graph[id] = new List<int>();
                if (j.Pending != null)
                {
                    var res = j.Pending.LastBlocker ?? "(尚未尝试)";
                    var holders = new List<int>();
                    foreach (var h in _cal.GetReservations(res))
                    {
                        if (h.JobId != id && h.End > now + Eps && !holders.Contains(h.JobId)) holders.Add(h.JobId);
                    }

                    holders.Sort();
                    graph[id].AddRange(holders);
                    edges.Add(new KernelWaitEdge
                    {
                        WaiterJobId = id,
                        WaitingOccurrence = j.Pending.StepIndex,
                        ResourceId = res,
                        HolderJobIds = holders,
                        Reason = holders.Count > 0 ? "资源被其他任务持有" : "资源无持有者但无重试事件",
                    });
                }
                else
                {
                    edges.Add(new KernelWaitEdge
                    {
                        WaiterJobId = id,
                        WaitingOccurrence = j.Result.LastCompletedOccurrence + 1,
                        ResourceId = null,
                        HolderJobIds = Array.Empty<int>(),
                        Reason = $"缺失事件来源（状态 {j.Status}）",
                    });
                }
            }

            var cycle = FindCycle(graph);
            var outcome = forced ?? (cycle.Count > 0 ? KernelOutcome.DeadlockDetected : KernelOutcome.NoFutureEvent);
            diag.WaitEdges = edges;
            diag.CycleJobIds = cycle;
            var ids = new List<int>();
            foreach (var j in unfinished) ids.Add(j.Spec.JobId);
            diag.UnfinishedJobIds = ids;
            if (diag.Message == null)
            {
                diag.Message = outcome == KernelOutcome.DeadlockDetected
                    ? "等待图存在环：" + string.Join("→", cycle)
                    : "无未来事件但仍有未完成任务。";
            }

            foreach (var e in edges)
            {
                _log.Append(now, KernelLogKind.Diagnostic, e.WaiterJobId, e.WaitingOccurrence,
                    resourceIds: e.ResourceId != null ? new[] { e.ResourceId } : null,
                    detail: $"{outcome}: {e.Reason}; holders=[{string.Join(",", e.HolderJobIds)}]");
            }

            foreach (var j in unfinished)
            {
                j.Status = KernelJobStatus.Failed;
                j.Result.FailureReason = outcome + ": " + diag.Message;
                _log.Append(now, KernelLogKind.JobFailed, j.Spec.JobId, j.Pending?.StepIndex ?? -1, detail: outcome.ToString());
            }

            return BuildResult(outcome, processed, diag);
        }

        private static List<int> FindCycle(Dictionary<int, List<int>> graph)
        {
            var keys = new List<int>(graph.Keys);
            keys.Sort();
            var color = new Dictionary<int, int>();
            var stack = new List<int>();
            List<int> found = null;

            bool Dfs(int u)
            {
                color[u] = 1;
                stack.Add(u);
                if (graph.TryGetValue(u, out var outs))
                {
                    foreach (var v in outs)
                    {
                        color.TryGetValue(v, out var cv);
                        if (cv == 1)
                        {
                            found = stack.GetRange(stack.IndexOf(v), stack.Count - stack.IndexOf(v));
                            return true;
                        }

                        if (cv == 0 && graph.ContainsKey(v) && Dfs(v)) return true;
                    }
                }

                stack.RemoveAt(stack.Count - 1);
                color[u] = 2;
                return false;
            }

            foreach (var k in keys)
            {
                color.TryGetValue(k, out var c);
                if (c == 0 && Dfs(k)) break;
            }

            return found ?? new List<int>();
        }

        private KernelRunResult BuildResult(KernelOutcome outcome, int processed, KernelDiagnostics diag)
        {
            var results = new List<KernelJobResult>();
            if (_jobs != null)
            {
                foreach (var j in _jobs)
                {
                    var r = j.Result;
                    r.Status = j.Status;
                    if (j.Status == KernelJobStatus.Completed)
                    {
                        r.WaitSeconds = Math.Max(0, r.CompletionTime - r.ArrivalTime - r.MoveSeconds - r.ProcessSeconds - r.ActionSeconds);
                    }

                    results.Add(r);
                }
            }

            return new KernelRunResult
            {
                Outcome = outcome,
                FinalTime = _clock?.Now ?? 0,
                ProcessedEvents = processed,
                Log = _log?.Entries ?? Array.Empty<KernelLogEntry>(),
                Jobs = results,
                Diagnostics = diag ?? new KernelDiagnostics(),
            };
        }

        #endregion
    }
}
