using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    public sealed partial class JointControllerStackerJobRigPlayback
    {
        private void SyncRailFromSubTask(in SimSubTask subTask)
        {
            // StackerRailColumn < 0 表示未写入；0 是合法轨道列（如二号库堆垛机 8）。
            if (subTask.StackerRailColumn < 0)
            {
                return;
            }

            if (_railColumn == subTask.StackerRailColumn)
            {
                return;
            }

            _activeAisleLeftColumn = subTask.StackerRailColumn;
            _railColumn = subTask.StackerRailColumn;
            CalibrateJointInitialValues();
        }

        private void ApplySubTaskMotion(
            in SimSubTask subTask,
            in StackerJobPlaybackTask task,
            float progress)
        {
            ApplyRecordedSubTaskMotion(in subTask, in task, progress);
        }

        private void ApplyRecordedSubTaskMotion(
            in SimSubTask subTask,
            in StackerJobPlaybackTask task,
            float progress)
        {
            var from = subTask.StackerFromSlot;
            var to = subTask.StackerToSlot;
            // 轴目标已经由 progress 决定。再按 Time.deltaTime 插值会让同一 simTime 随帧率和倍速变化。
            const float duration = 0f;
            var commitState = progress >= 0.999f;

            switch (subTask.Kind)
            {
                case SimSubTaskKind.StackerApproach:
                case SimSubTaskKind.StackerMove:
                    ComputeCarriageAxisFractions(
                        from.Row,
                        from.Level,
                        to.Level,
                        to.Row,
                        from.WarehouseId != 0 ? from.WarehouseId : to.WarehouseId,
                        progress,
                        subTask.Kind == SimSubTaskKind.StackerMove,
                        out var levelFraction,
                        out var rowFraction);
                    TryApplyCarriageLerp(
                        from.Level,
                        from.Row,
                        from.Column,
                        to.Level,
                        to.Row,
                        to.Column,
                        levelFraction,
                        rowFraction,
                        duration,
                        commitState);
                    if (subTask.Kind == SimSubTaskKind.StackerApproach)
                    {
                        ReconcileCargoBeforePick(in task);
                    }

                    break;

                case SimSubTaskKind.StackerPick:
                    // 取货：伸叉 → 绑货 → 收叉（货到中间），全部在 Pick 段内完成后再 Move。
                    ApplyForkExtendThenRetract(
                        from,
                        to,
                        progress,
                        StackerCargoHandover.ExtendFraction(in subTask),
                        commitState,
                        toUsesForkDepth: subTask.StackerToUsesForkDepth,
                        toForkDepthMeters: subTask.StackerToForkDepthMeters);
                    ReconcilePickCargo(in task, in subTask);
                    break;

                case SimSubTaskKind.StackerPlace:
                    // 放货：伸叉（货随叉）→ 解绑/放货 → 收叉，全部在 Place 段内完成。
                    ApplyForkExtendThenRetract(
                        from,
                        to,
                        progress,
                        StackerCargoHandover.ExtendFraction(in subTask),
                        commitState,
                        toUsesForkDepth: subTask.StackerToUsesForkDepth,
                        toForkDepthMeters: subTask.StackerToForkDepthMeters);
                    ReconcilePlaceCargo(in task, in subTask);
                    break;
            }

            if (subTask.Kind == SimSubTaskKind.StackerMove)
            {
                AttachCargoByJob(task.JobId);
            }
        }

        /// <summary>
        /// 货叉两段式：前段伸到终点（交接），后段收回到起点（Carriage）。
        /// 本体先对齐收回位，伸叉只改货叉轴，避免伸叉误差被排/层轴吃掉。
        /// </summary>
        private void ApplyForkExtendThenRetract(
            GridIndex from,
            GridIndex to,
            float progress,
            float extendEnd,
            bool commitState,
            bool toUsesForkDepth,
            float toForkDepthMeters)
        {
            extendEnd = Mathf.Clamp(extendEnd, 0.001f, 0.999f);
            var extending = progress <= extendEnd;
            var phaseProgress = extending
                ? (extendEnd > 1e-4f ? Mathf.Clamp01(progress / extendEnd) : 1f)
                : Mathf.Clamp01((progress - extendEnd) / Mathf.Max(1e-4f, 1f - extendEnd));

            if (!TryBuildEndpointJointValues(
                    from.Column,
                    from.Level,
                    from.Row,
                    useForkDepth: false,
                    forkDepthMeters: 0f,
                    out var retracted,
                    treatRailColumnAsCarriage: true)
                || !TryBuildEndpointJointValues(
                    to.Column,
                    to.Level,
                    to.Row,
                    toUsesForkDepth,
                    toForkDepthMeters,
                    out var extended,
                    treatRailColumnAsCarriage: false))
            {
                return;
            }

            // 排/层停在收回位。只插值货叉，避免伸叉把列向或层高提前带到终点。
            var fork = extending
                ? Mathf.Lerp(retracted.Fork, extended.Fork, phaseProgress)
                : Mathf.Lerp(extended.Fork, retracted.Fork, phaseProgress);
            var tip = new StackerAxisValueResolver.StackerAxisValues(retracted.Level, retracted.Row, fork);
            TryApplyForkChain(in tip, commitState && !extending, from.Column, from.Level, from.Row);
        }

        private void ApplySubTaskPose(in SimSubTask subTask, GridIndex pose)
        {
            if (subTask.StackerRailColumn >= 0)
            {
                _activeAisleLeftColumn = subTask.StackerRailColumn;
                _railColumn = subTask.StackerRailColumn;
            }

            var atRail = pose.Column == _railColumn && !subTask.StackerToUsesForkDepth;
            if (!TryBuildEndpointJointValues(
                    pose.Column,
                    pose.Level,
                    pose.Row,
                    subTask.StackerToUsesForkDepth,
                    subTask.StackerToForkDepthMeters,
                    out var axes,
                    treatRailColumnAsCarriage: atRail))
            {
                return;
            }

            TryApplyAxisValues(in axes, 0f, StackerAxisMask.Carriage);
            if (atRail)
            {
                TryApplyForkRetracted();
            }
            else
            {
                TryApplyForkScalarOnly(axes.Fork);
            }

            _storageColumn = pose.Column;
            _level = pose.Level;
            _row = pose.Row;
        }

        private void ResetMotionTracking()
        {
            ClearHeldFork();
        }

        private void ClearHeldFork()
        {
            _hasHeldFork = false;
            _heldForkRaw = 0f;
        }

        private void ComputeCarriageAxisFractions(
            int fromRow,
            int fromLevel,
            int toLevel,
            int toRow,
            int warehouseId,
            float linearProgress,
            bool isLoaded,
            out float levelFraction,
            out float rowFraction)
        {
            StackerKinematicsUtility.ComputeCarriageMoveFractions(
                Kinematics,
                _positionIndex.Positions,
                _railColumn,
                fromRow,
                fromLevel,
                toLevel,
                toRow,
                linearProgress,
                isLoaded,
                out levelFraction,
                out rowFraction,
                warehouseId);
        }
    }
}