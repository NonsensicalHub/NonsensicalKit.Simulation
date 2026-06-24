using UnityEngine;
using NaughtyAttributes;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>
    /// 一次仿真任务：引用硬件绑定、策略配置与流程计划。
    /// 初始占用由 <see cref="InitialOccupancyRatio"/> 与 <see cref="InitialOccupancyRandom"/> 指定。
    /// </summary>
    [CreateAssetMenu(fileName = "WarehouseSimScenario", menuName = "Warehouse Simulation/Scenario")]
    public class WarehouseSimScenario : ScriptableObject
    {
        [Label("硬件绑定")]
        [Tooltip("测试场景使用 Test Bindings Asset；正式部署替换为对接模块的实现。")]
        public WarehouseSimulationBindingsAsset Hardware;

        [Label("策略配置")]
        public WarehouseSimStrategyProfile Strategy;

        [Label("流程计划")]
        [Tooltip("出入库任务调度：每段指定流向、数量与释放节奏。至少配置一段；留空时运行时使用默认（100 箱瞬间入库）。")]
        public SimFlowPlanEntry[] FlowPlan = { new() { Quantity = 100 } };

        [Label("流程随机种子")]
        [Tooltip("随机间隔模式下使用，固定种子保证可复现。")]
        public int FlowRandomSeed = 42;

        [Range(0f, 1f)]
        [Label("初始占用率")]
        [Tooltip("仿真开始时货架可存储货位的占用比例。0 表示全空，1 表示全满。")]
        public float InitialOccupancyRatio = 0f;

        [Label("随机占用")]
        [Tooltip("为 true 时随机选取占用率对应数量的货位；为 false 时按遍历顺序填充。")]
        public bool InitialOccupancyRandom = false;

        /// <summary>策略配置；未指定时返回运行时默认策略。</summary>
        public WarehouseSimStrategyProfile ResolvedStrategy =>
            Strategy != null ? Strategy : SimStrategyDefaults.Instance;

        /// <summary>硬件绑定契约；资产须实现 <see cref="IWarehouseSimulationBindings"/>。</summary>
        public IWarehouseSimulationBindings ResolvedHardwareBindings =>
            Hardware as IWarehouseSimulationBindings;

        /// <summary>在启动后台仿真前于主线程解析策略，避免工作线程创建 ScriptableObject。</summary>
        public void EnsureResolvedOnMainThread()
        {
            if (Strategy == null)
            {
                SimStrategyDefaults.EnsureCreatedOnMainThread();
            }

            ResolvedHardwareBindings?.EnsureSlotPositionsLoaded();
        }
    }
}
