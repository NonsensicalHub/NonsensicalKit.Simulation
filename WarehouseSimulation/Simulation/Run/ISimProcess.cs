using NonsensicalKit.Simulation.WarehouseSimulation.Core;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>仿真主循环里的一个阶段。引擎按事件类型只交给一个进程。</summary>
    internal interface ISimProcess
    {
        bool Handles(SimEventType type);

        void OnEvent(ScheduledSimEvent evt);
    }
}
