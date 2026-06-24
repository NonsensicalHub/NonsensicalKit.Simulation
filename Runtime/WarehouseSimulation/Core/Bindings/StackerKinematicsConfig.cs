using System;
using NaughtyAttributes;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>堆垛机运动学参数。</summary>
    [Serializable]
    public sealed class StackerKinematicsConfig : IStackerKinematics
    {
        [Label("取货时间（秒）")]
        public float PickSeconds = 8f;

        [Label("放货时间（秒）")]
        public float PlaceSeconds = 8f;

        [Label("水平速度（米/秒）")]
        public float HorizontalSpeedMetersPerSecond = 3f;

        [Label("空载垂直速度（米/秒）")]
        public float VerticalSpeedMetersPerSecond = 1.2f;

        [Label("载货垂直速度（米/秒）")]
        public float VerticalSpeedLoadedMetersPerSecond = 0.8f;

        [Label("空载深度速度（米/秒）")]
        public float DepthSpeedMetersPerSecond = 3f;

        [Label("载货深度速度（米/秒）")]
        public float DepthSpeedLoadedMetersPerSecond = 2f;

        [Label("垂直加速度（米/秒²）")]
        public float VerticalAccelerationMetersPerSecondSquared = 0.5f;

        [Label("深度加速度（米/秒²）")]
        public float DepthAccelerationMetersPerSecondSquared = 1f;

        float IStackerKinematics.PickSeconds => PickSeconds;
        float IStackerKinematics.PlaceSeconds => PlaceSeconds;
        float IStackerKinematics.HorizontalSpeedMetersPerSecond => HorizontalSpeedMetersPerSecond;
        float IStackerKinematics.VerticalSpeedMetersPerSecond => VerticalSpeedMetersPerSecond;
        float IStackerKinematics.DepthSpeedMetersPerSecond => DepthSpeedMetersPerSecond;
        float IStackerKinematics.VerticalSpeedLoadedMetersPerSecond => VerticalSpeedLoadedMetersPerSecond;
        float IStackerKinematics.DepthSpeedLoadedMetersPerSecond => DepthSpeedLoadedMetersPerSecond;
        float IStackerKinematics.VerticalAccelerationMetersPerSecondSquared =>
            VerticalAccelerationMetersPerSecondSquared;
        float IStackerKinematics.DepthAccelerationMetersPerSecondSquared =>
            DepthAccelerationMetersPerSecondSquared;

        public static StackerKinematicsConfig CreateDefault() => new();
    }
}
