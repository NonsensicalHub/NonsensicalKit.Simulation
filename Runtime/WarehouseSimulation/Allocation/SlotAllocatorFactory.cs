using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Allocation
{
    /// <summary>根据 Scenario 与网格配置创建并初始化货位分配器。</summary>
    public static class SlotAllocatorFactory
    {
        public static DefaultSlotAllocator Create(WarehouseSimScenario scenario, WarehouseGridConfig grid)
        {
            var allocator = new DefaultSlotAllocator();
            allocator.Configure(grid);

            var initialOccupied = DefaultSlotAllocator.BuildInitialOccupancy(
                grid,
                scenario.ResolvedHardwareBindings,
                scenario.InitialOccupancyRatio,
                scenario.InitialOccupancyRandom,
                scenario.FlowRandomSeed);

            allocator.Reset(
                scenario.ResolvedHardwareBindings,
                initialOccupied,
                scenario.ResolvedStrategy.StackerPlacementStrategy);

            return allocator;
        }

        /// <summary>按场景配置生成初始占用货位列表（与仿真播种一致，供回放写入事件源）。</summary>
        public static List<GridIndex> BuildInitialOccupiedSlots(
            WarehouseSimScenario scenario,
            WarehouseGridConfig grid)
        {
            var bitmap = DefaultSlotAllocator.BuildInitialOccupancy(
                grid,
                scenario.ResolvedHardwareBindings,
                scenario.InitialOccupancyRatio,
                scenario.InitialOccupancyRandom,
                scenario.FlowRandomSeed);
            return SlotGridUtility.EnumerateOccupiedStorageSlots(grid, bitmap, scenario.ResolvedHardwareBindings);
        }
    }
}
