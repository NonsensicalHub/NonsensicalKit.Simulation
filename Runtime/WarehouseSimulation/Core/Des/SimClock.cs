namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>
    /// 离散事件仿真的逻辑时钟（单位：秒）。
    /// </summary>
    /// <remarks>
    /// 仅随事件处理向前跳跃，不在事件之间连续积分；与 Unity 的 <c>Time.time</c> 完全独立。
    /// <see cref="AdvanceTo"/> 保证单调不减，允许同刻多个事件（由 <see cref="SimEventQueue"/> 排序处理顺序）。
    /// </remarks>
    public sealed class SimClock
    {
        /// <summary>当前仿真时刻。</summary>
        public double Now { get; private set; }

        /// <summary>将时钟归零（新一轮 <c>Run</c> 前调用）。</summary>
        public void Reset() => Now = 0;

        /// <summary>
        /// 将时钟推进到 <paramref name="time"/>（若更早则保持不变）。
        /// </summary>
        /// <param name="time">即将处理的事件时刻。</param>
        public void AdvanceTo(double time)
        {
            if (time > Now)
            {
                Now = time;
            }
        }
    }
}
