using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>
    /// 仓储离散事件仿真主循环。只负责出队、推进时钟，并把事件交给一个进程。
    /// </summary>
    public sealed class WarehouseSimEngine : IPlaybackCapableWarehouseSimulator
    {
        private readonly SimRunContext _ctx;

        public WarehouseSimEngine(ISlotAllocator slotAllocator)
        {
            _ctx = new SimRunContext(slotAllocator);
        }

        public ISlotAllocator SlotAllocator => _ctx.SlotAllocator;

        public IReadOnlyList<SimPlaybackEvent> LastPlaybackEvents => _ctx.Timeline.PlaybackEvents;

        public IReadOnlyList<SimSubTask> LastSubTasks => _ctx.Timeline.SubTasks;

        public SimEventDiagnostics LastEventDiagnostics { get; private set; }

        public bool[] ExportOccupancySnapshot() =>
            _ctx.SlotAllocator.Occupied != null ? (bool[])_ctx.SlotAllocator.Occupied.Clone() : null;

        internal void UseLiveProgress(SimRunLiveProgress progress) => _ctx.LiveProgress = progress;

        public SimRunResult Run(WarehouseSimScenario scenario, SimRunOptions? runOptions = null)
        {
            var options = runOptions ?? SimRunOptions.Default;
            _ctx.Scenario = scenario;
            _ctx.Bindings = scenario?.ResolvedHardwareBindings;
            _ctx.Strategy = scenario?.ResolvedStrategy;
            _ctx.ShouldRecordPlayback = options.RecordPlaybackAndSubTasks;

            var result = new SimRunResult();
            if (_ctx.Bindings == null || scenario == null)
            {
                result.Success = false;
                result.Message = "Scenario 或硬件绑定未指定。";
                return result;
            }

            _ctx.WallClockProfiler.BindProgress(_ctx.LiveProgress);
            _ctx.WallClockProfiler.Reset(options.CollectWallClockProfile);

            using (_ctx.WallClockProfiler.Begin(SimWallClockProfilePhases.InitReset))
            {
                Reset();
            }

            using (_ctx.WallClockProfiler.Begin(SimWallClockProfilePhases.InitTopology))
            {
                if (!ValidateTopology(out var topologyError))
                {
                    result.Success = false;
                    result.Message = topologyError;
                    result.WallClockProfile = _ctx.WallClockProfiler.BuildSnapshot();
                    return result;
                }
            }

            using (_ctx.WallClockProfiler.Begin(SimWallClockProfilePhases.InitPorts))
            {
                _ctx.InitPorts();
            }

            using (_ctx.WallClockProfiler.Begin(SimWallClockProfilePhases.InitFlowPlan))
            {
                _ctx.InitFlow();
            }

            var plan = SimFlowPlanResolver.Resolve(scenario);
            result.TargetJobCount = _ctx.TargetCount;
            SimStrategyLabels.ApplyToResult(result, _ctx.Strategy);
            result.FlowPlanSummaryLabel = SimStrategyLabels.FormatFlowPlan(plan);
            result.StorageSlotCount = _ctx.SlotAllocator.StorageSlotCount;
            result.AllocatableStorageSlotCount = _ctx.SlotAllocator.CountAllocatableStorageSlots(
                _ctx.Bindings, _ctx.Topology);
            result.InitialOccupiedStorageSlotCount = _ctx.InitialOccupiedStorageSlotCount;

            if (result.AllocatableStorageSlotCount < result.TargetJobCount)
            {
                WarehouseSimLog.Warn(
                    $"可分配货位 {result.AllocatableStorageSlotCount} 少于计划任务 {result.TargetJobCount}；" +
                    "部分入库可能在库满或不可达货位时失败。");
            }

            WarehouseSimLog.Info(
                $"货位容量：可存储 {result.StorageSlotCount}，堆垛机可达可分配 {result.AllocatableStorageSlotCount}，" +
                $"初始占用 {_ctx.InitialOccupiedStorageSlotCount}");

            if (SimFlowPlanResolver.RequiresOutboundPorts(plan)
                && (_ctx.Topology.OutfeedNodeIndices == null
                    || _ctx.Topology.OutfeedNodeIndices.Count == 0))
            {
                result.Success = false;
                result.Message = "流程计划含出库任务，但 ConveyorMap 未配置出库口节点。";
                result.WallClockProfile = _ctx.WallClockProfiler.BuildSnapshot();
                return result;
            }

            WarehouseSimLog.Info(
                $"仿真开始：计划 {_ctx.TargetCount} 箱；流程={result.FlowPlanSummaryLabel}；" +
                $"堆垛机={result.StackerPlacementStrategyLabel}；" +
                $"入库口={result.InfeedPortSelectionStrategyLabel}；" +
                $"输送={result.ConveyorRoutingStrategyLabel}；" +
                $"入库口数 {_ctx.Topology.InfeedNodeIndices.Count}，" +
                $"出库口数 {_ctx.Topology.OutfeedNodeIndices?.Count ?? 0}");
            _ctx.InitConveyorKernel();
            _ctx.ScheduleFlowPlan();
            _ctx.ReportLiveProgress();

            using (_ctx.WallClockProfiler.Begin(SimWallClockProfilePhases.DesMainLoop))
            {
                while (_ctx.Queue.TryDequeue(out var evt) && _ctx.EventCount < _ctx.Bindings.MaxSimEvents)
                {
                    _ctx.EventCount++;
                    _ctx.EventTypeCounts[(int)evt.Type]++;
                    _ctx.Clock.AdvanceTo(evt.Time);
                    _ctx.Reservations.NoteHistoryFloor(_ctx.Clock.Now);
                    if ((_ctx.EventCount & 31) == 0)
                    {
                        _ctx.ReportLiveProgress();
                    }

                    Dispatch(evt, options.CollectWallClockProfile);
                }
            }

            using (_ctx.WallClockProfiler.Begin(SimWallClockProfilePhases.PostDiagnostics))
            {
                var queuePending = _ctx.Queue.Count;
                _ctx.ReportKernelDiagnostics();
                LastEventDiagnostics = _ctx.BuildEventDiagnostics(queuePending);
                _ctx.FinalizeFailureCounts();
                result.FailureCounts = new Dictionary<SimJobFailureReason, int>(_ctx.FailureCounts);
                var failureSummary = SimJobFailureReasonLabels.FormatSummary(result.FailureCounts);

                if (_ctx.EventCount >= _ctx.Bindings.MaxSimEvents)
                {
                    var incomplete = _ctx.CountIncompleteJobs();
                    if (incomplete > 0)
                    {
                        _ctx.RecordFailure(SimJobFailureReason.SimAbortedMaxEvents, incomplete);
                        result.FailureCounts = new Dictionary<SimJobFailureReason, int>(_ctx.FailureCounts);
                        failureSummary = SimJobFailureReasonLabels.FormatSummary(result.FailureCounts);
                    }

                    var diagnostics = SimEventDiagnosticsCollector.FormatPlainText(LastEventDiagnostics);
                    WarehouseSimLog.Error($"超过最大事件数 {_ctx.Bindings.MaxSimEvents}，仿真中止。\n{diagnostics}");
                    result.Success = false;
                    result.Message =
                        $"超过最大事件数 {_ctx.Bindings.MaxSimEvents}，仿真中止。\n失败统计：{failureSummary}\n{diagnostics}";
                }
                else if (_ctx.CompletedCount + _ctx.FailedNoSlot + _ctx.FailedNoCargo < _ctx.TargetCount
                         || _ctx.Flow.InboundWaitingCount > 0
                         || _ctx.Flow.OutboundWaitingCount > 0
                         || _ctx.Flow.HasPendingReleases())
                {
                    var diagnostics = SimEventDiagnosticsCollector.FormatPlainText(LastEventDiagnostics);
                    WarehouseSimLog.Warn($"事件队列已空但未完成全部任务。\n{diagnostics}");
                    result.Success = false;
                    result.Message =
                        $"事件队列已空但未完成全部任务。\n失败统计：{failureSummary}\n{diagnostics}";
                }
                else
                {
                    result.Success = true;
                    result.Message = _ctx.FailedNoSlot + _ctx.FailedNoCargo > 0
                        ? $"完成 {_ctx.CompletedCount} 单，{_ctx.FailedNoSlot} 箱入库失败，{_ctx.FailedNoCargo} 箱出库失败。失败统计：{failureSummary}"
                        : "仿真完成。";
                }

                result.CompletedJobCount = _ctx.CompletedCount;
                result.TotalSimSeconds = _ctx.Clock.Now;
                result.Completions = new List<JobCompletionRecord>(_ctx.Completions);
                result.ResourceWaitTotals = SimRunPostChecks.BuildWaitTotals(_ctx.Completions);
                result.ComputeStatistics();
                if (_ctx.ShouldRecordPlayback)
                {
                    _ctx.Timeline.Flatten();
                    using (_ctx.WallClockProfiler.Begin(SimWallClockProfilePhases.PostSortPlayback))
                    {
                        _ctx.Timeline.SortPlaybackEvents();
                    }

                    using (_ctx.WallClockProfiler.Begin(SimWallClockProfilePhases.PostSortSubTasks))
                    {
                        _ctx.Timeline.SortSubTasks();
                    }

                    result.ResourceUtilizations = SimResourceUtilizationBuilder.Build(
                        _ctx.Timeline.SubTasks,
                        result.TotalSimSeconds,
                        _ctx.Bindings?.ConveyorMap,
                        _ctx.Topology);
                    SimRunPostChecks.Run(_ctx, result);
                }
            }

            result.WallClockProfile = _ctx.WallClockProfiler.BuildSnapshot();
            WarehouseSimLog.Info(
                $"仿真结束：{(result.Success ? "成功" : "失败")} — {result.Message}；" +
                $"完成 {result.CompletedJobCount}/{result.TargetJobCount}，" +
                $"时长 {result.TotalSimSeconds:F1}s，吞吐 {result.ThroughputPerHour:F1} 箱/小时");
            return result;
        }

        private void Reset()
        {
            _ctx.ResetShared();
            var physicalSlots = _ctx.SlotAllocator.PhysicalSlotCount;
            var storageSlots = _ctx.SlotAllocator.StorageSlotCount;
            _ctx.InitialOccupiedStorageSlotCount = _ctx.SlotAllocator.Occupied != null
                ? _ctx.SlotAllocator.CountOccupiedStorageSlots(_ctx.SlotAllocator.Occupied)
                : 0;
            WarehouseSimLog.Info(
                $"货位：可存储 {storageSlots} / 物理格 {physicalSlots}（排除 {physicalSlots - storageSlots} 格）；" +
                $"布局 {_ctx.SlotAllocator.SlotLayoutDescription}");
        }

        private void Dispatch(ScheduledSimEvent evt, bool collectWallClockProfile)
        {
            if (!collectWallClockProfile)
            {
                DispatchCore(evt);
                return;
            }

            using (_ctx.WallClockProfiler.Begin(SimWallClockProfilePhases.Event(evt.Type)))
            {
                DispatchCore(evt);
            }
        }

        private void DispatchCore(ScheduledSimEvent evt)
        {
            var processes = _ctx.Processes;
            for (var i = 0; i < processes.Length; i++)
            {
                if (processes[i].Handles(evt.Type))
                {
                    processes[i].OnEvent(evt);
                    return;
                }
            }

            WarehouseSimLog.Warn($"未处理的仿真事件 {evt.Type}（JobId={evt.JobId}，Payload={evt.Payload}）。");
        }

        private bool ValidateTopology(out string error)
        {
            error = null;
            if (_ctx.Bindings.ConveyorMap == null)
            {
                error = "硬件绑定未配置 ConveyorMap。";
                return false;
            }

            if (!ConveyorMapTopology.TryBuild(_ctx.Bindings.ConveyorMap, _ctx.Bindings, out var topology, out error))
            {
                _ctx.Topology = null;
                return false;
            }

            _ctx.Topology = topology;
            var plan = SimFlowPlanResolver.Resolve(_ctx.Scenario);
            if (SimFlowPlanResolver.RequiresInboundPorts(plan) && _ctx.Topology.InfeedNodeIndices.Count == 0)
            {
                error = "流程计划含入库任务，但 ConveyorMap 中未配置入库口节点。";
                _ctx.Topology = null;
                return false;
            }

            if (_ctx.SlotAllocator is IWarehouseGridLookup grids
                && !WarehouseCatalog.TryValidate(_ctx.Bindings, _ctx.Topology, grids, out error))
            {
                _ctx.Topology = null;
                return false;
            }

            _ctx.Topology.WarmTransitShortestPathCache();
            return true;
        }
    }
}
