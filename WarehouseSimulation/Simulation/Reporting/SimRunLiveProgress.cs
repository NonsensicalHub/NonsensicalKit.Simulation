using System.Collections.Generic;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>
    /// 后台仿真线程写入、主线程读取的进行中快照。字段由仿真线程更新，心跳日志只读一份瞬时值。
    /// </summary>
    internal sealed class SimRunLiveProgress
    {
        private readonly Stack<string> _phases = new();

        public string Phase = "启动";
        public double SimTimeSeconds;
        public int EventCount;
        public int CompletedJobs;
        public int TargetJobs;
        public int CompletedInboundJobs;
        public int TargetInboundJobs;
        public int QueuedEvents;

        public void Enter(string phase)
        {
            if (string.IsNullOrEmpty(phase))
            {
                phase = "—";
            }

            _phases.Push(Phase);
            Phase = phase;
        }

        public void Leave()
        {
            Phase = _phases.Count > 0 ? _phases.Pop() : "—";
        }

        public void ReportTick(
            double simTime,
            int events,
            int completed,
            int target,
            int queued,
            int completedInbound,
            int targetInbound)
        {
            SimTimeSeconds = simTime;
            EventCount = events;
            CompletedJobs = completed;
            TargetJobs = target;
            CompletedInboundJobs = completedInbound;
            TargetInboundJobs = targetInbound;
            QueuedEvents = queued;
        }

        public string Format()
        {
            var phase = Phase ?? "—";
            return
                $"阶段 {phase}，仿真时刻 {SimTimeSeconds:F1}s，" +
                $"完成 {CompletedJobs}/{TargetJobs}，事件 {EventCount}，队列 {QueuedEvents}";
        }
    }
}
