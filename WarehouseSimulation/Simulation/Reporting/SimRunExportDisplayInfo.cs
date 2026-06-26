using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>报告导出展示用名称；须在主线程从 Unity 对象捕获后再传入后台导出。</summary>
    public readonly struct SimRunExportDisplayInfo
    {
        public string ScenarioName { get; }
        public string HardwareName { get; }
        public string StrategyName { get; }
        public string ConveyorMapName { get; }

        public SimRunExportDisplayInfo(
            string scenarioName,
            string hardwareName,
            string strategyName,
            string conveyorMapName)
        {
            ScenarioName = scenarioName;
            HardwareName = hardwareName;
            StrategyName = strategyName;
            ConveyorMapName = conveyorMapName;
        }

        public static SimRunExportDisplayInfo Capture(WarehouseSimScenario scenario)
        {
            if (scenario == null)
            {
                return default;
            }

            var map = scenario.ResolvedHardwareBindings?.ConveyorMap;
            return new SimRunExportDisplayInfo(
                scenario.name,
                scenario.Hardware != null ? scenario.Hardware.name : null,
                scenario.Strategy != null ? scenario.Strategy.name : null,
                map != null ? map.name : null);
        }
    }
}
