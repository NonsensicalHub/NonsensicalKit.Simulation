using System;
using System.Collections.Generic;
using System.Diagnostics;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>可选的墙钟耗时采集器，用于定位仿真 CPU 热点。</summary>
    internal sealed class SimWallClockProfiler
    {
        private static readonly NoOpScope s_noOp = new();

        private readonly Dictionary<string, MutableEntry> _entries = new();
        private long _runStartTicks;
        private bool _enabled;

        public bool Enabled => _enabled;

        public void Reset(bool enabled)
        {
            _enabled = enabled;
            _entries.Clear();
            _runStartTicks = enabled ? Stopwatch.GetTimestamp() : 0;
        }

        public IDisposable Begin(string phaseKey) =>
            _enabled ? new Scope(this, phaseKey) : s_noOp;

        internal void Add(string phaseKey, long elapsedTicks)
        {
            if (!_enabled || string.IsNullOrEmpty(phaseKey) || elapsedTicks <= 0)
            {
                return;
            }

            if (!_entries.TryGetValue(phaseKey, out var entry))
            {
                entry = new MutableEntry();
                _entries[phaseKey] = entry;
            }

            entry.TotalTicks += elapsedTicks;
            entry.CallCount++;
        }

        public SimWallClockProfileSnapshot BuildSnapshot()
        {
            var snapshot = new SimWallClockProfileSnapshot { Enabled = _enabled };
            if (!_enabled)
            {
                return snapshot;
            }

            var totalTicks = Math.Max(1, Stopwatch.GetTimestamp() - _runStartTicks);
            snapshot.TotalWallMilliseconds = totalTicks * 1000.0 / Stopwatch.Frequency;

            foreach (var pair in _entries)
            {
                var ms = pair.Value.TotalTicks * 1000.0 / Stopwatch.Frequency;
                snapshot.Entries.Add(new SimWallClockProfileEntry
                {
                    PhaseKey = pair.Key,
                    PhaseLabel = SimWallClockProfilePhases.GetLabel(pair.Key),
                    TotalMilliseconds = ms,
                    CallCount = pair.Value.CallCount,
                    PercentOfTotal = ms / snapshot.TotalWallMilliseconds * 100.0,
                });
            }

            snapshot.Entries.Sort((a, b) => b.TotalMilliseconds.CompareTo(a.TotalMilliseconds));
            return snapshot;
        }

        private sealed class MutableEntry
        {
            public long TotalTicks;
            public int CallCount;
        }

        private sealed class Scope : IDisposable
        {
            private readonly SimWallClockProfiler _profiler;
            private readonly string _phaseKey;
            private readonly long _startTicks;

            public Scope(SimWallClockProfiler profiler, string phaseKey)
            {
                _profiler = profiler;
                _phaseKey = phaseKey;
                _startTicks = Stopwatch.GetTimestamp();
            }

            public void Dispose() =>
                _profiler.Add(_phaseKey, Stopwatch.GetTimestamp() - _startTicks);
        }

        private sealed class NoOpScope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    internal static class SimWallClockProfilePhases
    {
        public const string InitReset = "init.reset";
        public const string InitTopology = "init.topology";
        public const string InitPorts = "init.ports";
        public const string InitFlowPlan = "init.flow_plan";
        public const string DesMainLoop = "des.main_loop";
        public const string ConveyorPathPlan = "conveyor.path_plan";
        public const string ConveyorZoneReserve = "conveyor.zone_reserve";
        public const string PlaybackRecordSubTask = "playback.record_subtask";
        public const string PlaybackConveyorSubTask = "playback.conveyor_subtask";
        public const string PostSortPlayback = "post.sort_playback";
        public const string PostSortSubTasks = "post.sort_subtasks";
        public const string PostOccupancyCheck = "post.occupancy_check";
        public const string PostTimelineCheck = "post.timeline_check";
        public const string PostDiagnostics = "post.diagnostics";

        public static string Event(SimEventType type) => $"des.event.{type}";

        public static string RecordSubTaskPhase(SimSubTaskKind kind) =>
            kind is SimSubTaskKind.InfeedMove
                or SimSubTaskKind.OutboundMove
                or SimSubTaskKind.SegmentTransit
                or SimSubTaskKind.SegmentHopMove
                or SimSubTaskKind.SegmentStopDwell
                or SimSubTaskKind.SegmentQueue
                or SimSubTaskKind.JunctionEnter
                or SimSubTaskKind.JunctionWait
                or SimSubTaskKind.JunctionExit
                ? PlaybackConveyorSubTask
                : PlaybackRecordSubTask;

        public static string GetLabel(string phaseKey)
        {
            if (string.IsNullOrEmpty(phaseKey))
            {
                return "—";
            }

            if (phaseKey.StartsWith("des.event.", StringComparison.Ordinal))
            {
                var name = phaseKey.Substring("des.event.".Length);
                return name switch
                {
                    nameof(SimEventType.InfeedPortFeed) => "DES 事件 · 入库口放货",
                    nameof(SimEventType.FlowCargoRelease) => "DES 事件 · 流程放货",
                    nameof(SimEventType.FlowPlanBatchRelease) => "DES 事件 · 流程批次放货",
                    nameof(SimEventType.FlowPlanInstantRelease) => "DES 事件 · 流程一次性放货",
                    nameof(SimEventType.InfeedServiceStart) => "DES 事件 · 入库服务开始",
                    nameof(SimEventType.InfeedServiceComplete) => "DES 事件 · 入库服务完成",
                    nameof(SimEventType.InfeedPortPhysicalRelease) => "DES 事件 · 入库口物理释放",
                    nameof(SimEventType.OutfeedServiceComplete) => "DES 事件 · 出库服务完成",
                    nameof(SimEventType.OutfeedPortPhysicalRelease) => "DES 事件 · 出库口物理释放",
                    nameof(SimEventType.OutfeedPortDispatch) => "DES 事件 · 出库口调度",
                    nameof(SimEventType.OccupancyReleased) => "DES 事件 · 占用释放",
                    nameof(SimEventType.ConveyorRouteRetry) => "DES 事件 · 输送路径重试",
                    nameof(SimEventType.ConveyorSegmentComplete) => "DES 事件 · 输送段完成",
                    nameof(SimEventType.ConveyorZoneComplete) => "DES 事件 · 输送 zone 完成",
                    nameof(SimEventType.ConveyorTransitComplete) => "DES 事件 · 输送到达",
                    nameof(SimEventType.StackerApproachComplete) => "DES 事件 · 堆垛机驶向作业点完成",
                    nameof(SimEventType.StackerPickComplete) => "DES 事件 · 堆垛机取货完成",
                    nameof(SimEventType.StackerMoveComplete) => "DES 事件 · 堆垛机移动完成",
                    nameof(SimEventType.StackerPlaceComplete) => "DES 事件 · 堆垛机放货完成",
                    nameof(SimEventType.OutboundPickupDeparture) => "DES 事件 · 出库交互点驶离",
                    nameof(SimEventType.JobCompleted) => "DES 事件 · 任务完成",
                    _ => $"DES 事件 · {name}",
                };
            }

            return phaseKey switch
            {
                InitReset => "初始化 · 重置状态",
                InitTopology => "初始化 · 拓扑校验",
                InitPorts => "初始化 · 端口初始化",
                InitFlowPlan => "初始化 · 流程计划",
                DesMainLoop => "DES · 主循环（合计）",
                ConveyorPathPlan => "输送 · 路径规划",
                ConveyorZoneReserve => "输送 · zone 预约",
                PlaybackRecordSubTask => "回放 · 记录子任务",
                PlaybackConveyorSubTask => "回放 · 输送细粒度子任务",
                PostSortPlayback => "收尾 · 排序回放事件",
                PostSortSubTasks => "收尾 · 排序子任务",
                PostOccupancyCheck => "收尾 · 占用自检",
                PostTimelineCheck => "收尾 · 时间轴自检",
                PostDiagnostics => "收尾 · 诊断汇总",
                _ => phaseKey,
            };
        }
    }
}
