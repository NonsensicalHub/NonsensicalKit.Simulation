using System;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>
    /// 任务配置档的 JSON 存档模型（与 ScriptableObject 解耦，可在运行时 / Web 读写）。
    /// 硬件绑定与可选策略资源用 Resources 路径引用；策略标量也可内联。
    /// </summary>
    [Serializable]
    public sealed class WarehouseSimScenarioDto
    {
        public string SchemaVersion = "1";

        /// <summary>存档文件名（不含扩展名），稳定 id。</summary>
        public string Id;

        /// <summary>界面显示名；空则用 Id。</summary>
        public string DisplayName;

        /// <summary>硬件绑定 Resources 路径，如 WarehouseSimulation/WarehouseBindings。</summary>
        public string HardwareResourcesPath;

        /// <summary>策略 Resources 路径（可选）。有值则优先加载该资产，再被内联 Strategy 覆盖非默认值。</summary>
        public string StrategyResourcesPath;

        /// <summary>内联策略参数（推荐；不依赖额外策略资产也能跑）。</summary>
        public WarehouseSimStrategyDto Strategy = new();

        public SimFlowPlanEntryDto[] FlowPlan;

        public int FlowRandomSeed = 42;

        public float InitialOccupancyRatio;

        public bool InitialOccupancyRandom;
    }

    [Serializable]
    public sealed class WarehouseSimStrategyDto
    {
        public string StackerPlacementStrategy = "NearestToPickup";
        public string InfeedPortSelectionStrategy = "RoundRobin";
        public string ConveyorRoutingStrategy = "ShortestDistance";
        public float RouteDistanceWeight = 1f;
        public float RouteCongestedCargoWeight = 5f;
        public float RouteTotalCargoWeight = 1f;
    }

    [Serializable]
    public sealed class SimFlowPlanEntryDto
    {
        public string Direction = "Inbound";

        /// <summary>限定仓库号。空或 null 表示不限制。</summary>
        public int[] AllowedWarehouseIds;

        public int Quantity = 100;
        public float StartDelaySeconds;
        public string ScheduleMode = "Instant";
        public int RandomQuantityMin = 1;
        public int RandomQuantityMax = 10;
        public float RandomIntervalMinSeconds = 5f;
        public float RandomIntervalMaxSeconds = 15f;
        public string[] RequiredProcessTags;
    }
}