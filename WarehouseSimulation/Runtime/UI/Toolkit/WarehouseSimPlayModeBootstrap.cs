using UnityEngine;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
/// <summary>
/// 独立仿真场景用。默认等界面上的仿真按钮；勾选后才会在 Play 时自动跑一次。
/// </summary>
public sealed class WarehouseSimPlayModeBootstrap : MonoBehaviour
{
    [SerializeField] private WarehouseSimRunner m_runner;
    [SerializeField] private bool m_runOnStart;

    private void Start()
    {
        if (!m_runOnStart || m_runner == null)
        {
            return;
        }

        m_runner.RunSimulation();
    }
}
}
