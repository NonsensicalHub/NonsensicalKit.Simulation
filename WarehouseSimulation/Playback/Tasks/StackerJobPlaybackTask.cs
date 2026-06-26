using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Playback.Tasks
{
    /// <summary>堆垛机宏观任务：入库为取货点→货位，出库为货位→取货点（不含取/移/放子步骤细节）。</summary>
    public struct StackerJobPlaybackTask
    {
        public int JobId;
        public int StackerId;
        public int PickupPointIndex;
        public GridIndex TargetSlot;
        public double TaskStartSimTime;
        public double TaskEndSimTime;
        public bool IsOutbound;
        public GridIndex IdleStackerSlot;
        public bool HasIdleStackerSlot;
    }

    public enum StackerJobMacroPhase
    {
        None,
        Waiting,
        Approaching,
        Picking,
        Moving,
        Placing,
    }

    /// <summary>宏观任务在某一仿真时刻的执行上下文（由上层从子任务时间轴解析）。</summary>
    public struct StackerJobPlaybackContext
    {
        public StackerJobPlaybackTask Task;
        public StackerJobMacroPhase Phase;
        public float PhaseProgress;
        public double SimTime;
        public SimSubTask ActiveSubTask;
        public bool HasActiveSubTask;
    }
}
