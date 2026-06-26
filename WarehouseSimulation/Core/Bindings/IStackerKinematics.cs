namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>堆垛机运动学参数；正式部署由设备/WMS 模块提供。</summary>
    public interface IStackerKinematics
    {
        float PickSeconds { get; }
        float PlaceSeconds { get; }
        float HorizontalSpeedMetersPerSecond { get; }
        float VerticalSpeedMetersPerSecond { get; }
        float DepthSpeedMetersPerSecond { get; }
        float VerticalSpeedLoadedMetersPerSecond { get; }
        float DepthSpeedLoadedMetersPerSecond { get; }
        float VerticalAccelerationMetersPerSecondSquared { get; }
        float DepthAccelerationMetersPerSecondSquared { get; }
    }
}
