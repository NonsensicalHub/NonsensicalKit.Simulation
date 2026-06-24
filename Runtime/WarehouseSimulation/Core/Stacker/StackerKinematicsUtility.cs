using System;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>
    /// 堆垛机行程时间估算（轨道固定列，仅层/排参与移动；货叉伸列瞬时完成）。
    /// 层向与排向同时运动，总时长取两轴梯形剖面时间的最大值。
    /// </summary>
    public static class StackerKinematicsUtility
    {
        public static float ComputeMoveSeconds(
            IStackerKinematics kinematics,
            ISlotPositionIndex positions,
            int railColumn,
            int fromRow,
            int fromLevel,
            GridIndex slot,
            bool isLoaded = false)
        {
            if (kinematics == null)
            {
                return 0f;
            }

            var levelDist = SlotPositionMath.ComputeLevelDistance(
                positions, railColumn, fromLevel, slot.Level);
            var rowDist = SlotPositionMath.ComputeRowDistance(
                positions, railColumn, slot.Level, fromRow, slot.Row);

            var verticalSpeed = ResolveVerticalSpeed(kinematics, isLoaded);
            var depthSpeed = ResolveDepthSpeed(kinematics, isLoaded);

            var levelDuration = ComputeTrapezoidalMoveSeconds(
                levelDist,
                verticalSpeed,
                kinematics.VerticalAccelerationMetersPerSecondSquared);
            var rowDuration = ComputeTrapezoidalMoveSeconds(
                rowDist,
                depthSpeed,
                kinematics.DepthAccelerationMetersPerSecondSquared);

            return Math.Max(levelDuration, rowDuration);
        }

        /// <summary>将归一化仿真时刻（0~1）映射为层/排位移比例，用于回放插值。</summary>
        public static void ComputeCarriageMoveFractions(
            IStackerKinematics kinematics,
            ISlotPositionIndex positions,
            int railColumn,
            int fromRow,
            int fromLevel,
            int toLevel,
            int toRow,
            float normalizedTime,
            bool isLoaded,
            out float levelFraction,
            out float rowFraction)
        {
            normalizedTime = Math.Clamp(normalizedTime, 0f, 1f);
            if (kinematics == null)
            {
                levelFraction = normalizedTime;
                rowFraction = normalizedTime;
                return;
            }

            var levelDist = SlotPositionMath.ComputeLevelDistance(
                positions, railColumn, fromLevel, toLevel);
            var rowDist = SlotPositionMath.ComputeRowDistance(
                positions, railColumn, toLevel, fromRow, toRow);

            var verticalSpeed = ResolveVerticalSpeed(kinematics, isLoaded);
            var depthSpeed = ResolveDepthSpeed(kinematics, isLoaded);

            var totalDuration = Math.Max(
                ComputeTrapezoidalMoveSeconds(
                    levelDist,
                    verticalSpeed,
                    kinematics.VerticalAccelerationMetersPerSecondSquared),
                ComputeTrapezoidalMoveSeconds(
                    rowDist,
                    depthSpeed,
                    kinematics.DepthAccelerationMetersPerSecondSquared));

            if (totalDuration <= 1e-6f)
            {
                levelFraction = normalizedTime;
                rowFraction = normalizedTime;
                return;
            }

            var elapsed = normalizedTime * totalDuration;
            levelFraction = levelDist > 1e-6f
                ? ComputeDisplacementAtTime(
                      elapsed,
                      levelDist,
                      verticalSpeed,
                      kinematics.VerticalAccelerationMetersPerSecondSquared) / levelDist
                : 1f;
            rowFraction = rowDist > 1e-6f
                ? ComputeDisplacementAtTime(
                      elapsed,
                      rowDist,
                      depthSpeed,
                      kinematics.DepthAccelerationMetersPerSecondSquared) / rowDist
                : 1f;
        }

        /// <summary>单轴梯形速度剖面行程时间（先加速、匀速、再减速；距离不足时退化为三角剖面）。</summary>
        public static float ComputeTrapezoidalMoveSeconds(
            float distance,
            float maxSpeed,
            float acceleration)
        {
            if (distance <= 1e-6f)
            {
                return 0f;
            }

            maxSpeed = Math.Max(0.01f, maxSpeed);
            acceleration = Math.Max(0.01f, acceleration);

            var accelDistance = maxSpeed * maxSpeed / acceleration;
            if (distance >= accelDistance)
            {
                var cruiseDistance = distance - accelDistance;
                var accelTime = maxSpeed / acceleration;
                return 2f * accelTime + cruiseDistance / maxSpeed;
            }

            return 2f * (float)Math.Sqrt(distance / acceleration);
        }

        public static float ComputeDisplacementAtTime(
            float time,
            float distance,
            float maxSpeed,
            float acceleration)
        {
            if (distance <= 1e-6f || time <= 0f)
            {
                return 0f;
            }

            if (time >= ComputeTrapezoidalMoveSeconds(distance, maxSpeed, acceleration))
            {
                return distance;
            }

            maxSpeed = Math.Max(0.01f, maxSpeed);
            acceleration = Math.Max(0.01f, acceleration);

            var accelDistance = maxSpeed * maxSpeed / acceleration;
            if (distance >= accelDistance)
            {
                var accelTime = maxSpeed / acceleration;
                var cruiseDistance = distance - accelDistance;
                var cruiseTime = cruiseDistance / maxSpeed;

                if (time <= accelTime)
                {
                    return 0.5f * acceleration * time * time;
                }

                if (time <= accelTime + cruiseTime)
                {
                    var accelPhaseDistance = 0.5f * accelDistance;
                    return accelPhaseDistance + maxSpeed * (time - accelTime);
                }

                var decelElapsed = time - accelTime - cruiseTime;
                var decelStart = 0.5f * accelDistance + cruiseDistance;
                return decelStart + maxSpeed * decelElapsed - 0.5f * acceleration * decelElapsed * decelElapsed;
            }

            var peakTime = (float)Math.Sqrt(distance / acceleration);
            if (time <= peakTime)
            {
                return 0.5f * acceleration * time * time;
            }

            var decelTime = time - peakTime;
            var peakDisplacement = distance * 0.5f;
            var peakVelocity = acceleration * peakTime;
            return peakDisplacement + peakVelocity * decelTime - 0.5f * acceleration * decelTime * decelTime;
        }

        private static float ResolveVerticalSpeed(IStackerKinematics kinematics, bool isLoaded) =>
            isLoaded
                ? kinematics.VerticalSpeedLoadedMetersPerSecond
                : kinematics.VerticalSpeedMetersPerSecond;

        private static float ResolveDepthSpeed(IStackerKinematics kinematics, bool isLoaded) =>
            isLoaded
                ? kinematics.DepthSpeedLoadedMetersPerSecond
                : kinematics.DepthSpeedMetersPerSecond;
    }
}
