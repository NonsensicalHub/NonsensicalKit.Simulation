using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>选路、取货点预定，并把整条 zone 链交给 <see cref="ConveyorKernelSession"/>。</summary>
    internal sealed class ConveyorProcess : ISimProcess
    {
        private readonly SimRunContext _ctx;
        private readonly ConveyorKernelSession _kernel;
        private int[] _pickupReservationCounts;

        public ConveyorProcess(SimRunContext ctx)
        {
            _ctx = ctx;
            _kernel = new ConveyorKernelSession(ctx);
        }

        internal int[] PickupReservationCounts => _pickupReservationCounts;

        public bool Handles(SimEventType type) =>
            type is SimEventType.ConveyorRouteRetry
                or SimEventType.ConveyorTransitComplete
                or SimEventType.ConveyorKernelTick;

        public void OnEvent(ScheduledSimEvent evt)
        {
            switch (evt.Type)
            {
                case SimEventType.ConveyorRouteRetry:
                    if (!_ctx.TryAcceptConveyorRouteRetry(evt.JobId, evt.Payload))
                    {
                        return;
                    }

                    _ctx.OnConveyorRouteRetryDispatched(evt.JobId);
                    if (_ctx.TryGetJob(evt.JobId, evt.Type, out var retryJob))
                    {
                        TryBegin(retryJob);
                    }

                    break;
                case SimEventType.ConveyorTransitComplete:
                    if (_ctx.TryGetJob(evt.JobId, evt.Type, out var conveyorJob))
                    {
                        OnTransitComplete(conveyorJob);
                    }

                    break;
                case SimEventType.ConveyorKernelTick:
                    _kernel.OnTick();
                    break;
            }
        }

        internal void InitPickupCounts()
        {
            var nodeCount = _ctx.Topology.Map.Nodes.Length;
            _pickupReservationCounts = new int[nodeCount];
        }

        internal void InitKernel() => _kernel.Init();

        internal void ReportKernelDiagnostics() => _kernel.ReportDiagnostics();

        internal void FillPickupDiagnostics(SimEventDiagnostics diag)
        {
            if (_pickupReservationCounts == null || _ctx.Topology?.PickupNodeIndices == null)
            {
                return;
            }

            foreach (var pickupIndex in _ctx.Topology.PickupNodeIndices)
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

                ref var node = ref _ctx.Topology.GetNode(pickupIndex);
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

        internal static bool IsTransitCommitted(WarehouseJob job) =>
            job.ConveyorPathZones != null && job.ConveyorPathZones.Count > 0;

        internal bool TryBegin(WarehouseJob job)
        {
            if (job.State == WarehouseJobState.FailedNoSlot
                || job.State == WarehouseJobState.FailedNoCargo
                || job.State == WarehouseJobState.Completed)
            {
                return false;
            }

            if (_kernel.IsActive && _kernel.IsSubmitted(job.JobId))
            {
                return true;
            }

            if (TrySchedule(job))
            {
                _ctx.ClearConveyorRoutePending(job);
                return true;
            }

            if (IsTransitCommitted(job))
            {
                _ctx.MarkConveyorRoutePending(job);
                return false;
            }

            if (job.Direction == SimFlowDirection.Outbound)
            {
                if (job.PickupPointIndex < 0
                    || _ctx.Topology?.OutfeedNodeIndices == null
                    || _ctx.Topology.OutfeedNodeIndices.Count == 0)
                {
                    _ctx.FailJobNoCargo(job, SimJobFailureReason.OutboundJobNoInventory, releaseResources: false);
                    return false;
                }

                _ctx.MarkConveyorRoutePending(job);
                return false;
            }

            if (!job.InboundRouteReachableChecked)
            {
                job.InboundRouteReachableChecked = true;
                if (!ConveyorPathPlanner.TrySelectBestPath(
                        _ctx.Topology,
                        _ctx.Bindings,
                        _ctx.Strategy,
                        _ctx.Reservations,
                        pickupReservationCounts: null,
                        job.InfeedPortIndex,
                        job.TargetSlot,
                        job.AssignedStackerId,
                        _ctx.Clock.Now,
                        out _,
                        out _,
                        out _,
                        job))
                {
                    _ctx.FailJobNoSlot(job, SimJobFailureReason.InboundConveyorRouteUnreachable, releaseResources: true);
                    return false;
                }
            }

            _ctx.MarkConveyorRoutePending(job);
            return false;
        }

        internal bool TryClaimInfeedOrigin(int portIndex, int ownerId) =>
            _kernel.TryClaimInfeedOrigin(portIndex, ownerId);

        internal void ReleaseInfeedOriginClaim(WarehouseJob job) => _kernel.ReleaseInfeedOriginClaim(job);

        internal bool TryClaimOutboundPickup(WarehouseJob job) => _kernel.TryClaimOutboundPickup(job);

        internal void ReleasePrimaryGoal(WarehouseJob job) => _kernel.ReleaseGoal(job);

        internal void ReleasePrimaryOutfeed(WarehouseJob job) => _kernel.ReleaseOutfeed(job);

        internal void ReleaseFailedOutboundPickup(WarehouseJob job) => _kernel.ReleaseFailedOutboundPickup(job);

        internal void NotifyRouteWaiters()
        {
            _ctx.RetryInboundConveyorRouteWaiters();
            _ctx.RetryOutboundConveyorRouteWaiters();
            _ctx.ScheduleInfeedQueueFill(_ctx.Clock.Now);
        }

        internal void IncrementPickup(int pickupNodeIndex)
        {
            if (_pickupReservationCounts == null
                || pickupNodeIndex < 0
                || pickupNodeIndex >= _pickupReservationCounts.Length)
            {
                return;
            }

            _pickupReservationCounts[pickupNodeIndex]++;
        }

        internal void DecrementPickup(int pickupNodeIndex)
        {
            if (_pickupReservationCounts == null
                || pickupNodeIndex < 0
                || pickupNodeIndex >= _pickupReservationCounts.Length)
            {
                return;
            }

            _pickupReservationCounts[pickupNodeIndex] =
                System.Math.Max(0, _pickupReservationCounts[pickupNodeIndex] - 1);
            NotifyRouteWaiters();
        }

        private bool TrySchedule(WarehouseJob job)
        {
            if (!_kernel.IsActive)
            {
                WarehouseSimLog.Error($"[ConveyorKernel] job={job.JobId} 输送内核未初始化（ConveyorMap 缺失？），无法调度输送。");
                return false;
            }

            return TrySubmit(job);
        }

        private bool TrySubmit(WarehouseJob job)
        {
            if (_kernel.IsSubmitted(job.JobId))
            {
                return true;
            }

            if (!TryCommitPath(job))
            {
                return false;
            }

            if (!_kernel.TrySubmitPlaced(job))
            {
                return false;
            }

            job.State = WarehouseJobState.OnConveyor;
            job.NextConveyorZoneIndex = job.ConveyorPathZones.Count;
            _kernel.Pump();
            return true;
        }

        private bool TryCommitPath(WarehouseJob job)
        {
            if (job.Direction == SimFlowDirection.Outbound)
            {
                return _ctx.TryCommitOutboundConveyorPath(job);
            }

            if (job.ConveyorPathZones != null && job.ConveyorPathZones.Count > 0)
            {
                return true;
            }

            bool pathSelected;
            using (_ctx.WallClockProfiler.Begin(SimWallClockProfilePhases.ConveyorPathPlan))
            {
                pathSelected = ConveyorPathPlanner.TrySelectBestPath(
                    _ctx.Topology,
                    _ctx.Bindings,
                    _ctx.Strategy,
                    _ctx.Reservations,
                    _pickupReservationCounts,
                    job.InfeedPortIndex,
                    job.TargetSlot,
                    job.AssignedStackerId,
                    _ctx.Clock.Now,
                    out var pickup,
                    out var stackerId,
                    out var path,
                    job);
                if (!pathSelected)
                {
                    return false;
                }

                job.PickupPointIndex = pickup;
                job.AssignedStackerId = stackerId;
                job.ConveyorPathNodeIndices = path;
                job.ConveyorSegmentSchedule ??= new List<ConveyorSegmentScheduleEntry>();
                job.ConveyorSegmentSchedule.Clear();
                job.ConveyorEdgeSubTasksRecorded = new HashSet<int>();
                job.ConveyorPathZones = ConveyorPathZoneChainBuilder.Build(
                    path,
                    _ctx.Topology,
                    _ctx.Bindings.ConveyorMap);
                if (job.ConveyorPathZones == null || job.ConveyorPathZones.Count == 0)
                {
                    WarehouseSimLog.Warn($"输送 zone 链为空 job={job.JobId}，请检查 ConveyorMap。");
                    return false;
                }

                job.NextConveyorZoneIndex = 0;
                IncrementPickup(pickup);
                WarehouseSimLog.Info(() =>
                    $"输送路径选定 job={job.JobId} pickup={pickup} stacker={stackerId} " +
                    $"路径节点={path.Count} zone数={job.ConveyorPathZones.Count}");
            }

            return true;
        }

        private void OnTransitComplete(WarehouseJob job)
        {
            if (job.State != WarehouseJobState.OnConveyor)
            {
                return;
            }

            _ctx.RecordPlayback(job, SimPlaybackPhase.ConveyorDone, job.AssignedStackerId, job.TargetSlot);
            if (job.Direction == SimFlowDirection.Outbound)
            {
                _ctx.BeginOutfeed(job);
                return;
            }

            job.State = WarehouseJobState.WaitingStacker;
            DecrementPickup(job.PickupPointIndex);
            _ctx.TryBeginStackerPhase(job);
        }
    }
}
