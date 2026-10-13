namespace NonsensicalKit.Simulation.WarehouseSimulation.Allocation
{
    /// <summary>按仓库号取该库的货架网格。</summary>
    internal interface IWarehouseGridLookup
    {
        bool TryGetGrid(int warehouseId, out WarehouseGridConfig grid);
    }
}
