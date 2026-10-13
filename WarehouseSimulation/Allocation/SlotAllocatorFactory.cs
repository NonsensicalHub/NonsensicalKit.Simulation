using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Allocation
{
    /// <summary>根据 Scenario 与网格配置创建并初始化货位分配器。</summary>
    public static class SlotAllocatorFactory
    {
        public static ISlotAllocator Create(WarehouseSimScenario scenario, WarehouseGridConfig grid)
        {
            var bindings = scenario.ResolvedHardwareBindings;
            if (!WarehouseCatalog.TryResolve(bindings, grid, out var warehouses, out var error))
            {
                throw new System.InvalidOperationException(error);
            }

            if (warehouses.Count == 1)
            {
                var only = warehouses[0];
                return CreateSingle(scenario, only.Grid, only.WarehouseId);
            }

            return MultiWarehouseSlotAllocator.Create(scenario, warehouses);
        }

        /// <summary>按场景配置生成初始占用货位列表（与仿真播种一致，供回放写入事件源）。</summary>
        public static List<GridIndex> BuildInitialOccupiedSlots(
            WarehouseSimScenario scenario,
            WarehouseGridConfig grid)
        {
            var bindings = scenario.ResolvedHardwareBindings;
            if (!WarehouseCatalog.TryResolve(bindings, grid, out var warehouses, out _))
            {
                return new List<GridIndex>();
            }

            var slots = new List<GridIndex>();
            for (var i = 0; i < warehouses.Count; i++)
            {
                var definition = warehouses[i];
                slots.AddRange(Enumerate(scenario, definition.Grid, bindings, definition.WarehouseId));
            }

            return slots;
        }

        private static DefaultSlotAllocator CreateSingle(
            WarehouseSimScenario scenario,
            WarehouseGridConfig grid,
            int warehouseId)
        {
            var allocator = new DefaultSlotAllocator();
            allocator.Configure(grid, warehouseId);

            var initialOccupied = DefaultSlotAllocator.BuildInitialOccupancy(
                grid,
                scenario.ResolvedHardwareBindings,
                scenario.InitialOccupancyRatio,
                scenario.InitialOccupancyRandom,
                scenario.FlowRandomSeed,
                warehouseId);

            allocator.Reset(
                scenario.ResolvedHardwareBindings,
                initialOccupied,
                scenario.ResolvedStrategy.StackerPlacementStrategy);

            return allocator;
        }

        private static List<GridIndex> Enumerate(
            WarehouseSimScenario scenario,
            WarehouseGridConfig grid,
            IWarehouseSimulationBindings bindings,
            int warehouseId)
        {
            var bitmap = DefaultSlotAllocator.BuildInitialOccupancy(
                grid,
                bindings,
                scenario.InitialOccupancyRatio,
                scenario.InitialOccupancyRandom,
                scenario.FlowRandomSeed,
                warehouseId);
            return SlotGridUtility.EnumerateOccupiedStorageSlots(grid, bitmap, bindings, warehouseId);
        }
    }
}
