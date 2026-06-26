using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback.Tasks;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime.DigitalTwin
{
    public sealed partial class JointControllerStackerJobRigPlayback
    {
        private void SyncRailFromSubTask(in SimSubTask subTask)
        {
            if (subTask.StackerRailColumn <= 0)
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
            float progress,
            double simTime,
            bool snapInstant)
        {
            ApplyRecordedSubTaskMotion(in subTask, in task, progress, simTime, snapInstant);
        }

        private void ApplyRecordedSubTaskMotion(
            in SimSubTask subTask,
            in StackerJobPlaybackTask task,
            float progress,
            double simTime,
            bool snapInstant)
        {
            var from = subTask.StackerFromSlot;
            var to = subTask.StackerToSlot;
            var duration = ComputeMotionDuration(in subTask, simTime, snapInstant);
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
                    break;

                case SimSubTaskKind.StackerPick:
                    TryApplyFullLerp(
                        from.Column,
                        from.Level,
                        from.Row,
                        to.Column,
                        to.Level,
                        to.Row,
                        progress,
                        duration,
                        commitState);
                    if (commitState)
                    {
                        AttachCargoByJob(task.JobId);
                    }

                    break;

                case SimSubTaskKind.StackerPlace:
                    TryApplyFullLerp(
                        from.Column,
                        from.Level,
                        from.Row,
                        to.Column,
                        to.Level,
                        to.Row,
                        progress,
                        duration,
                        commitState);
                    if (task.IsOutbound)
                    {
                        if (!commitState)
                        {
                            AttachCargoByJob(task.JobId);
                        }
                    }
                    else if (commitState)
                    {
                        PlaceCargoAtSlotByJob(task.JobId, task.TargetSlot);
                    }
                    else
                    {
                        AttachCargoByJob(task.JobId);
                    }

                    break;
            }

            if (subTask.Kind == SimSubTaskKind.StackerMove)
            {
                AttachCargoByJob(task.JobId);
            }
        }

        private void ApplySubTaskPose(in SimSubTask subTask, GridIndex pose, bool snapInstant)
        {
            if (subTask.StackerRailColumn > 0)
            {
                _activeAisleLeftColumn = subTask.StackerRailColumn;
                _railColumn = subTask.StackerRailColumn;
            }

            var duration = snapInstant ? 0f : GetEvaluateFrameDuration();
            if (TryBuildFullJointValues(pose.Column, pose.Level, pose.Row, out var axes))
            {
                TryApplyAxisValues(in axes, duration);
                _storageColumn = pose.Column;
                _level = pose.Level;
                _row = pose.Row;
            }
        }

        private float GetEvaluateFrameDuration() => Mathf.Max(Time.deltaTime, 1f / 90f);

        private void ResetMotionTracking()
        {
            _lastMotionSubTaskId = -1;
            _lastMotionSimTime = -1d;
        }

        private float ComputeMotionDuration(in SimSubTask subTask, double simTime, bool snapInstant)
        {
            if (snapInstant)
            {
                _lastMotionSubTaskId = subTask.SubTaskId;
                _lastMotionSimTime = simTime;
                return 0f;
            }

            var speed = _playback != null ? Mathf.Max(0.01f, _playback.PlaybackSpeed) : 60f;
            var subTaskChanged = subTask.SubTaskId != _lastMotionSubTaskId;
            var simDelta = subTaskChanged || _lastMotionSimTime < 0d
                ? 0d
                : System.Math.Max(0d, simTime - _lastMotionSimTime);
            _lastMotionSubTaskId = subTask.SubTaskId;
            _lastMotionSimTime = simTime;

            if (simDelta <= 1e-9)
            {
                return GetEvaluateFrameDuration();
            }

            return Mathf.Max((float)(simDelta / speed), GetEvaluateFrameDuration());
        }

        private void ComputeCarriageAxisFractions(
            int fromRow,
            int fromLevel,
            int toLevel,
            int toRow,
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
                out rowFraction);
        }
    }
}
