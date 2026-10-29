using System;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>堆垛机驶向、取货、移动、放货。负载计数和载货台账只留在这里。</summary>
    internal sealed class StackerProcess : ISimProcess
    {
        private readonly SimRunContext _ctx;
        private StackerCarriageBookkeeper _carriage = new();
        private int[] _activeJobCounts;

        public StackerProcess(SimRunContext ctx) => _ctx = ctx;

        internal int[] ActiveJobCounts => _activeJobCounts;

        internal StackerCarriageBookkeeper Carriage => _carriage;

        public bool Handles(SimEventType type) =>
            type is SimEventType.StackerApproachComplete
                or SimEventType.StackerPickComplete
                or SimEventType.StackerMoveComplete
                or SimEventType.StackerPlaceComplete;

        public void OnEvent(ScheduledSimEvent evt)
        {
            if (!_ctx.TryGetJob(evt.JobId, evt.Type, out var job))
            {
                return;
            }

            switch (evt.Type)
            {
                case SimEventType.StackerApproachComplete:
                    job.State = WarehouseJobState.StackerPick;
                    break;
                case SimEventType.StackerPickComplete:
                    OnPickComplete(job);
                    break;
                case SimEventType.StackerMoveComplete:
                    job.State = WarehouseJobState.StackerPlace;
                    _ctx.RecordPlayback(job, SimPlaybackPhase.StackerMove, job.AssignedStackerId, job.TargetSlot);
                    break;
                case SimEventType.StackerPlaceComplete:
                    OnPlaceComplete(job);
                    break;
            }
        }

        internal void InitFleet()
        {
            _activeJobCounts = new int[Math.Max(1, _ctx.Bindings.StackerCount)];
            _carriage.Reset(_ctx.Bindings.StackerCount);
        }

        internal void IncrementActive(int stackerId)
        {
            if (_activeJobCounts == null || stackerId < 0 || stackerId >= _activeJobCounts.Length)
            {
                return;
            }

            _activeJobCounts[stackerId]++;
        }

        internal void DecrementActive(int stackerId)
        {
            if (_activeJobCounts == null || stackerId < 0 || stackerId >= _activeJobCounts.Length)
            {
                return;
            }

            _activeJobCounts[stackerId] = Math.Max(0, _activeJobCounts[stackerId] - 1);
        }

        internal void SetCarriagePosition(int stackerId, int row, int level) =>
            _carriage.SetCarriagePosition(stackerId, row, level);

        internal void TryBegin(WarehouseJob job)
        {
            if (job.Direction == SimFlowDirection.Outbound)
            {
                TryBeginOutbound(job);
                return;
            }

            if (job.State != WarehouseJobState.WaitingStacker
                || !job.HasSlot
                || job.AssignedStackerId < 0)
            {
                return;
            }

            var pickupNode = _ctx.Topology.GetNode(job.PickupPointIndex);
            var plan = StackerWorkPlanner.PlanInbound(
                _ctx.Bindings, _ctx.Topology, _carriage, job, pickupNode);
            Begin(job, plan);
        }

        private void TryBeginOutbound(WarehouseJob job)
        {
            if (job.State != WarehouseJobState.WaitingStacker
                || !job.HasSlot
                || job.AssignedStackerId < 0
                || job.PickupPointIndex < 0)
            {
                return;
            }

            if (!_ctx.TryClaimOutboundPickup(job))
            {
                return;
            }

            var pickupNode = _ctx.Topology.GetNode(job.PickupPointIndex);
            var plan = StackerWorkPlanner.PlanOutbound(
                _ctx.Bindings, _ctx.Topology, _carriage, job, pickupNode);
            Begin(job, plan);
        }

        private void Begin(WarehouseJob job, in StackerWorkPlan plan)
        {
            var stackerId = job.AssignedStackerId;
            var total = plan.TotalSeconds;
            var resources = BuildResources(job, stackerId, job.TargetSlot.Column);
            var poses = plan.Poses;
            var desired = _ctx.Clock.Now;
            double actualStart;
            if (HasPlannedReservation(job, desired, total))
            {
                actualStart = job.StackerReserveStart;
            }
            else if (resources.Length > 0)
            {
                actualStart = _ctx.Reservations.ReserveAtEarliestAll(desired, total, resources);
            }
            else
            {
                actualStart = desired;
            }

            var approachFrom = plan.StartPose;
            var wait = actualStart - desired;
            if (wait > 1e-9)
            {
                job.WaitTimeAccum += wait;
                job.LastWaitResource = _ctx.Reservations.GetLatestBlockingResourceId(desired, resources)
                    ?? job.AisleResourceId
                    ?? job.StackerResourceId;
                _ctx.RecordPlayback(job, SimPlaybackPhase.StackerWait, stackerId, job.TargetSlot);
                _ctx.RecordSubTask(
                    job,
                    SimSubTaskKind.StackerWait,
                    desired,
                    actualStart,
                    stackerId,
                    job.TargetSlot,
                    stackerFromSlot: approachFrom,
                    stackerToSlot: approachFrom,
                    stackerRailColumn: poses.RailColumn,
                    hasStackerPose: true);
            }

            job.StackerResourceId = SimEntityNaming.StackerResourceId(stackerId);
            job.AisleResourceId = _ctx.Bindings.UseAisleColumnReservation
                ? SimEntityNaming.AisleResourceId(job.TargetSlot.WarehouseId, job.TargetSlot.Column)
                : null;
            job.StackerReserveStart = actualStart;
            job.StackerReserveEnd = actualStart + total;
            job.PhaseStartTime = actualStart;
            job.ScheduledCompleteTime = job.StackerReserveEnd;

            var cursor = actualStart;
            var needsApproach = !StackerSubTaskPoseBuilder.CarriagePosesEqual(approachFrom, poses.ApproachTo);
            if (needsApproach && plan.ApproachSeconds > 1e-6f)
            {
                job.State = WarehouseJobState.StackerApproach;
                job.ServiceTimeAccum += plan.ApproachSeconds;
                var approachEnd = cursor + plan.ApproachSeconds;
                _ctx.RecordPlayback(job, SimPlaybackPhase.StackerApproach, stackerId, job.TargetSlot);
                _ctx.RecordSubTask(
                    job,
                    SimSubTaskKind.StackerApproach,
                    cursor,
                    approachEnd,
                    stackerId,
                    job.TargetSlot,
                    stackerFromSlot: approachFrom,
                    stackerToSlot: poses.ApproachTo,
                    stackerRailColumn: poses.RailColumn,
                    hasStackerPose: true);
                _ctx.Queue.Enqueue(new ScheduledSimEvent(approachEnd, SimEventType.StackerApproachComplete, job.JobId));
                cursor = approachEnd;
            }
            else
            {
                job.State = WarehouseJobState.StackerPick;
            }

            job.ServiceTimeAccum += plan.PickSeconds;
            var pickEnd = cursor + plan.PickSeconds;
            _ctx.RecordSubTask(
                job,
                SimSubTaskKind.StackerPick,
                cursor,
                pickEnd,
                stackerId,
                job.TargetSlot,
                stackerFromSlot: poses.PickFrom,
                stackerToSlot: poses.PickTo,
                stackerRailColumn: poses.RailColumn,
                hasStackerPose: true,
                stackerToUsesForkDepth: plan.PickUsesForkDepth,
                stackerToForkDepthMeters: plan.PickForkDepthMeters);
            _ctx.Queue.Enqueue(new ScheduledSimEvent(pickEnd, SimEventType.StackerPickComplete, job.JobId));

            job.ServiceTimeAccum += plan.MoveSeconds;
            var moveEnd = pickEnd + plan.MoveSeconds;
            _ctx.RecordSubTask(
                job,
                SimSubTaskKind.StackerMove,
                pickEnd,
                moveEnd,
                stackerId,
                job.TargetSlot,
                stackerFromSlot: poses.MoveFrom,
                stackerToSlot: poses.MoveTo,
                stackerRailColumn: poses.RailColumn,
                hasStackerPose: true);
            _ctx.Queue.Enqueue(new ScheduledSimEvent(moveEnd, SimEventType.StackerMoveComplete, job.JobId));

            job.ServiceTimeAccum += plan.PlaceSeconds;
            var placeEnd = moveEnd + plan.PlaceSeconds;
            _ctx.RecordSubTask(
                job,
                SimSubTaskKind.StackerPlace,
                moveEnd,
                placeEnd,
                stackerId,
                job.TargetSlot,
                stackerFromSlot: poses.PlaceFrom,
                stackerToSlot: poses.PlaceTo,
                stackerRailColumn: poses.RailColumn,
                hasStackerPose: true,
                stackerToUsesForkDepth: plan.PlaceUsesForkDepth,
                stackerToForkDepthMeters: plan.PlaceForkDepthMeters);
            _ctx.Queue.Enqueue(new ScheduledSimEvent(placeEnd, SimEventType.StackerPlaceComplete, job.JobId));

            _carriage.CommitBooking(stackerId, placeEnd, plan.EndRow, plan.EndLevel);
            WarehouseSimLog.Detail(
                $"堆垛机计划 job={job.JobId} stacker={stackerId} 仓库={plan.WarehouseId} " +
                $"起点=R{plan.StartRow}L{plan.StartLevel} 终点=R{plan.EndRow}L{plan.EndLevel} " +
                $"t={actualStart:F3}-{placeEnd:F3} " +
                $"取货交接={StackerCargoHandover.FromPhase(cursor, pickEnd):F3} " +
                $"放货交接={StackerCargoHandover.FromPhase(moveEnd, placeEnd):F3}");
        }

        private string[] BuildResources(WarehouseJob job, int stackerId, int column)
        {
            var useStacker = _ctx.Bindings.UseStackerReservation;
            var useAisle = _ctx.Bindings.UseAisleColumnReservation;
            var aisleId = SimEntityNaming.AisleResourceId(job.TargetSlot.WarehouseId, column);
            if (useStacker && useAisle)
            {
                return new[] { SimEntityNaming.StackerResourceId(stackerId), aisleId };
            }

            if (useStacker)
            {
                return new[] { SimEntityNaming.StackerResourceId(stackerId) };
            }

            if (useAisle)
            {
                return new[] { aisleId };
            }

            return Array.Empty<string>();
        }

        private static bool HasPlannedReservation(WarehouseJob job, double desiredArriveTime, float workDuration)
        {
            if (job.StackerReserveEnd <= job.StackerReserveStart + 1e-9)
            {
                return false;
            }

            if (Math.Abs(desiredArriveTime - job.ScheduledCompleteTime) > 0.05)
            {
                return false;
            }

            return Math.Abs(job.StackerReserveEnd - job.StackerReserveStart - workDuration) <= 0.05;
        }

        private void OnPickComplete(WarehouseJob job)
        {
            if (job.Direction == SimFlowDirection.Outbound && job.AssignedStackerId >= 0)
            {
                _carriage.SetCarriagePosition(
                    job.AssignedStackerId,
                    job.TargetSlot.Row,
                    job.TargetSlot.Level);
            }
            else if (job.Direction == SimFlowDirection.Inbound)
            {
                _ctx.ReleasePrimaryGoal(job);
            }

            job.State = WarehouseJobState.StackerMove;
            _ctx.RecordPlayback(job, SimPlaybackPhase.StackerPick, job.AssignedStackerId, job.TargetSlot);
        }

        private void OnPlaceComplete(WarehouseJob job)
        {
            if (job.Direction == SimFlowDirection.Outbound)
            {
                _ctx.OnOutboundStackerPlaceComplete(job);
                return;
            }

            if (job.AssignedStackerId >= 0)
            {
                _carriage.SetCarriagePosition(
                    job.AssignedStackerId,
                    job.TargetSlot.Row,
                    job.TargetSlot.Level);
            }

            job.State = WarehouseJobState.Completed;
            _ctx.SlotAllocator.NotifyInboundPlaced(job.TargetSlot);
            if (_ctx.InboundWaitingCount > 0 && !_ctx.InboundExhaustedAtInfeed)
            {
                _ctx.ScheduleInfeedQueueFill(_ctx.Clock.Now);
            }

            _ctx.RecordPlayback(job, SimPlaybackPhase.StackerPlace, job.AssignedStackerId, job.TargetSlot);
            _ctx.Queue.Enqueue(new ScheduledSimEvent(_ctx.Clock.Now, SimEventType.JobCompleted, job.JobId));
        }
    }
}
