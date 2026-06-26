namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>单任务子任务时间轴自检问题类型。</summary>
    public enum SimSubTaskTimelineIssueKind
    {
        /// <summary>同一任务的两条子任务时间区间重叠。</summary>
        Overlap,

        /// <summary>任务时间轴上存在无任何子任务覆盖的空白时段。</summary>
        Gap,
    }
}
