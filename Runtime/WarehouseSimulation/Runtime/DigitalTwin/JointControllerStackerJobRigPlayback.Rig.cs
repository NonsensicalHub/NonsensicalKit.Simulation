using NonsensicalKit.DigitalTwin;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback.Tasks;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime.DigitalTwin
{
    public sealed partial class JointControllerStackerJobRigPlayback
    {
        private void CalibrateJointInitialValues()
        {
            if (!StackerAxisValueResolver.TryResolveOrigin(
                    _positionIndex.Positions,
                    _railColumn,
                    out var origin))
            {
                return;
            }

            StackerJointAxisMap.SetInitialValues(m_jointController, in origin);
        }

        private void ApplyPartialGrid(int column, int level, int row)
        {
            if (!TryBuildFullJointValues(column, level, row, out var axes))
            {
                return;
            }

            TryApplyAxisValues(in axes);
            _storageColumn = column;
            _level = level;
            _row = row;
        }

        private static StackerAxisValueResolver.StackerAxisValues LerpAxisValues(
            in StackerAxisValueResolver.StackerAxisValues from,
            in StackerAxisValueResolver.StackerAxisValues to,
            float progress) =>
            LerpAxisValues(from, to, progress, progress);

        private static StackerAxisValueResolver.StackerAxisValues LerpAxisValues(
            in StackerAxisValueResolver.StackerAxisValues from,
            in StackerAxisValueResolver.StackerAxisValues to,
            float levelProgress,
            float rowProgress)
        {
            levelProgress = Mathf.Clamp01(levelProgress);
            rowProgress = Mathf.Clamp01(rowProgress);
            var forkProgress = Mathf.Max(levelProgress, rowProgress);
            return new StackerAxisValueResolver.StackerAxisValues(
                Mathf.Lerp(from.Level, to.Level, levelProgress),
                Mathf.Lerp(from.Row, to.Row, rowProgress),
                Mathf.Lerp(from.Fork, to.Fork, forkProgress));
        }

        private bool TryApplyAxisValues(in StackerAxisValueResolver.StackerAxisValues axes)
        {
            return TryApplyAxisValues(in axes, 0f);
        }

        private bool TryApplyAxisValues(in StackerAxisValueResolver.StackerAxisValues axes, float duration)
        {
            if (!StackerJointAxisMap.TryFillValues(m_jointController, in axes, _jointScratch))
            {
                return false;
            }

            m_jointController.ChangeState(new ActionData(_jointScratch, duration));
            return true;
        }

        private bool TryApplyCarriageLerp(
            int fromLevel,
            int fromRow,
            int fromRailColumn,
            int toLevel,
            int toRow,
            int toRailColumn,
            float progress,
            float duration,
            bool commitState) =>
            TryApplyCarriageLerp(
                fromLevel,
                fromRow,
                fromRailColumn,
                toLevel,
                toRow,
                toRailColumn,
                progress,
                progress,
                duration,
                commitState);

        private bool TryApplyCarriageLerp(
            int fromLevel,
            int fromRow,
            int fromRailColumn,
            int toLevel,
            int toRow,
            int toRailColumn,
            float levelProgress,
            float rowProgress,
            float duration,
            bool commitState)
        {
            if (fromLevel == toLevel && fromRow == toRow && fromRailColumn == toRailColumn)
            {
                if (TryBuildCarriageJointValues(toLevel, toRow, toRailColumn, out var sameAxes)
                    && TryApplyAxisValues(in sameAxes, duration))
                {
                    if (commitState)
                    {
                        _storageColumn = toRailColumn;
                        _level = toLevel;
                        _row = toRow;
                    }

                    return true;
                }
            }

            if (!TryBuildCarriageJointValues(fromLevel, fromRow, fromRailColumn, out var fromAxes)
                || !TryBuildCarriageJointValues(toLevel, toRow, toRailColumn, out var toAxes))
            {
                LogMotionResolveSkipOnce(
                    $"Carriage L{fromLevel}R{fromRow}→L{toLevel}R{toRow} rail={toRailColumn}");
                return false;
            }

            var axes = LerpAxisValues(fromAxes, toAxes, levelProgress, rowProgress);
            if (!TryApplyAxisValues(in axes, duration))
            {
                return false;
            }

            if (commitState)
            {
                _storageColumn = toRailColumn;
                _level = toLevel;
                _row = toRow;
            }

            return true;
        }

        private bool TryApplyFullLerp(
            int fromColumn,
            int fromLevel,
            int fromRow,
            int toColumn,
            int toLevel,
            int toRow,
            float progress,
            float duration,
            bool commitState)
        {
            if (fromColumn == toColumn && fromLevel == toLevel && fromRow == toRow)
            {
                if (TryBuildFullJointValues(toColumn, toLevel, toRow, out var sameAxes)
                    && TryApplyAxisValues(in sameAxes, duration))
                {
                    if (commitState)
                    {
                        _storageColumn = toColumn;
                        _level = toLevel;
                        _row = toRow;
                    }

                    return true;
                }

                if (toColumn == _railColumn
                    && TryBuildCarriageJointValues(toLevel, toRow, out sameAxes)
                    && TryApplyAxisValues(in sameAxes, duration))
                {
                    if (commitState)
                    {
                        _storageColumn = _railColumn;
                        _level = toLevel;
                        _row = toRow;
                    }

                    return true;
                }
            }

            if (!TryBuildFullJointValues(fromColumn, fromLevel, fromRow, out var fromAxes)
                || !TryBuildFullJointValues(toColumn, toLevel, toRow, out var toAxes))
            {
                LogMotionResolveSkipOnce(
                    $"Full C{fromColumn}L{fromLevel}R{fromRow}→C{toColumn}L{toLevel}R{toRow}");
                return false;
            }

            var axes = LerpAxisValues(fromAxes, toAxes, progress);
            if (!TryApplyAxisValues(in axes, duration))
            {
                return false;
            }

            if (commitState)
            {
                _storageColumn = toColumn;
                _level = toLevel;
                _row = toRow;
            }

            return true;
        }

        private void SnapCarriage(int level, int row)
        {
            if (!TryBuildCarriageJointValues(level, row, out var axes))
            {
                return;
            }

            TryApplyAxisValues(in axes);
            _storageColumn = _railColumn;
            _level = level;
            _row = row;
        }

        private void SnapFork(int column, int level, int row)
        {
            var clampedColumn = StackerColumnReachUtility.ClampColumn(ActiveDefinition, column);
            ApplyPartialGrid(clampedColumn, level, row);
        }

        private void SnapToGrid(GridIndex slot) => SnapPartial(slot);

        private void SnapToHomePose()
        {
            _activeAisleLeftColumn = _definition.AisleLeftColumn;
            _railColumn = _activeAisleLeftColumn;
            _storageColumn = _railColumn;
            _level = 0;
            _row = 0;
            CalibrateJointInitialValues();
            SnapToGrid(new GridIndex(0, _railColumn, 0));
        }

        private void SnapToSlot(GridIndex slot)
        {
            var column = StackerColumnReachUtility.ClampColumn(ActiveDefinition, slot.Column);
            SnapPartial(column, slot.Level, slot.Row);
        }

        private void SnapPartial(GridIndex slot) =>
            SnapPartial(slot.Column, slot.Level, slot.Row);

        private void SnapPartial(int column, int level, int row)
        {
            var clampedColumn = StackerColumnReachUtility.ClampColumn(ActiveDefinition, column);
            ApplyPartialGrid(clampedColumn, level, row);
        }

        private bool TryBuildFullJointValues(int column, int level, int row, out StackerAxisValueResolver.StackerAxisValues axes)
        {
            return StackerAxisValueResolver.TryResolveAbsoluteValues(
                _positionIndex.Positions,
                _railColumn,
                new GridIndex(level, column, row),
                out axes);
        }

        private bool TryBuildCarriageJointValues(
            int level,
            int row,
            int railColumn,
            out StackerAxisValueResolver.StackerAxisValues axes)
        {
            return StackerAxisValueResolver.TryResolveCarriageValues(
                _positionIndex.Positions,
                railColumn,
                new GridIndex(level, railColumn, row),
                out axes);
        }

        private bool TryBuildCarriageJointValues(int level, int row, out StackerAxisValueResolver.StackerAxisValues axes) =>
            TryBuildCarriageJointValues(level, row, _railColumn, out axes);

        private void SnapToPickupForJob(in StackerJobPlaybackTask task)
        {
            if (TryResolveTargetGrid(in task, out var pickup, task.IsOutbound))
            {
                SnapPartial(pickup);
            }
        }

        private void LogMotionResolveSkipOnce(string detail)
        {
            if (_loggedMotionResolveSkip)
            {
                return;
            }

            _loggedMotionResolveSkip = true;
            SimPlaybackLog.Warn(
                $"JointStackerRig 堆垛机 {m_stackerId} 无法解析关节目标（{detail}），" +
                "请检查仓库数据或货位坐标索引是否就绪。",
                this);
        }
    }
}
