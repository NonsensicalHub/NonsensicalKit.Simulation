using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>仓库离散事件仿真器：运行后可提供回放与子任务时间轴。</summary>
    public interface IPlaybackCapableWarehouseSimulator
    {
        /// <summary>按场景配置执行一次完整仿真，返回统计与完成记录。</summary>
        SimRunResult Run(WarehouseSimScenario scenario, SimRunOptions? runOptions = null);

        /// <summary>最近一次 <c>Run</c> 产生的按时间排序的回放事件。</summary>
        IReadOnlyList<SimPlaybackEvent> LastPlaybackEvents { get; }

        /// <summary>最近一次 <c>Run</c> 产生的子任务时间轴（含开始/结束时刻）。</summary>
        IReadOnlyList<SimSubTask> LastSubTasks { get; }
    }
}
