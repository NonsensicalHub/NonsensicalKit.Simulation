namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>单次仿真运行选项（由 Runner 配置，与场景任务内容无关）。</summary>
    public struct SimRunOptions
    {
        public bool RecordPlaybackAndSubTasks;
        public bool CollectWallClockProfile;

        public static SimRunOptions Default => new()
        {
            RecordPlaybackAndSubTasks = true,
            CollectWallClockProfile = false,
        };
    }
}
