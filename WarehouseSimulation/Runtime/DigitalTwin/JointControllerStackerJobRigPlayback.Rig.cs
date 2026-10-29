using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    public sealed partial class JointControllerStackerJobRigPlayback
    {
        /// <summary>轨道变了要重标定：货叉 0 点是这条巷道的中心，不是上一条巷的。</summary>
        private void CalibrateJointInitialValues()
        {
            _chainReady = false;
            EnsureDriver();
        }

        private void ApplyPartialGrid(int column, int level, int row)
        {
            if (!TryBuildCarriageJointValues(level, row, out var carriage))
            {
                return;
            }

            // 排/层走 Carriage；列向只给货叉相对中心的伸出量，不把绝对 Z 整段灌进第三轴。
            var forkRaw = carriage.Fork;
            if (column != _railColumn
                && TryBuildFullJointValues(column, level, row, out var extended))
            {
                forkRaw = extended.Fork;
            }

            TryApplyAxisValues(in carriage, 0f, StackerAxisMask.Carriage);
            TryApplyForkScalarOnly(forkRaw);
            _storageColumn = column;
            _level = level;
            _row = row;
        }

        private static StackerAxisValueResolver.StackerAxisValues LerpAxisValues(
            in StackerAxisValueResolver.StackerAxisValues from,
            in StackerAxisValueResolver.StackerAxisValues to,
            float progress)
        {
            progress = Mathf.Clamp01(progress);
            return new StackerAxisValueResolver.StackerAxisValues(
                Mathf.Lerp(from.Level, to.Level, progress),
                Mathf.Lerp(from.Row, to.Row, progress),
                Mathf.Lerp(from.Fork, to.Fork, progress));
        }

        private bool TryApplyAxisValues(in StackerAxisValueResolver.StackerAxisValues axes) =>
            TryApplyAxisValues(in axes, 0f, StackerAxisMask.All);

        private bool TryApplyAxisValues(
            in StackerAxisValueResolver.StackerAxisValues axes,
            float duration) =>
            TryApplyAxisValues(in axes, duration, StackerAxisMask.All);

        /// <summary>
        /// 排/层：相对模型静止位的仓库坐标差。
        /// 货叉：不追绝对列向 Z。0=模型静止位（收回），只写相对巷道中心的伸出量。
        /// 第三轴是货叉深处的小节点，若灌进绝对 Z，机身还在原地时它会自己飞去货位。
        /// </summary>
        private bool TryApplyAxisValues(
            in StackerAxisValueResolver.StackerAxisValues axes,
            float duration,
            StackerAxisMask mask)
        {
            _ = duration;
            if (!EnsureDriver() || !EnsureChain())
            {
                return false;
            }

            if (mask == StackerAxisMask.None)
            {
                mask = StackerAxisMask.All;
            }

            m_transformDriver.SetChainScalars(
                MapChain(_homeRow, axes.Row, _signRow, _scaleRow),
                MapChain(_homeLevel, axes.Level, _signLevel, _scaleLevel),
                MapForkExtend(axes.Fork),
                mask);
            return true;
        }

        /// <summary>只改货叉。forkRaw 是仓库列向坐标；内部换成相对巷道中心的伸出量。</summary>
        private bool TryApplyForkScalarOnly(float forkRaw)
        {
            if (!EnsureDriver() || !EnsureChain())
            {
                return false;
            }

            m_transformDriver.SetForkScalar(MapForkExtend(forkRaw));
            return true;
        }

        private bool TryApplyForkRetracted()
        {
            if (!EnsureDriver() || !EnsureChain())
            {
                return false;
            }

            // 关节 0 = 模型静止位（收回）。MapForkExtend(中心)=0，与这里一致。
            m_transformDriver.SetForkScalar(0f);
            return true;
        }

        private static float MapChain(float home, float value, int sign, float scale)
        {
            var safe = scale > 1e-6f ? scale : 1f;
            return sign * (value - home) / safe;
        }

        /// <summary>
        /// 货叉关节标量：只写相对巷道中心的伸出量。
        /// 中心/收回 → 0（停在模型静止位）。若改成相对静止位的绝对列坐标，
        /// 静止位离目标货位一远，整段列向会灌进货叉子节点，表现为货叉脱离机身乱飞。
        /// </summary>
        private float MapForkExtend(float absoluteFork)
        {
            var safe = _scaleFork > 1e-6f ? _scaleFork : 1f;
            return _signFork * (absoluteFork - _centerFork) / safe;
        }

        /// <summary>
        /// 排/层 0 点 = 模型静止位对齐点。货叉 0 点 = 关节 ZeroState（收回）；
        /// 巷道中心只作为伸出量基准，不把绝对列向灌进货叉轴。
        /// </summary>
        private bool EnsureChain()
        {
            if (_chainReady)
            {
                return true;
            }

            if (m_transformDriver == null || _positionIndex == null || _positionIndex.Positions == null)
            {
                return false;
            }

            var parked = m_transformDriver.GetAlignWorldPosition()
                - m_transformDriver.GetAxisWorldStep(StackerAxisMask.Row) * m_transformDriver.GetRowScalar()
                - m_transformDriver.GetAxisWorldStep(StackerAxisMask.Level) * m_transformDriver.GetLevelScalar()
                - m_transformDriver.GetAxisWorldStep(StackerAxisMask.Fork) * m_transformDriver.GetForkScalar();
            if (!_positionIndex.TryWorldToLocal(parked, out var local))
            {
                return false;
            }

            var mapping = SlotLocalAxisMappingCache.Resolve(_positionIndex.Positions);
            _homeRow = mapping.RowOf(local);
            _homeLevel = mapping.LevelOf(local);

            var manager = _positionIndex.BoundManager;
            if (manager == null)
            {
                return false;
            }

            BindChainAxis(StackerAxisMask.Row, mapping.RowAxis, manager.transform, out _signRow, out _scaleRow);
            BindChainAxis(StackerAxisMask.Level, mapping.LevelAxis, manager.transform, out _signLevel, out _scaleLevel);
            BindChainAxis(StackerAxisMask.Fork, mapping.ForkAxis, manager.transform, out _signFork, out _scaleFork);

            if (!TryBuildCarriageJointValues(0, 0, out var carriage))
            {
                // 巷道中心解析失败时退回静止位列向，伸出量至少不会把整段世界 Z 灌进去。
                _centerFork = mapping.ForkOf(local);
            }
            else
            {
                _centerFork = carriage.Fork;
            }

            _chainReady = true;
            return true;
        }

        private void BindChainAxis(
            StackerAxisMask axis,
            int warehouseAxis,
            Transform warehouse,
            out int sign,
            out float scale)
        {
            var step = m_transformDriver.GetAxisWorldStep(axis);
            var unit = Vector3.zero;
            unit[warehouseAxis] = 1f;
            unit = warehouse.TransformVector(unit);
            var unitMag = unit.magnitude;
            var stepMag = step.magnitude;
            scale = unitMag > 1e-6f && stepMag > 1e-6f ? stepMag / unitMag : 1f;
            sign = Vector3.Dot(step, unit) >= 0f ? 1 : -1;
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
                    && TryApplyAxisValues(in sameAxes, duration, StackerAxisMask.Carriage)
                    && TryApplyForkRetracted())
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
                return false;
            }

            // 走行/升降只动排、层。货叉关节保持 0（模型收回位），跟着父轴走，绝不追列向绝对 Z。
            var axes = new StackerAxisValueResolver.StackerAxisValues(
                Mathf.Lerp(fromAxes.Level, toAxes.Level, Mathf.Clamp01(levelProgress)),
                Mathf.Lerp(fromAxes.Row, toAxes.Row, Mathf.Clamp01(rowProgress)),
                toAxes.Fork);
            if (!TryApplyAxisValues(in axes, duration, StackerAxisMask.Carriage)
                || !TryApplyForkRetracted())
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
            bool commitState,
            bool toUsesForkDepth = false,
            float toForkDepthMeters = 0f,
            bool fromUsesForkDepth = false,
            float fromForkDepthMeters = 0f,
            bool fromTreatRailColumnAsCarriage = true,
            bool toTreatRailColumnAsCarriage = true)
        {
            // 伸叉/收叉按两端的世界坐标插值。相对静止位加一小段伸缩，到不了另一列的 Z。
            if (!TryBuildEndpointJointValues(
                    fromColumn,
                    fromLevel,
                    fromRow,
                    fromUsesForkDepth,
                    fromForkDepthMeters,
                    out var fromAxes,
                    treatRailColumnAsCarriage: fromTreatRailColumnAsCarriage && !fromUsesForkDepth)
                || !TryBuildEndpointJointValues(
                    toColumn,
                    toLevel,
                    toRow,
                    toUsesForkDepth,
                    toForkDepthMeters,
                    out var toAxes,
                    treatRailColumnAsCarriage: toTreatRailColumnAsCarriage && !toUsesForkDepth))
            {
                return false;
            }

            var axes = LerpAxisValues(fromAxes, toAxes, Mathf.Clamp01(progress));
            if (m_transformDriver != null)
            {
                m_transformDriver.StopMotion();
            }

            // 排/层与货叉分开写：货叉只用相对中心伸出量。
            if (!TryApplyAxisValues(in axes, 0f, StackerAxisMask.Carriage)
                || !TryApplyForkScalarOnly(axes.Fork))
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

        /// <summary>
        /// 伸叉/收叉：排/层钉在收回位，货叉只写相对巷道中心的伸出量（收回=0）。
        /// </summary>
        private bool TryApplyForkChain(
            in StackerAxisValueResolver.StackerAxisValues tip,
            bool commitState,
            int commitColumn,
            int commitLevel,
            int commitRow)
        {
            var carriage = new StackerAxisValueResolver.StackerAxisValues(tip.Level, tip.Row, tip.Fork);
            if (!TryApplyAxisValues(in carriage, 0f, StackerAxisMask.Carriage)
                || !TryApplyForkScalarOnly(tip.Fork))
            {
                return false;
            }

            if (commitState)
            {
                _storageColumn = commitColumn;
                _level = commitLevel;
                _row = commitRow;
            }

            return true;
        }

        private void SnapCarriage(int level, int row)
        {
            if (!TryBuildCarriageJointValues(level, row, out var axes))
            {
                return;
            }

            TryApplyAxisValues(in axes, 0f, StackerAxisMask.Carriage);
            TryApplyForkRetracted();
            _storageColumn = _railColumn;
            _level = level;
            _row = row;
        }

        /// <summary>停在该排/层的收回位：货叉关节标量 0（模型静止位），不追巷道中心绝对 Z。</summary>
        private void SnapRetractedCarriage(int level, int row) => SnapCarriage(level, row);

        private void SnapRetractedPickup(in StackerJobPlaybackTask task)
        {
            if (TryGetPickupNodeFromJob(in task, out var node))
            {
                SnapRetractedCarriage(Mathf.Max(0, node.PickupLevel), node.PickupRow);
                return;
            }

            if (TryResolveTargetGrid(in task, out var pickup, task.IsOutbound))
            {
                SnapRetractedCarriage(pickup.Level, pickup.Row);
            }
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
            ClearHeldFork();
            CalibrateJointInitialValues();
            // 回原点停在巷道中心收回位，不要 Absolute 伸到左列货位。
            SnapRetractedCarriage(0, 0);
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

        private bool TryBuildEndpointJointValues(
            int column,
            int level,
            int row,
            bool useForkDepth,
            float forkDepthMeters,
            out StackerAxisValueResolver.StackerAxisValues axes,
            bool treatRailColumnAsCarriage = false)
        {
            if (useForkDepth)
            {
                return StackerAxisValueResolver.TryResolvePickupForkValues(
                    _positionIndex.Positions,
                    _railColumn,
                    level,
                    row,
                    _activeAisleLeftColumn,
                    forkDepthMeters,
                    out axes);
            }

            // GridIndex 列号==轨道列有两种含义：
            // - CarriagePose：货叉收回（巷道中心）→ treatRailColumnAsCarriage=true
            // - ForkPose 伸到左浅位：目标列恰好等于巷道左列 → Absolute 伸到该列
            // 放货若把终点也当成收回，会看不见朝左伸叉，或从左列扫向正方向。
            if (treatRailColumnAsCarriage && column == _railColumn)
            {
                return TryBuildCarriageJointValues(level, row, out axes);
            }

            return TryBuildFullJointValues(column, level, row, out axes);
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
    }
}
