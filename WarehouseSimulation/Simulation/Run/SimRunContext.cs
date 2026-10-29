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
    /// 一次运行的共享事实：时钟、队列、任务、预约表和时间轴。
    /// 阶段之间不互相引用，要继续下一步就调用这里的交接方法，由它转给对应进程。
    /// 必须在当前事件里做完的交接保持同步，避免打乱同一时刻的原有顺序。
    /// </summary>
    internal sealed class SimRunContext
    {
        private readonly FlowPlanProcess _flow;
        private readonly InfeedProcess _infeed;
        private readonly OutfeedProcess _outfeed;
        private readonly StackerProcess _stacker;
        private readonly OccupancyWakeProcess _occupancy;
        private readonly ConveyorProcess _conveyor;

        public SimRunContext(ISlotAllocator slotAllocator)
        {
            SlotAllocator = slotAllocator ?? throw new ArgumentNullException(nameof(slotAllocator));
            Timeline = new SimTimeline(this);
            _flow = new FlowPlanProcess(this);
            _infeed = new InfeedProcess(this);
            _outfeed = new OutfeedProcess(this);
            _stacker = new StackerProcess(this);
            _occupancy = new OccupancyWakeProcess(this);
            _conveyor = new ConveyorProcess(this);
            Processes = new ISimProcess[] { _flow, _infeed, _conveyor, _stacker, _outfeed, _occupancy };
        }

        public ISlotAllocator SlotAllocator { get; }

        public IWarehouseSimulationBindings Bindings { get; set; }

        public WarehouseSimStrategyProfile Strategy { get; set; }

        public WarehouseSimScenario Scenario { get; set; }

        public ConveyorMapTopology Topology { get; set; }

        public SimClock Clock { get; } = new();

        public SimEventQueue Queue { get; } = new();

        public ReservationTable Reservations { get; set; } = new();

        public Dictionary<int, WarehouseJob> Jobs { get; } = new();

        public List<JobCompletionRecord> Completions { get; } = new();

        public Dictionary<SimJobFailureReason, int> FailureCounts { get; } = new();

        public SimTimeline Timeline { get; }

        public SimWallClockProfiler WallClockProfiler { get; } = new();

        public SimRunLiveProgress LiveProgress { get; set; }

        public bool ShouldRecordPlayback { get; set; }

        public ISimProcess[] Processes { get; }

        public int TargetCount { get; set; }

        public int InboundTargetCount { get; set; }

        public int InboundCompletedCount { get; set; }

        public int NextJobId { get; set; }

        public bool InboundExhaustedAtInfeed { get; set; }

        public int FailedNoCargo { get; set; }

        public int FailedNoSlot { get; set; }

        public int CompletedCount { get; set; }

        public int InitialOccupiedStorageSlotCount { get; set; }

        public int EventCount { get; set; }

        public int[] EventTypeCounts { get; set; }

        public int InboundWaitingCount
        {
            get => _flow.InboundWaitingCount;
            set => _flow.InboundWaitingCount = value;
        }

        public int OutboundWaitingCount
        {
            get => _flow.OutboundWaitingCount;
            set => _flow.OutboundWaitingCount = value;
        }

        public int[] StackerActiveJobCounts => _stacker.ActiveJobCounts;

        public int[] PickupReservationCounts => _conveyor.PickupReservationCounts;

        public void ResetShared()
        {
            Clock.Reset();
            Queue.Clear();
            Reservations = new ReservationTable();
            Jobs.Clear();
            Completions.Clear();
            Timeline.Clear();
            FailureCounts.Clear();
            TargetCount = 0;
            InboundTargetCount = 0;
            InboundCompletedCount = 0;
            NextJobId = 0;
            InboundExhaustedAtInfeed = false;
            FailedNoCargo = 0;
            FailedNoSlot = 0;
            CompletedCount = 0;
            InitialOccupiedStorageSlotCount = 0;
            EventCount = 0;
            EventTypeCounts = new int[Enum.GetValues(typeof(SimEventType)).Length];
            Topology = null;
            _flow.Reset();
        }

        public void InitPorts()
        {
            _infeed.InitPorts();
            _conveyor.InitPickupCounts();
            _stacker.InitFleet();
            _occupancy.Init();
            _outfeed.InitPorts();
        }

        public void InitFlow() => _flow.Init();

        public void InitConveyorKernel() => _conveyor.InitKernel();

        public void ScheduleFlowPlan() => _flow.Schedule();

        public bool FlowHasPendingReleases() => _flow.HasPendingReleases();

        public void ReportKernelDiagnostics() => _conveyor.ReportKernelDiagnostics();

        public void ReportLiveProgress()
        {
            LiveProgress?.ReportTick(
                Clock.Now,
                EventCount,
                CompletedCount,
                TargetCount,
                Queue.Count,
                InboundCompletedCount,
                InboundTargetCount);
        }

        public bool TryGetJob(int jobId, SimEventType eventType, out WarehouseJob job)
        {
            if (Jobs.TryGetValue(jobId, out job))
            {
                return true;
            }

            WarehouseSimLog.Warn($"未知 JobId={jobId}，跳过事件 {eventType}。");
            job = null;
            return false;
        }

        public void RecordFailure(SimJobFailureReason reason, int count = 1)
        {
            if (count <= 0)
            {
                return;
            }

            if (!FailureCounts.ContainsKey(reason))
            {
                FailureCounts[reason] = 0;
            }

            FailureCounts[reason] += count;
        }

        public int CountIncompleteJobs() =>
            Math.Max(0, TargetCount - CompletedCount - FailedNoSlot - FailedNoCargo);

        public void FinalizeFailureCounts()
        {
            var incomplete = Math.Max(0, TargetCount - CompletedCount - FailedNoSlot - FailedNoCargo);
            if (incomplete <= 0)
            {
                return;
            }

            if (EventCount >= Bindings?.MaxSimEvents)
            {
                return;
            }

            RecordFailure(SimJobFailureReason.SimIncompleteRemaining, incomplete);
        }

        public void RecordPlayback(WarehouseJob job, SimPlaybackPhase phase, int stackerId, GridIndex slot) =>
            Timeline.RecordPlayback(job, phase, stackerId, slot);

        public void RecordPlaybackAt(
            double simTime,
            WarehouseJob job,
            SimPlaybackPhase phase,
            int stackerId,
            GridIndex slot,
            bool attachPathContext = false) =>
            Timeline.RecordPlaybackAt(simTime, job, phase, stackerId, slot, attachPathContext);

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
            float stackerToForkDepthMeters = 0f) =>
            Timeline.RecordSubTask(
                job,
                kind,
                startSimTime,
                endSimTime,
                stackerId,
                slot,
                fromNodeIndex,
                toNodeIndex,
                segmentSlotIndex,
                attachPathContext,
                stackerFromSlot,
                stackerToSlot,
                stackerRailColumn,
                hasStackerPose,
                stackerToUsesForkDepth,
                stackerToForkDepthMeters);

        public void EnsurePlaybackSnapshot(WarehouseJob job) => Timeline.EnsurePlaybackSnapshot(job);

        public void ScheduleInitialInfeedFeeds() => _infeed.ScheduleInitialFeeds();

        public void ScheduleInfeedQueueFill(double when) => _infeed.ScheduleQueueFill(when);

        public void ScheduleInfeedPortPhysicalRelease(WarehouseJob job, double releaseTime) =>
            _infeed.SchedulePhysicalRelease(job, releaseTime);

        public void ScheduleInitialOutfeedDispatches() => _outfeed.ScheduleInitialDispatches();

        public void ScheduleOutfeedQueueFill(double when) => _outfeed.ScheduleQueueFill(when);

        public bool TryBeginConveyor(WarehouseJob job) => _conveyor.TryBegin(job);

        public void TryBeginStackerPhase(WarehouseJob job) => _stacker.TryBegin(job);

        public void BeginOutfeed(WarehouseJob job) => _outfeed.Begin(job);

        public void OnOutboundStackerPlaceComplete(WarehouseJob job) => _outfeed.OnStackerPlaceComplete(job);

        public void ReleaseOutboundStackerAfterPickupDeparture(WarehouseJob job) =>
            _outfeed.ReleaseStackerAfterPickupDeparture(job);

        public bool TryClaimInfeedOrigin(int portIndex, int ownerId) =>
            _conveyor.TryClaimInfeedOrigin(portIndex, ownerId);

        public void ReleaseInfeedOriginClaim(WarehouseJob job) => _conveyor.ReleaseInfeedOriginClaim(job);

        public bool TryClaimOutboundPickup(WarehouseJob job) => _conveyor.TryClaimOutboundPickup(job);

        public void ReleasePrimaryGoal(WarehouseJob job) => _conveyor.ReleasePrimaryGoal(job);

        public void ReleasePrimaryOutfeed(WarehouseJob job) => _conveyor.ReleasePrimaryOutfeed(job);

        public void ReleaseFailedOutboundPickup(WarehouseJob job) => _conveyor.ReleaseFailedOutboundPickup(job);

        public void ClearConveyorRoutePending(WarehouseJob job) => _occupancy.ClearPending(job);

        public void MarkConveyorRoutePending(WarehouseJob job, params string[] blockingResources) =>
            _occupancy.MarkPending(job, blockingResources);

        public bool TryAcceptConveyorRouteRetry(int jobId, int token) => _occupancy.TryAcceptRetry(jobId, token);

        public void OnConveyorRouteRetryDispatched(int jobId) => _occupancy.OnRetryDispatched(jobId);

        public void RetryInboundConveyorRouteWaiters() => _occupancy.RetryInboundWaiters();

        public void RetryOutboundConveyorRouteWaiters() => _occupancy.RetryOutboundWaiters();

        public bool TryCommitOutboundConveyorPath(WarehouseJob job) => _outfeed.TryCommitPath(job);

        public void FailJobNoSlot(WarehouseJob job, SimJobFailureReason reason, bool releaseResources = false) =>
            _infeed.FailNoSlot(job, reason, releaseResources);

        public void FailJobNoCargo(WarehouseJob job, SimJobFailureReason reason, bool releaseResources = false) =>
            _outfeed.FailNoCargo(job, reason, releaseResources);

        public void IncrementStackerActiveJobCount(int stackerId) => _stacker.IncrementActive(stackerId);

        public void DecrementStackerActiveJobCount(int stackerId) => _stacker.DecrementActive(stackerId);

        public void SetStackerCarriagePosition(int stackerId, int row, int level) =>
            _stacker.SetCarriagePosition(stackerId, row, level);

        public void ReleaseAllocatedSlot(WarehouseJob job)
        {
            if (!job.HasSlot)
            {
                return;
            }

            SlotAllocator.Release(job.TargetSlot);
            job.HasSlot = false;
            DecrementStackerActiveJobCount(job.AssignedStackerId);
        }

        public void DequeueInboundFlowEntry() => _flow.DequeueInbound();

        public void ClearInboundFlowEntries() => _flow.ClearInbound();

        public void DequeueOutboundFlowEntry() => _flow.DequeueOutbound();

        public void ClearOutboundFlowEntries() => _flow.ClearOutbound();

        public string[] PeekInboundProcessTags() => _flow.PeekInboundTags();

        public string[] PeekOutboundProcessTags() => _flow.PeekOutboundTags();

        public int[] PeekInboundWarehouseIds() => _flow.PeekInboundWarehouseIds();

        public int[] PeekOutboundWarehouseIds() => _flow.PeekOutboundWarehouseIds();

        public SimEventDiagnostics BuildEventDiagnostics(int queuePending)
        {
            var diag = new SimEventDiagnostics
            {
                DispatchedEventCount = EventCount,
                QueuePendingCount = queuePending,
                MaxSimEvents = Bindings?.MaxSimEvents ?? 0,
                SimTimeSeconds = Clock.Now,
                CompletedJobCount = CompletedCount,
                FailedJobCount = FailedNoSlot + FailedNoCargo,
                TargetJobCount = TargetCount,
            };

            foreach (var entry in EventTypeCounts
                         .Select((count, index) => (Type: (SimEventType)index, Count: count))
                         .Where(x => x.Count > 0)
                         .OrderByDescending(x => x.Count))
            {
                var pct = EventCount > 0 ? entry.Count * 100.0 / EventCount : 0;
                diag.EventTypeStats.Add(new SimEventTypeStat
                {
                    Type = entry.Type,
                    Count = entry.Count,
                    Percent = pct,
                });
            }

            var stateCounts = new Dictionary<WarehouseJobState, int>();
            foreach (var job in Jobs.Values)
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

            _infeed.FillDiagnostics(diag);
            _outfeed.FillDiagnostics(diag);
            _conveyor.FillPickupDiagnostics(diag);

            if (SlotAllocator.Occupied != null)
            {
                diag.TotalSlotCount = SlotAllocator.StorageSlotCount;
                diag.OccupiedSlotCount = SlotAllocator.CountOccupiedStorageSlots(SlotAllocator.Occupied);
                diag.AllocatableStorageSlotCount = SlotAllocator.CountAllocatableStorageSlots(Bindings, Topology);
            }

            foreach (var kv in FailureCounts.OrderByDescending(x => x.Value))
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

        public static string[] CopyProcessTags(string[] source)
        {
            if (source == null || source.Length == 0)
            {
                return null;
            }

            var copy = new string[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                copy[i] = source[i];
            }

            return copy;
        }
    }
}
