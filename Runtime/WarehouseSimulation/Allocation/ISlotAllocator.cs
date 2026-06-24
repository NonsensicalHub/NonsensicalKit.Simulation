using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Allocation
{
    /// <summary>货位分配策略：维护占用位图并选取空货位。</summary>
    public interface ISlotAllocator
    {
        bool[] Occupied { get; }

        int TotalFreeCount { get; }

        int PhysicalSlotCount { get; }

        int StorageSlotCount { get; }

        /// <summary>货位布局描述（默认实现返回网格尺寸）。</summary>
        string SlotLayoutDescription { get; }

        StackerSlotPlacementStrategy PlacementStrategy { get; }

        void Reset(
            IWarehouseSimulationBindings bindings,
            bool[] initialOccupied = null,
            StackerSlotPlacementStrategy placementStrategy = StackerSlotPlacementStrategy.NearestToPickup);

        /// <param name="servingStackerId">仅在该堆垛机伸叉列域内分配货位/param>
        /// <param name="pickupColumn">取货点列，供放置策略计算距离</param>
        /// <param name="pickupRow">取货点排</param>
        bool TryAllocateSlotForStacker(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            int servingStackerId,
            int pickupColumn,
            int pickupRow,
            out GridIndex slot);

        /// <summary>是否存在任一堆垛机伸叉范围内仍可分配的空货位。</summary>
        bool HasAllocatableFreeSlot(IWarehouseSimulationBindings bindings, ConveyorMapTopology topology);

        /// <summary>任一堆垛机伸叉范围内、且为可存储的货位总数。</summary>
        int CountAllocatableStorageSlots(IWarehouseSimulationBindings bindings, ConveyorMapTopology topology);

        /// <summary>任一堆垛机伸叉范围内、当前仍空闲的可存储货位数。</summary>
        int CountFreeAllocatableStorageSlots(IWarehouseSimulationBindings bindings, ConveyorMapTopology topology);

        /// <summary>指定堆垛机列域内是否仍有空货位。</summary>
        bool HasAllocatableFreeSlotForStacker(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            int servingStackerId);

        int CountOccupiedStorageSlots(bool[] occupied);

        void Occupy(GridIndex slot);

        void Release(GridIndex slot);

        /// <summary>是否存在任一堆垛机伸叉范围内仍有库存的货位。</summary>
        bool HasRetrievableOccupiedSlot(IWarehouseSimulationBindings bindings, ConveyorMapTopology topology);

        /// <summary>从已占用货位中选取出库源货位（就近取货点）。</summary>
        bool TrySelectOccupiedSlotForStacker(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            int servingStackerId,
            int pickupColumn,
            int pickupRow,
            System.Collections.Generic.HashSet<GridIndex> reservedSlots,
            out GridIndex slot);
    }
}
