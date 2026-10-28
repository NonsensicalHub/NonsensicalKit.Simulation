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
        /// 时长、阶段端点和计划尾来自同一份 <see cref="StackerWorkPlan"/>。
        /// 下一项作业的起点是这份计划的结束排/层，不是堆垛机此刻的物理位置。
        /// 已有且时长一致的预约窗会复用，避免再写一段占用。
        /// 实际开始晚于 <c>_clock.Now</c> 时累计等待时间并记录 <see cref="SimSubTaskKind.StackerWait"/>。
        /// 等待姿态停在计划起点，不覆盖前一作业还在走的轨迹。
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
            BeginStackerPhase(job, plan);
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

            if (!TryClaimOutboundPickup(job))
            {
                // 交互点上有箱（或有箱正等着进），先不派堆垛机；空出后重试。
                return;
            }

            var pickupNode = _conveyorTopology.GetNode(job.PickupPointIndex);
            var plan = StackerWorkPlanner.PlanOutbound(
                _bindings, _conveyorTopology, _stackerCarriage, job, pickupNode);
            BeginStackerPhase(job, plan);
        }

        private void BeginStackerPhase(WarehouseJob job, in StackerWorkPlan plan)
        {
            var stackerId = job.AssignedStackerId;
            var total = plan.TotalSeconds;
            var resources = BuildStackerResources(job, stackerId, job.TargetSlot.Column);
            var poses = plan.Poses;
            var desired = _clock.Now;
            double actualStart;
            if (HasPlannedStackerReservation(job, desired, total))
            {
                actualStart = job.StackerReserveStart;
            }
            else if (resources.Length > 0)
            {
                actualStart = _reservations.ReserveAtEarliestAll(desired, total, resources);
            }
            else
            {
                actualStart = desired;
            }

            // 取货点 zone 占用由输送调度写入。这里只提交与本计划时长相同的堆垛机/巷道区间，不再另算一套阶段。

            var approachFrom = plan.StartPose;
            var wait = actualStart - desired;
            if (wait > 1e-9)
            {
                job.WaitTimeAccum += wait;
                job.LastWaitResource = _reservations.GetLatestBlockingResourceId(desired, resources)
                    ?? job.AisleResourceId
                    ?? job.StackerResourceId;
                RecordPlayback(job, SimPlaybackPhase.StackerWait, stackerId, job.TargetSlot);
                RecordSubTask(
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
            job.AisleResourceId = _bindings.UseAisleColumnReservation
                ? SimEntityNaming.AisleResourceId(job.TargetSlot.WarehouseId, job.TargetSlot.Column)
                : null;
            job.StackerReserveStart = actualStart;
            job.StackerReserveEnd = actualStart + total;
            job.PhaseStartTime = actualStart;
            job.ScheduledCompleteTime = job.StackerReserveEnd;

            var cursor = actualStart;
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
            RecordSubTask(
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
                hasStackerPose: true,
                stackerToUsesForkDepth: plan.PlaceUsesForkDepth,
                stackerToForkDepthMeters: plan.PlaceForkDepthMeters);
            _queue.Enqueue(new ScheduledSimEvent(placeEnd, SimEventType.StackerPlaceComplete, job.JobId));

            _stackerCarriage.CommitBooking(stackerId, placeEnd, plan.EndRow, plan.EndLevel);
            WarehouseSimLog.Detail(
                $"堆垛机计划 job={job.JobId} stacker={stackerId} 仓库={plan.WarehouseId} " +
                $"起点=R{plan.StartRow}L{plan.StartLevel} 终点=R{plan.EndRow}L{plan.EndLevel} " +
                $"t={actualStart:F3}-{placeEnd:F3} " +
                $"取货交接={StackerCargoHandover.FromPhase(cursor, pickEnd):F3} " +
                $"放货交接={StackerCargoHandover.FromPhase(moveEnd, placeEnd):F3}");
        }


        // 伸叉侧向只认配置的 ForkDepthMeters（原样）；货位侧用 Absolute，不再提供列相对轨道的符号推断。

        private string[] BuildStackerResources(WarehouseJob job, int stackerId, int column)
        {
            var useStacker = _bindings.UseStackerReservation;
            var useAisle = _bindings.UseAisleColumnReservation;
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
            else if (job.Direction == SimFlowDirection.Inbound)
            {
                ReleasePrimaryGoal(job);
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
