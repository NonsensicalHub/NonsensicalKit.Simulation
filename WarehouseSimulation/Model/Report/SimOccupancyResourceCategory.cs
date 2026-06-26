namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>独占资源类型（占用自检分类）。</summary>
    public enum SimOccupancyResourceCategory
    {
        Junction,
        SegmentSlot,
        Infeed,
        Outfeed,
        Pickup,
        ProcessStation,
        VerticalTransfer,
        Unknown,
    }
}
