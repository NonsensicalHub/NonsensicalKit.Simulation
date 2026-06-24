using UnityEngine;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Playback
{
    /// <summary>在 Inspector 中挂载具体事件处理器（仓库、输送线、堆垛机、日志等）。</summary>
    public abstract class SimPlaybackEventHandlerBehaviour : MonoBehaviour
    {
        /// <summary>处理一条回放事件（可选，用于调试日志等）。</summary>
        public virtual void OnPlaybackEvent(Model.SimPlaybackEvent evt)
        {
        }

        /// <summary>
        /// 按仿真时刻求值场景状态（用于进度条跳转与子任务时间轴驱动）。
        /// 默认无操作；具体 Handler 可覆盖以支持任意时刻恢复。
        /// </summary>
        public virtual void OnPlaybackEvaluate(double simTime, System.Collections.Generic.IReadOnlyList<Model.SimSubTask> subTasks)
        {
        }

        /// <summary>新一轮回放开始前重置 Handler 内部状态与场景可视。</summary>
        public virtual void ResetPlaybackState()
        {
        }
    }
}
