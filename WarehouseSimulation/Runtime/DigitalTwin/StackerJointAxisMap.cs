using NonsensicalKit.DigitalTwin;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>
    /// 【旧路径】JointController 三轴绑定与 AxisScale/MapSigned。
    /// 回放已改接 <see cref="StackerTransformDriver"/>，不再从此类灌关节。
    /// 文件保留以免半删半留；新逻辑请勿再调用。
    /// </summary>
    internal static class StackerJointAxisMap
    {
        public const int RequiredJointCount = 3;

        /// <summary>解析后的关节下标与符号（符号使「坐标增大」沿关节正行程）。</summary>
        public struct Binding
        {
            public int LevelJointIndex;
            public int RowJointIndex;
            public int ForkJointIndex;
            public int LevelSign;
            public int RowSign;
            public int ForkSign;
            /// <summary>
            /// 相对缩放 = 关节父轴向世界米/局部 ÷ 仓库(.dat→世界)轴向米/局部。
            /// MapSigned 用 (raw-origin)/AxisScale；同比例时为 1，勿误当成「只除关节缩放」。
            /// </summary>
            public float LevelAxisScale;
            public float RowAxisScale;
            public float ForkAxisScale;
            public bool IsValid;

            public static Binding LegacyDefault => new()
            {
                LevelJointIndex = 0,
                RowJointIndex = 1,
                ForkJointIndex = 2,
                LevelSign = 1,
                RowSign = 1,
                ForkSign = 1,
                LevelAxisScale = 1f,
                RowAxisScale = 1f,
                ForkAxisScale = 1f,
                IsValid = true,
            };
        }

        public static bool IsValid(JointController controller) =>
            controller != null
            && controller.Joints != null
            && controller.Joints.Length >= RequiredJointCount;

        /// <summary>
        /// 用货位坐标增量的世界方向，匹配各关节正行程；失败则回退 DirType 轴号匹配，再回退 LegacyDefault。
        /// </summary>
        public static Binding Resolve(
            JointController controller,
            in SlotLocalAxisMapping axes,
            int railColumn,
            TryGetWorldPosition tryGetWorld,
            ISlotPositionIndex localFallback = null)
        {
            if (!IsValid(controller))
            {
                return default;
            }

            Binding resolved;
            if (TryResolveByWorldMotion(
                    controller,
                    in axes,
                    railColumn,
                    tryGetWorld,
                    localFallback,
                    out resolved))
            {
                FillAxisScales(controller, ref resolved, in axes, railColumn, tryGetWorld, localFallback);
                return resolved;
            }

            if (TryResolveByDirType(controller, in axes, out resolved))
            {
                RefineSignsFromWorld(
                    controller,
                    ref resolved,
                    in axes,
                    railColumn,
                    tryGetWorld,
                    localFallback);
                FillAxisScales(controller, ref resolved, in axes, railColumn, tryGetWorld, localFallback);
                return resolved;
            }

            resolved = Binding.LegacyDefault;
            RefineSignsFromWorld(
                controller,
                ref resolved,
                in axes,
                railColumn,
                tryGetWorld,
                localFallback);
            FillAxisScales(controller, ref resolved, in axes, railColumn, tryGetWorld, localFallback);
            return resolved;
        }

        /// <summary>
        /// 用「货位轴坐标分量增大」的世界位移，对照关节正行程，校正符号。
        /// 必须按坐标值增大方向（而非列号/排号增大），否则 Fork 在 PosZ 随列号递减时会被误判反号。
        /// </summary>
        public static void RefineSignsFromWorld(
            JointController controller,
            ref Binding binding,
            in SlotLocalAxisMapping axes,
            int railColumn,
            TryGetWorldPosition tryGetWorld,
            ISlotPositionIndex localFallback = null)
        {
            if (!binding.IsValid || !IsValid(controller))
            {
                return;
            }

            binding.RowSign = DetectSign(
                controller,
                binding.RowJointIndex,
                railColumn,
                AxisKind.Row,
                in axes,
                tryGetWorld,
                localFallback);
            binding.LevelSign = DetectSign(
                controller,
                binding.LevelJointIndex,
                railColumn,
                AxisKind.Level,
                in axes,
                tryGetWorld,
                localFallback);
            binding.ForkSign = DetectSign(
                controller,
                binding.ForkJointIndex,
                railColumn,
                AxisKind.Fork,
                in axes,
                tryGetWorld,
                localFallback);
        }

        public delegate bool TryGetWorldPosition(int level, int column, int row, out Vector3 world);

        public static bool TryFillValues(
            JointController controller,
            in Binding binding,
            in StackerAxisValueResolver.StackerAxisValues origin,
            in StackerAxisValueResolver.StackerAxisValues axes,
            float[] buffer)
        {
            if (!binding.IsValid || !IsValid(controller) || buffer == null || buffer.Length < controller.Joints.Length)
            {
                return false;
            }

            var current = controller.GetJointsValue();
            System.Array.Copy(current, buffer, controller.Joints.Length);
            // GetJointsValue 返回相对 ZeroState 的 gap，不含 InitialValue；
            // 先补成 ChangeState 用的绝对目标，再覆盖三轴，避免多余关节被写成 gap。
            for (var i = 0; i < controller.Joints.Length; i++)
            {
                buffer[i] = controller.Joints[i].InitialValue + current[i];
            }

            buffer[binding.LevelJointIndex] = MapSigned(
                axes.Level, origin.Level, binding.LevelSign, binding.LevelAxisScale);
            buffer[binding.RowJointIndex] = MapSigned(
                axes.Row, origin.Row, binding.RowSign, binding.RowAxisScale);
            buffer[binding.ForkJointIndex] = MapSigned(
                axes.Fork, origin.Fork, binding.ForkSign, binding.ForkAxisScale);
            return true;
        }

        /// <summary>
        /// 从当前关节 gap 反推货位轴坐标（与 <see cref="TryFillValues"/> / MapSigned 互逆）。
        /// </summary>
        public static bool TryReadCurrentAxisValues(
            JointController controller,
            in Binding binding,
            in StackerAxisValueResolver.StackerAxisValues origin,
            out StackerAxisValueResolver.StackerAxisValues axes)
        {
            axes = default;
            if (!binding.IsValid || !IsValid(controller))
            {
                return false;
            }

            var gap = controller.GetJointsValue();
            axes = new StackerAxisValueResolver.StackerAxisValues(
                UnmapSigned(gap[binding.LevelJointIndex], origin.Level, binding.LevelSign, binding.LevelAxisScale),
                UnmapSigned(gap[binding.RowJointIndex], origin.Row, binding.RowSign, binding.RowAxisScale),
                UnmapSigned(gap[binding.ForkJointIndex], origin.Fork, binding.ForkSign, binding.ForkAxisScale));
            return true;
        }

        public static void SetInitialValues(
            JointController controller,
            in Binding binding,
            in StackerAxisValueResolver.StackerAxisValues origin)
        {
            if (!binding.IsValid || !IsValid(controller))
            {
                return;
            }

            // InitialValue 取原点「未镜像」分量；TryFillValues 用 MapSigned 保证世界 offset=sign*(raw-origin)（已除父轴向缩放）。
            controller.Joints[binding.LevelJointIndex].InitialValue = origin.Level;
            controller.Joints[binding.RowJointIndex].InitialValue = origin.Row;
            controller.Joints[binding.ForkJointIndex].InitialValue = origin.Fork;
        }

        public static bool TryGetForkJointNode(
            JointController controller,
            in Binding binding,
            out Transform node)
        {
            node = null;
            if (!binding.IsValid || !IsValid(controller))
            {
                return false;
            }

            node = controller.Joints[binding.ForkJointIndex].JointsNode;
            return node != null;
        }

        public static bool TryGetRowJointNode(
            JointController controller,
            in Binding binding,
            out Transform node)
        {
            node = null;
            if (!binding.IsValid || !IsValid(controller))
            {
                return false;
            }

            node = controller.Joints[binding.RowJointIndex].JointsNode;
            return node != null;
        }

        /// <summary>
        /// 货位轴坐标 → ChangeState 目标。
        /// axisScale = S_joint/S_warehouse；局部 offset 经关节父缩放后应等于货位世界位移。
        /// </summary>
        private static float MapSigned(float raw, float origin, int sign, float axisScale)
        {
            var scale = axisScale > 1e-6f ? axisScale : 1f;
            return origin + sign * (raw - origin) / scale;
        }

        private static float UnmapSigned(float gap, float origin, int sign, float axisScale)
        {
            var scale = axisScale > 1e-6f ? axisScale : 1f;
            return origin + sign * gap * scale;
        }

        /// <summary>
        /// 相对缩放：S_joint（关节父 TransformVector）/ S_warehouse（同轴货位对 world/local）。
        /// 缺世界坐标时退回 1（假定 .dat 局部与关节局部同单位）。
        /// </summary>
        private static void FillAxisScales(
            JointController controller,
            ref Binding binding,
            in SlotLocalAxisMapping axes,
            int railColumn,
            TryGetWorldPosition tryGetWorld,
            ISlotPositionIndex localFallback)
        {
            if (!binding.IsValid || !IsValid(controller))
            {
                return;
            }

            binding.LevelAxisScale = MeasureRelativeAxisScale(
                controller.Joints[binding.LevelJointIndex],
                AxisKind.Level,
                in axes,
                railColumn,
                tryGetWorld,
                localFallback);
            binding.RowAxisScale = MeasureRelativeAxisScale(
                controller.Joints[binding.RowJointIndex],
                AxisKind.Row,
                in axes,
                railColumn,
                tryGetWorld,
                localFallback);
            binding.ForkAxisScale = MeasureRelativeAxisScale(
                controller.Joints[binding.ForkJointIndex],
                AxisKind.Fork,
                in axes,
                railColumn,
                tryGetWorld,
                localFallback);
        }

        private static float MeasureRelativeAxisScale(
            JointSetting joint,
            AxisKind axisKind,
            in SlotLocalAxisMapping axes,
            int railColumn,
            TryGetWorldPosition tryGetWorld,
            ISlotPositionIndex localFallback)
        {
            var jointScale = MeasureParentAxisScale(joint);
            var warehouseScale = MeasureWarehouseAxisScale(
                axisKind, in axes, railColumn, tryGetWorld, localFallback);
            if (warehouseScale < 1e-6f)
            {
                return 1f;
            }

            var relative = jointScale / warehouseScale;
            return relative > 1e-6f ? relative : 1f;
        }

        private static float MeasureParentAxisScale(JointSetting joint)
        {
            if (joint.JointsNode == null
                || !TryGetPositiveMotionLocal(joint.DirType, out var localAxis))
            {
                return 1f;
            }

            var parent = joint.JointsNode.parent;
            if (parent == null)
            {
                return 1f;
            }

            var world = parent.TransformVector(localAxis);
            var magnitude = world.magnitude;
            return magnitude > 1e-6f ? magnitude : 1f;
        }

        /// <summary>仓库：同一轴两货位的 |Δworld| / |Δ.dat局部|。</summary>
        private static float MeasureWarehouseAxisScale(
            AxisKind axisKind,
            in SlotLocalAxisMapping axes,
            int railColumn,
            TryGetWorldPosition tryGetWorld,
            ISlotPositionIndex localFallback)
        {
            if (tryGetWorld == null
                || localFallback == null
                || !localFallback.IsReady)
            {
                return 1f;
            }

            if (!TryPickSlotPair(
                    railColumn,
                    axisKind,
                    localFallback,
                    out var levelA,
                    out var colA,
                    out var rowA,
                    out var levelB,
                    out var colB,
                    out var rowB))
            {
                return 1f;
            }

            if (!localFallback.TryGetLocalPosition(new GridIndex(levelA, colA, rowA), out var localA)
                && !localFallback.TryGetAxisPosition(levelA, colA, rowA, out localA))
            {
                return 1f;
            }

            if (!localFallback.TryGetLocalPosition(new GridIndex(levelB, colB, rowB), out var localB)
                && !localFallback.TryGetAxisPosition(levelB, colB, rowB, out localB))
            {
                return 1f;
            }

            var localDelta = AxisComponent(localB, axisKind, in axes) - AxisComponent(localA, axisKind, in axes);
            if (Mathf.Abs(localDelta) < 1e-5f)
            {
                return 1f;
            }

            if (!tryGetWorld(levelA, colA, rowA, out var worldA)
                || !tryGetWorld(levelB, colB, rowB, out var worldB))
            {
                return 1f;
            }

            var worldDelta = (worldB - worldA).magnitude;
            if (worldDelta < 1e-6f)
            {
                return 1f;
            }

            return worldDelta / Mathf.Abs(localDelta);
        }

        private enum AxisKind
        {
            Level,
            Row,
            Fork,
        }

        private static bool TryResolveByWorldMotion(
            JointController controller,
            in SlotLocalAxisMapping axes,
            int railColumn,
            TryGetWorldPosition tryGetWorld,
            ISlotPositionIndex localFallback,
            out Binding binding)
        {
            binding = default;
            if (!TryBuildDesiredDelta(railColumn, AxisKind.Level, in axes, tryGetWorld, localFallback, out var levelDir)
                || !TryBuildDesiredDelta(railColumn, AxisKind.Row, in axes, tryGetWorld, localFallback, out var rowDir)
                || !TryBuildDesiredDelta(railColumn, AxisKind.Fork, in axes, tryGetWorld, localFallback, out var forkDir))
            {
                return false;
            }

            if (levelDir.sqrMagnitude < 1e-8f || rowDir.sqrMagnitude < 1e-8f || forkDir.sqrMagnitude < 1e-8f)
            {
                return false;
            }

            levelDir.Normalize();
            rowDir.Normalize();
            forkDir.Normalize();

            var bestLevel = -1;
            var bestRow = -1;
            var bestFork = -1;
            var bestLevelScore = 0f;
            var bestRowScore = 0f;
            var bestForkScore = 0f;
            var levelSign = 1;
            var rowSign = 1;
            var forkSign = 1;

            for (var i = 0; i < RequiredJointCount; i++)
            {
                var joint = controller.Joints[i];
                if (joint.JointsNode == null
                    || !TryGetPositiveMotionLocal(joint.DirType, out var localMotion))
                {
                    continue;
                }

                var motion = joint.JointsNode.TransformDirection(localMotion);
                if (motion.sqrMagnitude < 1e-8f)
                {
                    continue;
                }

                motion.Normalize();
                ScoreAxis(motion, levelDir, i, ref bestLevel, ref bestLevelScore, ref levelSign);
                ScoreAxis(motion, rowDir, i, ref bestRow, ref bestRowScore, ref rowSign);
                ScoreAxis(motion, forkDir, i, ref bestFork, ref bestForkScore, ref forkSign);
            }

            // 贪心去重：分高者优先占轴，避免同一关节被三个轴同时选中。
            var order = new[]
            {
                (AxisKind.Level, bestLevel, bestLevelScore, levelSign),
                (AxisKind.Row, bestRow, bestRowScore, rowSign),
                (AxisKind.Fork, bestFork, bestForkScore, forkSign),
            };
            System.Array.Sort(order, (a, b) => b.Item3.CompareTo(a.Item3));

            var used = new bool[RequiredJointCount];
            var level = -1;
            var row = -1;
            var fork = -1;
            levelSign = 1;
            rowSign = 1;
            forkSign = 1;
            const float minScore = 0.35f;
            foreach (var (kind, idx, score, sign) in order)
            {
                if (idx < 0 || score < minScore || used[idx])
                {
                    continue;
                }

                used[idx] = true;
                switch (kind)
                {
                    case AxisKind.Level:
                        level = idx;
                        levelSign = sign;
                        break;
                    case AxisKind.Row:
                        row = idx;
                        rowSign = sign;
                        break;
                    default:
                        fork = idx;
                        forkSign = sign;
                        break;
                }
            }

            if (level < 0 || row < 0 || fork < 0
                || level == row || level == fork || row == fork)
            {
                return false;
            }

            binding = new Binding
            {
                LevelJointIndex = level,
                RowJointIndex = row,
                ForkJointIndex = fork,
                LevelSign = levelSign,
                RowSign = rowSign,
                ForkSign = forkSign,
                IsValid = true,
            };
            return true;
        }

        private static void ScoreAxis(
            Vector3 motion,
            Vector3 desired,
            int jointIndex,
            ref int bestIndex,
            ref float bestScore,
            ref int bestSign)
        {
            var dot = Vector3.Dot(motion, desired);
            var score = Mathf.Abs(dot);
            if (score <= bestScore)
            {
                return;
            }

            bestScore = score;
            bestIndex = jointIndex;
            bestSign = dot >= 0f ? 1 : -1;
        }

        private static bool TryResolveByDirType(
            JointController controller,
            in SlotLocalAxisMapping axes,
            out Binding binding)
        {
            binding = default;
            var level = -1;
            var row = -1;
            var fork = -1;
            for (var i = 0; i < RequiredJointCount; i++)
            {
                if (!TryGetDirAxis(controller.Joints[i].DirType, out var dirAxis))
                {
                    continue;
                }

                if (dirAxis == axes.LevelAxis && level < 0)
                {
                    level = i;
                }
                else if (dirAxis == axes.RowAxis && row < 0)
                {
                    row = i;
                }
                else if (dirAxis == axes.ForkAxis && fork < 0)
                {
                    fork = i;
                }
            }

            if (level < 0 || row < 0 || fork < 0
                || level == row || level == fork || row == fork)
            {
                return false;
            }

            binding = new Binding
            {
                LevelJointIndex = level,
                RowJointIndex = row,
                ForkJointIndex = fork,
                LevelSign = 1,
                RowSign = 1,
                ForkSign = 1,
                IsValid = true,
            };
            return true;
        }

        private static int DetectSign(
            JointController controller,
            int jointIndex,
            int railColumn,
            AxisKind axisKind,
            in SlotLocalAxisMapping axes,
            TryGetWorldPosition tryGetWorld,
            ISlotPositionIndex localFallback)
        {
            if (!TryBuildDesiredDelta(railColumn, axisKind, in axes, tryGetWorld, localFallback, out var desired)
                || desired.sqrMagnitude < 1e-8f)
            {
                return 1;
            }

            var joint = controller.Joints[jointIndex];
            if (joint.JointsNode == null)
            {
                return 1;
            }

            if (!TryGetPositiveMotionLocal(joint.DirType, out var localMotion))
            {
                return 1;
            }

            var motionWorld = joint.JointsNode.TransformDirection(localMotion);
            var dot = Vector3.Dot(motionWorld, desired);
            if (Mathf.Abs(dot) < 1e-6f)
            {
                return 1;
            }

            return dot > 0f ? 1 : -1;
        }

        private static bool TryBuildDesiredDelta(
            int railColumn,
            AxisKind axisKind,
            in SlotLocalAxisMapping axes,
            TryGetWorldPosition tryGetWorld,
            ISlotPositionIndex localFallback,
            out Vector3 delta)
        {
            delta = default;
            if (localFallback == null || !localFallback.IsReady)
            {
                return false;
            }

            if (!TryPickSlotPair(railColumn, axisKind, localFallback, out var levelA, out var colA, out var rowA,
                    out var levelB, out var colB, out var rowB))
            {
                return false;
            }

            if (!localFallback.TryGetLocalPosition(new GridIndex(levelA, colA, rowA), out var localA)
                && !localFallback.TryGetAxisPosition(levelA, colA, rowA, out localA))
            {
                return false;
            }

            if (!localFallback.TryGetLocalPosition(new GridIndex(levelB, colB, rowB), out var localB)
                && !localFallback.TryGetAxisPosition(levelB, colB, rowB, out localB))
            {
                return false;
            }

            // 轴分量只认 .dat 局部坐标；世界位移用同一对货位的世界坐标。
            var valueA = AxisComponent(localA, axisKind, in axes);
            var valueB = AxisComponent(localB, axisKind, in axes);
            if (valueB < valueA)
            {
                (levelA, colA, rowA, levelB, colB, rowB) = (levelB, colB, rowB, levelA, colA, rowA);
                (localA, localB) = (localB, localA);
            }

            if (tryGetWorld != null
                && tryGetWorld(levelA, colA, rowA, out var worldA)
                && tryGetWorld(levelB, colB, rowB, out var worldB))
            {
                delta = worldB - worldA;
                return delta.sqrMagnitude >= 1e-8f;
            }

            delta = localB - localA;
            return delta.sqrMagnitude >= 1e-8f;
        }

        private static bool TryPickSlotPair(
            int railColumn,
            AxisKind axisKind,
            ISlotPositionIndex localFallback,
            out int levelA,
            out int colA,
            out int rowA,
            out int levelB,
            out int colB,
            out int rowB)
        {
            levelA = colA = rowA = levelB = colB = rowB = 0;
            switch (axisKind)
            {
                case AxisKind.Row:
                    levelA = 0;
                    colA = railColumn;
                    rowA = 0;
                    levelB = 0;
                    colB = railColumn;
                    rowB = 1;
                    if (HasLocal(localFallback, levelA, colA, rowA) && HasLocal(localFallback, levelB, colB, rowB))
                    {
                        return true;
                    }

                    rowA = 1;
                    rowB = 2;
                    return HasLocal(localFallback, levelA, colA, rowA) && HasLocal(localFallback, levelB, colB, rowB);

                case AxisKind.Level:
                    levelA = 0;
                    colA = railColumn;
                    rowA = 1;
                    levelB = 1;
                    colB = railColumn;
                    rowB = 1;
                    return HasLocal(localFallback, levelA, colA, rowA) && HasLocal(localFallback, levelB, colB, rowB);

                default:
                    levelA = 0;
                    colA = railColumn;
                    rowA = 1;
                    levelB = 0;
                    colB = railColumn + 1;
                    rowB = 1;
                    if (HasLocal(localFallback, levelA, colA, rowA) && HasLocal(localFallback, levelB, colB, rowB))
                    {
                        return true;
                    }

                    colA = Mathf.Max(0, railColumn - 1);
                    colB = railColumn;
                    return HasLocal(localFallback, levelA, colA, rowA) && HasLocal(localFallback, levelB, colB, rowB);
            }
        }

        private static bool HasLocal(ISlotPositionIndex index, int level, int column, int row) =>
            index.TryGetLocalPosition(new GridIndex(level, column, row), out _)
            || index.TryGetAxisPosition(level, column, row, out _);

        private static float AxisComponent(in Vector3 local, AxisKind kind, in SlotLocalAxisMapping axes) =>
            kind switch
            {
                AxisKind.Level => axes.LevelOf(local),
                AxisKind.Row => axes.RowOf(local),
                _ => axes.ForkOf(local),
            };

        private static bool TryGetDirAxis(JointDirType dirType, out int axis)
        {
            switch (dirType)
            {
                case JointDirType.X:
                case JointDirType.IX:
                    axis = 0;
                    return true;
                case JointDirType.Y:
                case JointDirType.IY:
                    axis = 1;
                    return true;
                case JointDirType.Z:
                case JointDirType.IZ:
                    axis = 2;
                    return true;
                default:
                    axis = 0;
                    return false;
            }
        }

        /// <summary>关节 value 增大一单位时，节点局部空间的位移方向（已含 IX/IY/IZ）。</summary>
        private static bool TryGetPositiveMotionLocal(JointDirType dirType, out Vector3 local)
        {
            switch (dirType)
            {
                case JointDirType.X:
                    local = Vector3.right;
                    return true;
                case JointDirType.Y:
                    local = Vector3.up;
                    return true;
                case JointDirType.Z:
                    local = Vector3.forward;
                    return true;
                case JointDirType.IX:
                    local = Vector3.left;
                    return true;
                case JointDirType.IY:
                    local = Vector3.down;
                    return true;
                case JointDirType.IZ:
                    local = Vector3.back;
                    return true;
                default:
                    local = Vector3.zero;
                    return false;
            }
        }
    }
}
