using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <content>
    /// 堆垛机阶段：在取货点预约堆垛机/巷道资源后，依次触发驶向作业点、取货、移动、放货四个子事件。
    /// 取货点 zone 占用已在输送调度中写入，此处勿重复预约（见 <see cref="TryBeginStackerPhase"/> 注释）。
    /// </content>
    public sealed partial class StackerWarehouseSimulator
    {
        #region 堆垛机预约与四阶段子事件

        /// <summary>
        /// 在取货点启动堆垛机阶段：预约堆垛机与巷道资源，并排定驶向 → 取货 → 移动 → 放货四个离散事件。
        /// </summary>
        /// <remarks>
        /// 若输送阶段已在取货点 zone 写入堆垛机连续占用（<c>ApplyPickupStackerReserve</c>），
        /// 此处复用已有预约窗，避免重复 <see cref="ReservationTable.TryReserve"/> 产生幽灵占用。
        /// 实际开始晚于 <c>_clock.Now</c> 时累计等待时间并记录 <see cref="SimSubTaskKind.StackerWait"/>。
        /// </remarks>
        private void TryBeginStackerPhase(WarehouseJob job)
        {
            if (job.Direction == SimFlowDirection.Outbound)
            {
                TryBeginOutboundStackerPhase(job);
                return;
            }

            if (job.State != WarehouseJobState.WaitingStacker
                || !job.HasSlot
                || job.AssignedStackerId < 0)
            {
                return;
            }

            var pickupNode = _conveyorTopology.GetNode(job.PickupPointIndex);
            var plan = StackerWorkPlanner.PlanInbound(
                _bindings, _conveyorTopology, _stackerCarriage, job, pickupNode);
            BeginStackerPhase(job, plan, commitCarriageBooking: !HasPlannedStackerReservation(
                job, _clock.Now, plan.TotalSeconds));
        }

        private void TryBeginOutboundStackerPhase(WarehouseJob job)
        {
            if (job.State != WarehouseJobState.WaitingStacker
                || !job.HasSlot
                || job.AssignedStackerId < 0
                || job.PickupPointIndex < 0)
            {
                return;
            }

            var pickupNode = _conveyorTopology.GetNode(job.PickupPointIndex);
            var plan = StackerWorkPlanner.PlanOutbound(
                _bindings, _conveyorTopology, _stackerCarriage, job, pickupNode);
            BeginStackerPhase(job, plan, commitCarriageBooking: true);
        }

        private void BeginStackerPhase(WarehouseJob job, in StackerWorkPlan plan, bool commitCarriageBooking)
        {
            var stackerId = job.AssignedStackerId;
            var total = plan.TotalSeconds;
            var resources = BuildStackerResources(job, stackerId, job.TargetSlot.Column);
            var pickupNode = _conveyorTopology.GetNode(job.PickupPointIndex);
            var poses = StackerSubTaskPoseBuilder.Build(
                _bindings, _conveyorTopology, job, pickupNode);
            var desired = _clock.Now;
            double actualStart;
            if (HasPlannedStackerReservation(job, desired, total))
            {
                actualStart = job.StackerReserveStart;
            }
            else if (resources.Length > 0)
            {
                actualStart = _reservations.ReserveAtEarliestAll(desired, total, resources);
                job.StackerReserveStart = actualStart;
                job.StackerReserveEnd = actualStart + total;
            }
            else
            {
                actualStart = desired;
                job.StackerReserveStart = actualStart;
                job.StackerReserveEnd = actualStart + total;
            }

            // 取货点 zone 的整段占用（驶入 + 驶向/取/移/放）已在输送调度阶段（ApplyPickupStackerReserve）写入，
            // 此处不再重复预约，否则会与已有占用冲突并顺延出一段幽灵占用，误阻后续箱进入。

            var wait = actualStart - desired;
            if (wait > 1e-9)
            {
                job.WaitTimeAccum += wait;
                job.LastWaitResource = _reservations.GetLatestBlockingResourceId(desired, resources)
                    ?? job.AisleResourceId
                    ?? job.StackerResourceId;
                RecordPlayback(job, SimPlaybackPhase.StackerWait, stackerId, job.TargetSlot);
                var waitPose = StackerSubTaskPoseBuilder.ResolveCarriagePose(
                    _stackerCarriage, stackerId, poses.RailColumn);
                RecordSubTask(
                    job,
                    SimSubTaskKind.StackerWait,
                    desired,
                    actualStart,
                    stackerId,
                    job.TargetSlot,
                    stackerFromSlot: waitPose,
                    stackerToSlot: waitPose,
                    stackerRailColumn: poses.RailColumn,
                    hasStackerPose: true);
            }

            job.StackerResourceId = SimEntityNaming.StackerResourceId(stackerId);
            job.AisleResourceId = _bindings.UseAisleColumnReservation ? AisleId(job.TargetSlot.Column) : null;
            job.StackerReserveStart = actualStart;
            job.StackerReserveEnd = actualStart + total;
            job.PhaseStartTime = actualStart;
            job.ScheduledCompleteTime = job.StackerReserveEnd;

            var cursor = actualStart;
            var approachFrom = StackerSubTaskPoseBuilder.ResolveCarriagePose(
                _stackerCarriage, stackerId, poses.RailColumn);
            var needsApproach = !StackerSubTaskPoseBuilder.CarriagePosesEqual(
                approachFrom, poses.ApproachTo);
            if (needsApproach && plan.ApproachSeconds > 1e-6f)
            {
                job.State = WarehouseJobState.StackerApproach;
                job.ServiceTimeAccum += plan.ApproachSeconds;
                var approachEnd = cursor + plan.ApproachSeconds;
                RecordPlayback(job, SimPlaybackPhase.StackerApproach, stackerId, job.TargetSlot);
                RecordSubTask(
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
                _queue.Enqueue(new ScheduledSimEvent(approachEnd, SimEventType.StackerApproachComplete, job.JobId));
                cursor = approachEnd;
            }
            else
            {
                job.State = WarehouseJobState.StackerPick;
            }

            job.ServiceTimeAccum += plan.PickSeconds;
            var pickEnd = cursor + plan.PickSeconds;
            var pickFrom = poses.PickFrom;
            if (!needsApproach || plan.ApproachSeconds <= 1e-6f)
            {
                var pickStartCarriage = StackerSubTaskPoseBuilder.ResolveCarriagePose(
                    _stackerCarriage, stackerId, poses.RailColumn);
                if (!StackerSubTaskPoseBuilder.CarriagePosesEqual(pickStartCarriage, poses.PickFrom))
                {
                    pickFrom = pickStartCarriage;
                }
            }

            RecordSubTask(
                job,
                SimSubTaskKind.StackerPick,
                cursor,
                pickEnd,
                stackerId,
                job.TargetSlot,
                stackerFromSlot: pickFrom,
                stackerToSlot: poses.PickTo,
                stackerRailColumn: poses.RailColumn,
                hasStackerPose: true);
            _queue.Enqueue(new ScheduledSimEvent(pickEnd, SimEventType.StackerPickComplete, job.JobId));

            job.ServiceTimeAccum += plan.MoveSeconds;
            var moveEnd = pickEnd + plan.MoveSeconds;
            RecordSubTask(
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
            _queue.Enqueue(new ScheduledSimEvent(moveEnd, SimEventType.StackerMoveComplete, job.JobId));

            job.ServiceTimeAccum += plan.PlaceSeconds;
            var placeEnd = moveEnd + plan.PlaceSeconds;
            RecordSubTask(
                job,
                SimSubTaskKind.StackerPlace,
                moveEnd,
                placeEnd,
                stackerId,
                job.TargetSlot,
                stackerFromSlot: poses.PlaceFrom,
                stackerToSlot: poses.PlaceTo,
                stackerRailColumn: poses.RailColumn,
                hasStackerPose: true);
            _queue.Enqueue(new ScheduledSimEvent(placeEnd, SimEventType.StackerPlaceComplete, job.JobId));

            if (commitCarriageBooking)
            {
                _stackerCarriage.CommitBooking(stackerId, placeEnd, plan.EndRow, plan.EndLevel);
            }
        }

        private string[] BuildStackerResources(WarehouseJob job, int stackerId, int column)
        {
            var useStacker = _bindings.UseStackerReservation;
            var useAisle = _bindings.UseAisleColumnReservation;
            if (useStacker && useAisle)
            {
                return new[] { SimEntityNaming.StackerResourceId(stackerId), AisleId(column) };
            }

            if (useStacker)
            {
                return new[] { SimEntityNaming.StackerResourceId(stackerId) };
            }

            if (useAisle)
            {
                return new[] { AisleId(column) };
            }

            return Array.Empty<string>();
        }

        private static bool HasPlannedStackerReservation(WarehouseJob job, double desiredArriveTime, float workDuration)
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

        private static string AisleId(int column) => $"aisle-col-{column}";

        private void OnStackerApproachComplete(WarehouseJob job)
        {
            job.State = WarehouseJobState.StackerPick;
        }

        private void OnStackerPickComplete(WarehouseJob job)
        {
            if (job.Direction == SimFlowDirection.Outbound && job.AssignedStackerId >= 0)
            {
                _stackerCarriage.SetCarriagePosition(
                    job.AssignedStackerId,
                    job.TargetSlot.Row,
                    job.TargetSlot.Level);
            }
            job.State = WarehouseJobState.StackerMove;
            RecordPlayback(job, SimPlaybackPhase.StackerPick, job.AssignedStackerId, job.TargetSlot);
        }

        private void OnStackerMoveComplete(WarehouseJob job)
        {
            job.State = WarehouseJobState.StackerPlace;
            RecordPlayback(job, SimPlaybackPhase.StackerMove, job.AssignedStackerId, job.TargetSlot);
        }

        private void OnStackerPlaceComplete(WarehouseJob job)
        {
            if (job.Direction == SimFlowDirection.Outbound)
            {
                OnOutboundStackerPlaceComplete(job);
                return;
            }

            if (job.AssignedStackerId >= 0)
            {
                _stackerCarriage.SetCarriagePosition(
                    job.AssignedStackerId,
                    job.TargetSlot.Row,
                    job.TargetSlot.Level);
            }

            job.State = WarehouseJobState.Completed;
            RecordPlayback(job, SimPlaybackPhase.StackerPlace, job.AssignedStackerId, job.TargetSlot);
            _queue.Enqueue(new ScheduledSimEvent(_clock.Now, SimEventType.JobCompleted, job.JobId));
        }

        #endregion
    }
}
