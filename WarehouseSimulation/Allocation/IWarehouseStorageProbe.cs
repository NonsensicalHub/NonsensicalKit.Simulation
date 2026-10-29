namespace NonsensicalKit.Simulation.WarehouseSimulation.Allocation
{
    /// <summary>按仓库号查看是否还有空货位或已有库存，不扫描货格。</summary>
    public interface IWarehouseStorageProbe
    {
        bool HasFreeStorage(int warehouseId);

        bool HasStoredCargo(int warehouseId);
    }
}
