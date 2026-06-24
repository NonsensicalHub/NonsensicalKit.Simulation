using UnityEngine;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>
    /// 硬件绑定的 ScriptableObject 基类，供 Scenario / Inspector 引用。
    /// 子类须实现 <see cref="IWarehouseSimulationBindings"/>。
    /// </summary>
    public abstract class WarehouseSimulationBindingsAsset : ScriptableObject
    {
    }
}
