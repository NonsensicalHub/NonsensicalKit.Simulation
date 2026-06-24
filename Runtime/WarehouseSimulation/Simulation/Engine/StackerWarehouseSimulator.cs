using System;
using System.Collections.Generic;
using System.Linq;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>
    /// 堆垛机立体库入库的离散事件仿真器（DES）主干。
    /// </summary>
    /// <remarks>
    /// <para><b>时间推进</b>：从事件优先队列取出时刻最早的事件，将 <see cref="SimClock"/> 推进到该时刻并分发处理。</para>
    /// <para><b>单箱生命周期</b>（见各 partial）：</para>
    /// <list type="number">
    /// <item>入库口放货与扫描服务（<c>Infeed</c>）</item>
    /// <item>输送网 zone 链逐步预约（<c>Conveyor</c> + <see cref="ConveyorTransitScheduler"/>）</item>
    /// <item>取货点堆垛机取/移/放（<c>Stacker</c>）</item>
    /// </list>
    /// <para><b>资源模型</b>：<see cref="ReservationTable"/> 记录各资源 ID 的时空占用；冲突时按 FIFO 顺延。
    /// 占用结束通过 <c>OccupancyNotify</c> partial 异步唤醒等待队列中的任务。</para>
    /// <para><b>完成时间</b>：每箱耗时 = 各阶段服务时间之和 + 因资源冲突产生的等待时间（非固定常数）。</para>
    /// </remarks>
    public sealed partial class StackerWarehouseSimulator : IPlaybackCapableWarehouseSimulator
    {
        // —— 货位分配（由调用方注入，测试/正式实现均可）——
        private readonly ISlotAllocator _slotAllocator;

        // —— 场景与配置（Run 时注入，Reset 后只读使用）——
        private IWarehouseSimulationBindings _bindings;
        private WarehouseSimStrategyProfile _strategy;
        private WarehouseSimScenario _scenario;
        private ConveyorMapTopology _conveyorTopology;

        // —— DES 核心：仿真时钟 + 事件队列 + 全局资源预约表 ——
        private readonly SimClock _clock = new();
        private readonly SimEventQueue _queue = new();
        private ReservationTable _reservations = new();

        // —— 任务与输出 ——
        /// <summary>进行中的入库任务，键为 JobId。</summary>
        private readonly Dictionary<int, WarehouseJob> _jobs = new();
        /// <summary>已成功完成的任务统计记录。</summary>
        private readonly List<JobCompletionRecord> _completions = new();
        /// <summary>Unity 回放用离散相位事件（可选，由场景开关控制）。</summary>
        private readonly List<SimPlaybackEvent> _playback = new();
        /// <summary>细粒度子任务时间轴（占用自检、HTML 报告）。</summary>
        private readonly List<SimSubTask> _subTasks = new();
        private int _nextSubTaskId;

        // —— 入库口与堆垛机负载（放货决策用，非 ReservationTable 内资源）——
        /// <summary>各入库口是否仍有货箱占据物理碰撞区（尾端未离开前为 true）。</summary>
        private bool[] _infeedCargoOccupied;
        /// <summary>各入库口已预定但尚未完成服务的任务数（含在途输送）。</summary>
        private int[] _infeedReservationCounts;
        /// <summary>各地图节点（取货点）上已选定路径、尚未到达的任务数。</summary>
        private int[] _pickupReservationCounts;
        /// <summary>各堆垛机已分配货位且未完成的任务数（用于负载均衡选机）。</summary>
        private int[] _stackerActiveJobCounts;
        private StackerCarriageBookkeeper _stackerCarriage = new();
        private ConveyorTransitScheduler _conveyorScheduler;
        private ConveyorSubTaskRecorder _conveyorSubTaskRecorder;
        /// <summary>各入库口扫描服务时间轴的下一空闲时刻（单口串行服务）。</summary>
        private double[] _infeedServiceFreeByPort;
        /// <summary>是否已为该入库口排定 <see cref="SimEventType.InfeedPortFeed"/> 事件（防重复入队）。</summary>
        private bool[] _infeedPortFeedScheduled;
        private int[] _infeedPortOrderScratch;
        private int _infeedRoundRobinCursor;

        // —— 全局到货与进度计数 ——
        /// <summary>本场景计划任务总数（流程计划各段数量之和）。</summary>
        private int _targetCount;
        private int _nextJobId;
        private bool _inboundExhaustedAtInfeed;
        private int _failedNoCargo;
        private bool _recordPlayback;
        private bool _collectWallClockProfile;
        private readonly SimWallClockProfiler _wallClockProfiler = new();
        private int _completedCount;
        private int _failedNoSlot;
        private readonly Dictionary<SimJobFailureReason, int> _failureCounts = new();
        private int _initialOccupiedStorageSlotCount;
        private int _eventCount;
        private int[] _eventTypeCounts;

        public ISlotAllocator SlotAllocator => _slotAllocator;

        public IReadOnlyList<SimPlaybackEvent> LastPlaybackEvents => _playback;
        public IReadOnlyList<SimSubTask> LastSubTasks => _subTasks;
        public SimEventDiagnostics LastEventDiagnostics { get; private set; }

        public StackerWarehouseSimulator(ISlotAllocator slotAllocator)
        {
            _slotAllocator = slotAllocator ?? throw new ArgumentNullException(nameof(slotAllocator));
        }

        /// <summary>仿真结束后的货位占用快照（flat 索引由 <see cref="ISlotAllocator"/> 实现决定）。</summary>
        public bool[] ExportOccupancySnapshot() =>
            _slotAllocator.Occupied != null ? (bool[])_slotAllocator.Occupied.Clone() : null;

        /// <inheritdoc />
        /// <remarks>主循环：按事件时间推进时钟并分发，直至任务全部完成或达到事件上限。</remarks>
        public SimRunResult Run(WarehouseSimScenario scenario, SimRunOptions? runOptions = null)
        {
            var options = runOptions ?? SimRunOptions.Default;
            _scenario = scenario;
            _bindings = scenario?.ResolvedHardwareBindings;
            _strategy = scenario?.ResolvedStrategy;
            _recordPlayback = options.RecordPlaybackAndSubTasks;
            _collectWallClockProfile = options.CollectWallClockProfile;

            var result = new SimRunResult();
            if (_bindings == null || scenario == null)
            {
                result.Success = false;
                result.Message = "Scenario 或硬件绑定未指定。";
                return result;
            }

            _wallClockProfiler.Reset(_collectWallClockProfile);

            using (_wallClockProfiler.Begin(SimWallClockProfilePhases.InitReset))
            {
                Reset();
            }

            using (_wallClockProfiler.Begin(SimWallClockProfilePhases.InitTopology))
            {
                if (!ValidateTopology(out var topologyError))
                {
                    result.Success = false;
                    result.Message = topologyError;
                    result.WallClockProfile = _wallClockProfiler.BuildSnapshot();
                    return result;
                }
            }

            using (_wallClockProfiler.Begin(SimWallClockProfilePhases.InitPorts))
            {
                InitInfeedPorts();
                InitOutfeedPorts();
            }

            using (_wallClockProfiler.Begin(SimWallClockProfilePhases.InitFlowPlan))
            {
                InitFlowPlan();
            }

            var plan = SimFlowPlanResolver.Resolve(scenario);
            result.TargetJobCount = _targetCount;
            ApplyStrategyLabels(result);
            result.FlowPlanSummaryLabel = SimStrategyLabels.FormatFlowPlan(plan);
            result.StorageSlotCount = _slotAllocator.StorageSlotCount;
            result.AllocatableStorageSlotCount = _slotAllocator.CountAllocatableStorageSlots(
                _bindings, _conveyorTopology);
            result.InitialOccupiedStorageSlotCount = _initialOccupiedStorageSlotCount;

            if (result.AllocatableStorageSlotCount < result.TargetJobCount)
            {
                WarehouseSimLog.Warn(
                    $"可分配货位 {result.AllocatableStorageSlotCount} 少于计划任务 {result.TargetJobCount}；" +
                    "部分入库可能在库满或不可达货位时失败。");
            }

            WarehouseSimLog.Info(
                $"货位容量：可存储 {result.StorageSlotCount}，堆垛机可达可分配 {result.AllocatableStorageSlotCount}，" +
                $"初始占用 {_initialOccupiedStorageSlotCount}");

            if (SimFlowPlanResolver.RequiresOutboundPorts(plan)
                && (_conveyorTopology.OutfeedNodeIndices == null
                    || _conveyorTopology.OutfeedNodeIndices.Count == 0))
            {
                result.Success = false;
                result.Message = "流程计划含出库任务，但 ConveyorMap 未配置出库口节点。";
                result.WallClockProfile = _wallClockProfiler.BuildSnapshot();
                return result;
            }

            WarehouseSimLog.Info(
                $"仿真开始：计划 {_targetCount} 箱；流程={result.FlowPlanSummaryLabel}；" +
                $"堆垛机={result.StackerPlacementStrategyLabel}；" +
                $"入库口={result.InfeedPortSelectionStrategyLabel}；" +
                $"输送={result.ConveyorRoutingStrategyLabel}；" +
                $"入库口数 {_conveyorTopology.InfeedNodeIndices.Count}，" +
                $"出库口数 {_conveyorTopology.OutfeedNodeIndices?.Count ?? 0}");
            ScheduleFlowPlan();

            // 主循环：每次弹出队首事件 → 推进时钟 → 更新状态机 / 预约资源 / 排定后续事件
            using (_wallClockProfiler.Begin(SimWallClockProfilePhases.DesMainLoop))
            {
                while (_queue.TryDequeue(out var evt) && _eventCount < _bindings.MaxSimEvents)
                {
                    _eventCount++;
                    _eventTypeCounts[(int)evt.Type]++;
                    _clock.AdvanceTo(evt.Time);
                    Dispatch(evt);
                }
            }

            using (_wallClockProfiler.Begin(SimWallClockProfilePhases.PostDiagnostics))
            {
                var queuePending = _queue.Count;
                LastEventDiagnostics = BuildEventDiagnosticsSnapshot(queuePending);
                FinalizeFailureCounts(queuePending);
                result.FailureCounts = new Dictionary<SimJobFailureReason, int>(_failureCounts);
                var failureSummary = SimJobFailureReasonLabels.FormatSummary(result.FailureCounts);

                if (_eventCount >= _bindings.MaxSimEvents)
                {
                    var incomplete = CountIncompleteJobs();
                    if (incomplete > 0)
                    {
                        RecordFailure(SimJobFailureReason.SimAbortedMaxEvents, incomplete);
                        result.FailureCounts = new Dictionary<SimJobFailureReason, int>(_failureCounts);
                        failureSummary = SimJobFailureReasonLabels.FormatSummary(result.FailureCounts);
                    }

                    var diagnostics = SimEventDiagnosticsCollector.FormatPlainText(LastEventDiagnostics);
                    WarehouseSimLog.Error($"超过最大事件数 {_bindings.MaxSimEvents}，仿真中止。\n{diagnostics}");
                    result.Success = false;
                    result.Message =
                        $"超过最大事件数 {_bindings.MaxSimEvents}，仿真中止。\n失败统计：{failureSummary}\n{diagnostics}";
                }
                else if (_completedCount + _failedNoSlot + _failedNoCargo < _targetCount
                         || _inboundWaitingCount > 0
                         || _outboundWaitingCount > 0
                         || (_flowPlanScheduler != null && _flowPlanScheduler.HasPendingReleases()))
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
                    result.Message = _failedNoSlot + _failedNoCargo > 0
                        ? $"完成 {_completedCount} 单，{_failedNoSlot} 箱入库失败，{_failedNoCargo} 箱出库失败。失败统计：{failureSummary}"
                        : "仿真完成。";
                }

                result.CompletedJobCount = _completedCount;
                result.TotalSimSeconds = _clock.Now;
                result.Completions = new List<JobCompletionRecord>(_completions);
                result.ResourceWaitTotals = BuildWaitTotals();
                result.ComputeStatistics();
                if (_recordPlayback)
                {
                    using (_wallClockProfiler.Begin(SimWallClockProfilePhases.PostSortPlayback))
                    {
                        SortPlaybackEvents();
                    }

                    using (_wallClockProfiler.Begin(SimWallClockProfilePhases.PostSortSubTasks))
                    {
                        SortSubTasks();
                    }

                    result.ResourceUtilizations = SimResourceUtilizationBuilder.Build(
                        _subTasks,
                        result.TotalSimSeconds,
                        _bindings?.ConveyorMap,
                        _conveyorTopology);
                    RunPostSimulationSelfChecks(result);
                }
            }

            result.WallClockProfile = _wallClockProfiler.BuildSnapshot();
            WarehouseSimLog.Info(
                $"仿真结束：{(result.Success ? "成功" : "失败")} — {result.Message}；" +
                $"完成 {result.CompletedJobCount}/{result.TargetJobCount}，" +
                $"时长 {result.TotalSimSeconds:F1}s，吞吐 {result.ThroughputPerHour:F1} 箱/小时");
            return result;
        }

        #region 初始化
        /// <summary>重置时钟、队列、占用图与计数器；按场景播种初始占用率。</summary>
        private void Reset()
        {
            _clock.Reset();
            _queue.Clear();
            _reservations = new ReservationTable();
            _jobs.Clear();
            _completions.Clear();
            _playback.Clear();
            _subTasks.Clear();
            _nextSubTaskId = 0;
            _completedCount = 0;
            _failedNoSlot = 0;
            _failedNoCargo = 0;
            _failureCounts.Clear();
            _initialOccupiedStorageSlotCount = 0;
            _eventCount = 0;
            _eventTypeCounts = new int[Enum.GetValues(typeof(SimEventType)).Length];
            _conveyorTopology = null;

            _nextJobId = 0;
            _inboundExhaustedAtInfeed = false;
            _flowPlanScheduler = null;
            _inboundWaitingCount = 0;
            _outboundWaitingCount = 0;
            _outboundReservedSlots.Clear();
            _outboundStackerReleasedAfterPickup.Clear();
            _outboundPickupHolds.Clear();

            var physicalSlots = _slotAllocator.PhysicalSlotCount;
            var storageSlots = _slotAllocator.StorageSlotCount;
            _initialOccupiedStorageSlotCount = _slotAllocator.Occupied != null
                ? _slotAllocator.CountOccupiedStorageSlots(_slotAllocator.Occupied)
                : 0;
            WarehouseSimLog.Info(
                $"货位：可存储 {storageSlots} / 物理格 {physicalSlots}（排除 {physicalSlots - storageSlots} 格）；" +
                $"布局 {_slotAllocator.SlotLayoutDescription}");

            _infeedCargoOccupied = null;
            _infeedReservationCounts = null;
            _pickupReservationCounts = null;
            _conveyorScheduler = null;
            _infeedServiceFreeByPort = null;
            _infeedPortFeedScheduled = null;
            _infeedPortOrderScratch = null;
            _infeedRoundRobinCursor = 0;
            _infeedQueueFillScheduled = false;
            _outfeedQueueFillScheduled = false;
            _targetCount = 0;
        }

        /// <summary>为每个入库口初始化占用状态、取货点预定计数与入库服务时间轴。</summary>
        private void InitInfeedPorts()
        {
            var infeedCount = _conveyorTopology.InfeedNodeIndices.Count;
            _infeedCargoOccupied = new bool[infeedCount];
            _infeedReservationCounts = new int[infeedCount];
            _infeedServiceFreeByPort = new double[infeedCount];
            _infeedPortFeedScheduled = new bool[infeedCount];
            _infeedPortOrderScratch = new int[Math.Max(1, infeedCount)];

            var nodeCount = _conveyorTopology.Map.Nodes.Length;
            _pickupReservationCounts = new int[nodeCount];
            _stackerActiveJobCounts = new int[Math.Max(1, _bindings.StackerCount)];
            _stackerCarriage.Reset(_bindings.StackerCount);
            _conveyorScheduler = new ConveyorTransitScheduler(
                _conveyorTopology, _reservations, _bindings, _stackerCarriage);
            _conveyorSubTaskRecorder = new ConveyorSubTaskRecorder(_conveyorTopology, _bindings, RecordSubTask);
            InitOccupancyNotifier();
        }

        #endregion
        #region 事件调度

        /// <summary>
        /// 事件分发中枢：将 <see cref="SimEventType"/> 映射到各 partial 中的阶段处理函数。
        /// </summary>
        /// <remarks>
        /// <paramref name="evt"/>.Payload 含义因类型而异，例如入库口序号、已完成 zone 下标、占用释放序号等。
        /// 需要 JobId 的事件在找不到任务时静默跳过并打日志，避免脏数据导致仿真崩溃。
        /// </remarks>
        private void Dispatch(ScheduledSimEvent evt)
        {
            using (_wallClockProfiler.Begin(SimWallClockProfilePhases.Event(evt.Type)))
            {
                DispatchCore(evt);
            }
        }

        private void DispatchCore(ScheduledSimEvent evt)
        {
            switch (evt.Type)
            {
                // —— 流程计划 ——
                case SimEventType.FlowCargoRelease:
                    OnFlowCargoRelease(evt.Payload);
                    break;
                case SimEventType.FlowPlanBatchRelease:
                    OnFlowPlanBatchRelease(evt.Payload);
                    break;
                case SimEventType.FlowPlanInstantRelease:
                    OnFlowPlanInstantRelease(evt.Payload);
                    break;

                // —— 到货与入库口 ——
                case SimEventType.InfeedPortFeed:
                    OnInfeedPortFeed(evt.Payload);
                    break;
                case SimEventType.InfeedServiceComplete:
                    if (TryGetJob(evt.JobId, evt.Type, out var infeedCompleteJob))
                    {
                        OnInfeedComplete(infeedCompleteJob);
                    }

                    break;
                case SimEventType.InfeedPortPhysicalRelease:
                    if (TryGetJob(evt.JobId, evt.Type, out var releaseJob))
                    {
                        OnInfeedPortPhysicalRelease(releaseJob);
                    }
                    break;

                case SimEventType.OutfeedServiceComplete:
                    if (TryGetJob(evt.JobId, evt.Type, out var outfeedCompleteJob))
                    {
                        OnOutfeedComplete(outfeedCompleteJob);
                    }

                    break;
                case SimEventType.OutfeedPortPhysicalRelease:
                    if (TryGetJob(evt.JobId, evt.Type, out var outfeedReleaseJob))
                    {
                        OnOutfeedPortPhysicalRelease(outfeedReleaseJob);
                    }

                    break;
                case SimEventType.OutfeedPortDispatch:
                    OnOutfeedPortDispatch(evt.Payload);
                    break;
                // —— 资源释放唤醒（OccupancyNotify partial）——
                case SimEventType.OccupancyReleased:
                    OnOccupancyReleased(evt.Payload);
                    break;

                // —— 输送（Conveyor partial）——
                case SimEventType.ConveyorRouteRetry:
                    OnConveyorRouteRetryDispatched(evt.JobId);
                    if (TryGetJob(evt.JobId, evt.Type, out var retryJob))
                    {
                        TryBeginConveyor(retryJob);
                    }

                    break;
                case SimEventType.ConveyorSegmentComplete:
                case SimEventType.ConveyorZoneComplete:
                    if (TryGetJob(evt.JobId, evt.Type, out var zoneJob))
                    {
                        OnConveyorZoneComplete(zoneJob, evt.Payload);
                    }

                    break;
                case SimEventType.ConveyorTransitComplete:
                    if (TryGetJob(evt.JobId, evt.Type, out var conveyorJob))
                    {
                        OnConveyorComplete(conveyorJob);
                    }

                    break;

                // —— 堆垛机（Stacker partial）——
                case SimEventType.StackerApproachComplete:
                    if (TryGetJob(evt.JobId, evt.Type, out var approachJob))
                    {
                        OnStackerApproachComplete(approachJob);
                    }

                    break;
                case SimEventType.StackerPickComplete:
                    if (TryGetJob(evt.JobId, evt.Type, out var pickJob))
                    {
                        OnStackerPickComplete(pickJob);
                    }

                    break;
                case SimEventType.StackerMoveComplete:
                    if (TryGetJob(evt.JobId, evt.Type, out var moveJob))
                    {
                        OnStackerMoveComplete(moveJob);
                    }

                    break;
                case SimEventType.StackerPlaceComplete:
                    if (TryGetJob(evt.JobId, evt.Type, out var placeJob))
                    {
                        OnStackerPlaceComplete(placeJob);
                    }

                    break;
                case SimEventType.OutboundPickupDeparture:
                    if (TryGetJob(evt.JobId, evt.Type, out var outboundDepartJob))
                    {
                        ReleaseOutboundStackerAfterPickupDeparture(outboundDepartJob);
                    }

                    break;
                case SimEventType.JobCompleted:
                    if (TryGetJob(evt.JobId, evt.Type, out var completedJob))
                    {
                        OnJobCompleted(completedJob);
                    }

                    break;

                // 预留：入库服务开始相位（当前由 BeginInfeed 直接排定 Complete）
                case SimEventType.InfeedServiceStart:
                    break;
            }
        }

        /// <summary>按 JobId 查找任务；仿真异常或提前结束时可能缺失。</summary>
        private bool TryGetJob(int jobId, SimEventType eventType, out WarehouseJob job)
        {
            if (_jobs.TryGetValue(jobId, out job))
            {
                return true;
            }

            WarehouseSimLog.Warn($"未知 JobId={jobId}，跳过事件 {eventType}。");
            job = null;
            return false;
        }

        #endregion
        
        /// <summary>构建输送拓扑并校验入库口。</summary>
        private bool ValidateTopology(out string error)
        {
            error = null;

            if (_bindings.ConveyorMap == null)
            {
                error = "硬件绑定未配置 ConveyorMap。";
                return false;
            }

            if (!ConveyorMapTopology.TryBuild(_bindings.ConveyorMap, _bindings, out _conveyorTopology, out error))
            {
                _conveyorTopology = null;
                return false;
            }

            var plan = SimFlowPlanResolver.Resolve(_scenario);
            if (SimFlowPlanResolver.RequiresInboundPorts(plan)
                && _conveyorTopology.InfeedNodeIndices.Count == 0)
            {
                error = "流程计划含入库任务，但 ConveyorMap 中未配置入库口节点。";
                _conveyorTopology = null;
                return false;
            }

            _conveyorTopology.WarmTransitShortestPathCache();
            return true;
        }
    }
}
